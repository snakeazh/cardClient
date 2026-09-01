using System.Threading.Tasks;
using App.UI.Popup;
using Framework.UI;
using Framework.UI.Core;
using Framework.UI.Navigation;
using Framework.UI.View;

namespace App.UI
{
    /// <summary>
    /// 主界面底栏：切换 Home / 图鉴 / 天赋。常驻 Navigation 层，进关卡/对局时只隐藏图层。
    /// </summary>
    public sealed class NavigationViewModel : ViewModelBase
    {
        private readonly IUIManager _ui;
        private IllustratedBookPopViewModel _book;
        private TalentPopupViewModel _talent;
        private bool _busy;

        /// <summary>导航栏主动关闭弹窗期间置位：抑制弹窗 OnClose 回调里的"回主页"重置，避免覆盖新页签状态。</summary>
        private bool _suppressCloseNotify;

        public NavigationViewModel(IUIManager ui)
        {
            _ui = ui;
            AdventureOn = new ObservableProperty<bool>(true);
            CollectOn = new ObservableProperty<bool>(false);
            TalentOn = new ObservableProperty<bool>(false);
        }

        public ObservableProperty<bool> AdventureOn { get; }

        public ObservableProperty<bool> CollectOn { get; }

        public ObservableProperty<bool> TalentOn { get; }

        public async Task EnsureShown()
        {
            if (!IsOpen)
            {
                await _ui.Open(this);
            }

            SetLayerVisible(true);
        }

        public void HideBar()
        {
            SetLayerVisible(false);
        }

        public async void ShowHome()
        {
            await ShowHomeAsync();
        }

        public async void ShowIllustratedBook()
        {
            await ShowIllustratedBookAsync();
        }

        public async void ShowTalent()
        {
            await ShowTalentAsync();
        }

        public async Task ShowHomeAsync()
        {
            if (_busy)
            {
                return;
            }

            _busy = true;
            try
            {
                AdventureOn.Value = true;
                CollectOn.Value = false;
                TalentOn.Value = false;
                await CloseBook();
                await CloseTalent();
            }
            finally
            {
                _busy = false;
            }
        }

        public async Task ShowIllustratedBookAsync()
        {
            if (_busy)
            {
                return;
            }

            if (_book != null && _book.IsOpen)
            {
                CollectOn.Value = true;
                AdventureOn.Value = false;
                TalentOn.Value = false;
                return;
            }

            _busy = true;
            try
            {
                AdventureOn.Value = false;
                CollectOn.Value = true;
                TalentOn.Value = false;
                await CloseTalent();
                var registration = _ui.Registry.GetByViewModelType(typeof(IllustratedBookPopViewModel));
                _book = (IllustratedBookPopViewModel)_ui.Registry.CreateViewModel(registration);
                await _ui.Open(_book);
            }
            finally
            {
                _busy = false;
            }
        }

        public async Task ShowTalentAsync()
        {
            if (_busy)
            {
                return;
            }

            if (_talent != null && _talent.IsOpen)
            {
                TalentOn.Value = true;
                AdventureOn.Value = false;
                CollectOn.Value = false;
                return;
            }

            _busy = true;
            try
            {
                AdventureOn.Value = false;
                CollectOn.Value = false;
                TalentOn.Value = true;
                await CloseBook();
                var registration = _ui.Registry.GetByViewModelType(typeof(TalentPopupViewModel));
                _talent = (TalentPopupViewModel)_ui.Registry.CreateViewModel(registration);
                await _ui.Open(_talent);
            }
            finally
            {
                _busy = false;
            }
        }

        /// <summary>图鉴关闭回调（OnClose）。弹窗自行关闭（点遮罩/关闭按钮）时回主页；
        /// 由导航栏切页主动关闭时被抑制，保持新页签的选中状态。</summary>
        public void NotifyBookClosed()
        {
            _book = null;
            if (_suppressCloseNotify)
            {
                return;
            }

            AdventureOn.Value = true;
            CollectOn.Value = false;
            TalentOn.Value = false;
        }

        private async Task CloseBook()
        {
            if (_book == null)
            {
                return;
            }

            var book = _book;
            _book = null;
            if (book.IsOpen)
            {
                await CloseByNavigation(book);
            }
        }

        private async Task CloseTalent()
        {
            if (_talent == null)
            {
                return;
            }

            var talent = _talent;
            _talent = null;
            if (talent.IsOpen)
            {
                await CloseByNavigation(talent);
            }
        }

        private async Task CloseByNavigation(ViewModelBase viewModel)
        {
            _suppressCloseNotify = true;
            try
            {
                await _ui.Close(viewModel);
            }
            finally
            {
                _suppressCloseNotify = false;
            }
        }

        private void SetLayerVisible(bool visible)
        {
            if (_ui?.Root == null)
            {
                return;
            }

            _ui.Root.GetLayer(UILayer.Navigation).gameObject.SetActive(visible);
        }
    }
}
