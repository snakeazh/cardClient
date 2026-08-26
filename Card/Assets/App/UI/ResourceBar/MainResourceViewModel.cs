using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using App.Config;
using App.Game;
using App.Resources;
using App.Wallet;
using Framework.Assets;
using Framework.UI;
using Framework.UI.Navigation;
using Framework.UI.View;
using UnityEngine;

namespace App.UI
{
    /// <summary>
    /// 常驻资源栏。挂 Resource 层，进 Home 后 EnsureShown，之后不关闭。
    /// 第一项局外钱包金币，第二项局内闯关金币；局内只显示局内，局外只显示局外。
    /// </summary>
    public sealed class MainResourceViewModel : ViewModelBase
    {
        private readonly IUIManager _ui;
        private readonly IWalletService _wallet;
        private readonly GameSession _session;
        private readonly IResourceService _resources;
        private readonly ResourceSlot _gold;
        private readonly ResourceSlot _runGold;
        private readonly ResourceSlot[] _slots;

        public MainResourceViewModel(
            IUIManager ui,
            IWalletService wallet,
            GameSession session,
            IResourceService resources)
        {
            _ui = ui ?? throw new ArgumentNullException(nameof(ui));
            _wallet = wallet ?? throw new ArgumentNullException(nameof(wallet));
            _session = session ?? throw new ArgumentNullException(nameof(session));
            _resources = resources ?? throw new ArgumentNullException(nameof(resources));
            _gold = new ResourceSlot(ResourceKind.Gold);
            _runGold = new ResourceSlot(ResourceKind.RunGold);
            _runGold.Visible.Value = false;
            _slots = new[] { _gold, _runGold };
        }

        public IReadOnlyList<ResourceSlot> Slots => _slots;

        public async Task EnsureShown()
        {
            if (!IsOpen)
            {
                await _ui.Open(this);
            }

            SetLayerVisible(true);
        }

        protected override async Task OnOpen(object args)
        {
            _wallet.Changed += OnWalletChanged;
            _session.Changed += OnSessionChanged;
            RefreshGold();
            RefreshRunGold();
            SetInRun(false);
            await LoadGoldIcon();
        }

        public void SetInRun(bool inRun)
        {
            _gold.Visible.Value = !inRun;
            _runGold.Visible.Value = inRun;
        }

        protected override Task OnClose()
        {
            Unsubscribe();
            return Task.CompletedTask;
        }

        protected override void OnDispose()
        {
            Unsubscribe();
        }

        private void Unsubscribe()
        {
            _wallet.Changed -= OnWalletChanged;
            _session.Changed -= OnSessionChanged;
        }

        private void OnWalletChanged()
        {
            RefreshGold();
        }

        private void OnSessionChanged()
        {
            RefreshRunGold();
        }

        private void RefreshGold()
        {
            _gold.Amount.Value = _wallet.Gold.ToString();
        }

        private void RefreshRunGold()
        {
            _runGold.Amount.Value = _session.Run.Gold.ToString();
        }

        private async Task LoadGoldIcon()
        {
            var iconName = GameConst.Instance != null ? GameConst.Instance.GoldIcon : null;
            var sprite = await TryLoadSprite(ResResourcePaths.CommonIcon(iconName));
            if (sprite == null)
            {
                sprite = await TryLoadSprite(ResResourcePaths.CommonIcon("gold"));
            }

            if (sprite != null)
            {
                _gold.Icon.Value = sprite;
                _runGold.Icon.Value = sprite;
            }
        }

        private async Task<Sprite> TryLoadSprite(string key)
        {
            if (string.IsNullOrEmpty(key) || _resources == null)
            {
                return null;
            }

            try
            {
                return await _resources.LoadAsync<Sprite>(key);
            }
            catch (Exception)
            {
                return null;
            }
        }

        private void SetLayerVisible(bool visible)
        {
            if (_ui?.Root == null)
            {
                return;
            }

            _ui.Root.GetLayer(UILayer.Resource).gameObject.SetActive(visible);
        }
    }
}
