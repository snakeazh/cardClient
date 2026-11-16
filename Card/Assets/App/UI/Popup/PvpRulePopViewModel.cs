using Framework.UI;
using Framework.UI.Core;
using Framework.UI.View;

namespace App.UI.Popup
{
    /// <summary>竞技场规则弹窗的关闭操作。</summary>
    public sealed class PvpRulePopViewModel : ViewModelBase
    {
        private readonly IUIManager _ui;

        public PvpRulePopViewModel(IUIManager ui)
        {
            _ui = ui;
            CloseCommand = new RelayCommand(ClosePop);
        }

        public IRelayCommand CloseCommand { get; }

        private void ClosePop()
        {
            _ = _ui.Close(this);
        }
    }
}
