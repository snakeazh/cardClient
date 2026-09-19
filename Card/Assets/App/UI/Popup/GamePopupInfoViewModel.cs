using System.Collections.Generic;
using System.Threading.Tasks;
using App.Config;
using CardShare.Contracts.Config;
using App.Game;
using Framework.UI;
using Framework.UI.Core;
using Framework.UI.View;

namespace App.UI.Popup
{
    /// <summary>
    /// 关卡机制说明弹窗：读本关 <see cref="BossEntryConfig"/>，按「名称：描述」列出。
    /// </summary>
    public sealed class GamePopupInfoViewModel : ViewModelBase
    {
        private readonly IUIManager _ui;

        public GamePopupInfoViewModel(GameSession session, IUIManager ui)
        {
            Session = session;
            _ui = ui;
            ListVersion = new ObservableProperty<int>();
            CloseCommand = new RelayCommand(Close);
        }

        public GameSession Session { get; }

        /// <summary>机制列表变化后自增，View 订阅后重刷行。</summary>
        public ObservableProperty<int> ListVersion { get; }

        public IRelayCommand CloseCommand { get; }

        public List<BossEntryConfig> ResolveEntries()
        {
            return BossMechanics.ResolveAll(Session.Run);
        }

        protected override Task OnOpen(object args)
        {
            ListVersion.Value++;
            return Task.CompletedTask;
        }

        private void Close()
        {
            _ = _ui.Close(this);
        }
    }
}
