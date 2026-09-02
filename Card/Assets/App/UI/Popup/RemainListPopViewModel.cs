using System.Threading.Tasks;
using App.Atlas;
using App.Config;
using App.Game;
using App.Resources;
using Framework.Assets;
using Framework.UI;
using Framework.UI.Core;
using Framework.UI.View;
using UnityEngine;

namespace App.UI.Popup
{
    /// <summary>
    /// 遗物列表弹窗：读 Session.Run.RelicConfigIds 只展示本局已持有的装备卡。
    /// Session.Changed 后 ListVersion 自增驱动 View 刷新，金币随 ResourceBar 显示。
    /// </summary>
    public sealed class RemainListPopViewModel : ViewModelBase
    {
        private readonly IUIManager _ui;

        public RemainListPopViewModel(
            GameSession session,
            IUIManager ui,
            IAtlasService atlas,
            IResourceService resources)
        {
            Session = session;
            Atlas = atlas;
            Resources = resources;
            _ui = ui;
            GoldText = new ObservableProperty<string>(session.Run.Gold.ToString());
            ListVersion = new ObservableProperty<int>();
            CloseCommand = new RelayCommand(Close);
        }

        public GameSession Session { get; }

        public IAtlasService Atlas { get; }

        /// <summary>供 View 加载 ItemTip 预制体。</summary>
        public IResourceService Resources { get; }

        public ObservableProperty<string> GoldText { get; }

        /// <summary>持有遗物变化后自增，View 订阅后重刷格子。</summary>
        public ObservableProperty<int> ListVersion { get; }

        public IRelayCommand CloseCommand { get; }

        public Sprite GetRelicIcon(RelicConfig relic)
        {
            if (relic == null || string.IsNullOrWhiteSpace(relic.Icon) || Atlas == null)
            {
                return null;
            }

            Atlas.TryGetSprite(ResResourcePaths.RelicAtlas, relic.Icon.Trim(), out var sprite);
            return sprite;
        }

        protected override Task OnOpen(object args)
        {
            Session.Changed -= OnSessionChanged;
            Session.Changed += OnSessionChanged;
            Refresh();
            return Task.CompletedTask;
        }

        protected override void OnDispose()
        {
            Session.Changed -= OnSessionChanged;
        }

        private void OnSessionChanged()
        {
            Refresh();
        }

        private void Refresh()
        {
            GoldText.Value = Session.Run.Gold.ToString();
            ListVersion.Value++;
        }

        private void Close()
        {
            _ = _ui.Close(this);
        }
    }
}
