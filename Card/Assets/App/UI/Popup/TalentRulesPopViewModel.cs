using System.Text;
using App.Config;
using CardShare.Contracts.Config;
using Framework.UI;
using Framework.UI.Core;
using Framework.UI.View;

namespace App.UI.Popup
{
    /// <summary>
    /// 天赋规则说明：内容取 GameConst.TalentDesc（导出表用 '|' 分隔行，
    /// 单条内残留的 '|' 同样转行），无配置时留空。点 Mask 关闭。
    /// </summary>
    public sealed class TalentRulesPopViewModel : ViewModelBase
    {
        private readonly IUIManager _ui;

        public TalentRulesPopViewModel(IUIManager ui)
        {
            _ui = ui;
            CloseCommand = new RelayCommand(Dismiss);
            DescText = new ObservableProperty<string>(BuildDesc());
        }

        public ObservableProperty<string> DescText { get; }

        public IRelayCommand CloseCommand { get; }

        private static string BuildDesc()
        {
            if (!GameConst.IsLoaded || GameConst.Instance.TalentDesc == null)
            {
                return string.Empty;
            }

            var lines = GameConst.Instance.TalentDesc;
            var builder = new StringBuilder();
            for (var i = 0; i < lines.Length; i++)
            {
                if (i > 0)
                {
                    builder.Append('\n');
                }

                builder.Append(lines[i]?.Replace('|', '\n'));
            }

            return builder.ToString();
        }

        private void Dismiss()
        {
            _ = _ui.Close(this);
        }
    }
}
