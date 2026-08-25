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
                if (_book != null)
                {
                    var book = _book;
                    _book = null;
                    if (book.IsOpen)
                    {
                        await _ui.Close(book);
                    }
                }

                if (_talent != null)
                {
                    var talent = _talent;
                    _talent = null;
                    if (talent.IsOpen)
                    {
                        await _ui.Close(talent);
                    }
                }
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

        public void NotifyBookClosed()
        {
            _book = null;
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
                await _ui.Close(book);
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
                await _ui.Close(talent);
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
