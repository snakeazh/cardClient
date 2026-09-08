using System.Threading.Tasks;
using Framework.UI.Core;
using Framework.UI.Dialog;
using Framework.UI.View;

namespace App.UI.Popup
{
    public sealed class CommonTopArgs
    {
        public CommonTopArgs(string tip, bool showYes = true, bool showNo = true)
        {
            Tip = tip ?? string.Empty;
            ShowYes = showYes;
            ShowNo = showNo;
        }

        public string Tip { get; }

        public bool ShowYes { get; }

        public bool ShowNo { get; }
    }

    /// <summary>
    /// 通用确认：默认「确定退出游戏吗」。也可传入文案（如体力不足）。
    /// </summary>
    public sealed class CommonTopViewModel : ViewModelBase
    {
        private readonly IDialogService _dialogs;

        public CommonTopViewModel(IDialogService dialogs)
        {
            _dialogs = dialogs;
            TipContext = new ObservableProperty<string>("确定退出游戏吗");
            ShowYes = new ObservableProperty<bool>(true);
            ShowNo = new ObservableProperty<bool>(true);
            YesCommand = new RelayCommand(Confirm);
            NoCommand = new RelayCommand(Cancel);
        }

        public ObservableProperty<string> TipContext { get; }

        public ObservableProperty<bool> ShowYes { get; }

        public ObservableProperty<bool> ShowNo { get; }

        public IRelayCommand YesCommand { get; }

        public IRelayCommand NoCommand { get; }

        protected override Task OnOpen(object args)
        {
            ShowYes.Value = true;
            ShowNo.Value = true;
            if (args is CommonTopArgs options)
            {
                if (!string.IsNullOrWhiteSpace(options.Tip))
                {
                    TipContext.Value = options.Tip;
                }

                ShowYes.Value = options.ShowYes;
                ShowNo.Value = options.ShowNo;
            }
            else if (args is string tip && !string.IsNullOrWhiteSpace(tip))
            {
                TipContext.Value = tip;
            }

            return Task.CompletedTask;
        }

        private void Confirm()
        {
            _ = _dialogs.CloseWithResult(true);
        }

        private void Cancel()
        {
            _ = _dialogs.CloseWithResult(false);
        }
    }
}
