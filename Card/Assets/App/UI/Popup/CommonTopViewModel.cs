using System.Threading.Tasks;
using Framework.UI.Core;
using Framework.UI.Dialog;
using Framework.UI.View;

namespace App.UI.Popup
{
    /// <summary>
    /// 局内退出确认：确定后走失败结算（无复活），取消则继续对局。
    /// </summary>
    public sealed class CommonTopViewModel : ViewModelBase
    {
        private readonly IDialogService _dialogs;

        public CommonTopViewModel(IDialogService dialogs)
        {
            _dialogs = dialogs;
            TipContext = new ObservableProperty<string>("确定退出游戏吗");
            YesCommand = new RelayCommand(Confirm);
            NoCommand = new RelayCommand(Cancel);
        }

        public ObservableProperty<string> TipContext { get; }

        public IRelayCommand YesCommand { get; }

        public IRelayCommand NoCommand { get; }

        protected override Task OnOpen(object args)
        {
            if (args is string tip && !string.IsNullOrWhiteSpace(tip))
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
