using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using App.Energy;
using App.Game;
using App.Wallet;
using Framework.UI;
using Framework.UI.Navigation;
using Framework.UI.View;
using UnityEngine;

namespace App.UI
{
    /// <summary>
    /// 常驻资源栏。挂 Resource 层，进 Home 后 EnsureShown，之后不关闭。
    /// 局外显示钱包金币与体力，局内只显示局内闯关金币。
    /// 图标由 MainResourceView 手动引用后经 SetIcons 注入（Common 目录不打图集）。
    /// </summary>
    public sealed class MainResourceViewModel : ViewModelBase
    {
        private readonly IUIManager _ui;
        private readonly IWalletService _wallet;
        private readonly IEnergyService _energy;
        private readonly GameSession _session;
        private readonly ResourceSlot _gold;
        private readonly ResourceSlot _energySlot;
        private readonly ResourceSlot _runGold;
        private readonly ResourceSlot[] _slots;

        public MainResourceViewModel(
            IUIManager ui,
            IWalletService wallet,
            IEnergyService energy,
            GameSession session)
        {
            _ui = ui ?? throw new ArgumentNullException(nameof(ui));
            _wallet = wallet ?? throw new ArgumentNullException(nameof(wallet));
            _energy = energy ?? throw new ArgumentNullException(nameof(energy));
            _session = session ?? throw new ArgumentNullException(nameof(session));
            _gold = new ResourceSlot(ResourceKind.Gold);
            _energySlot = new ResourceSlot(ResourceKind.Energy);
            _runGold = new ResourceSlot(ResourceKind.RunGold);
            _runGold.Visible.Value = false;
            _slots = new[] { _gold, _energySlot, _runGold };
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

        protected override Task OnOpen(object args)
        {
            _wallet.Changed += OnWalletChanged;
            _energy.Changed += OnEnergyChanged;
            _session.Changed += OnSessionChanged;
            RefreshGold();
            RefreshEnergy();
            RefreshRunGold();
            SetInRun(false);
            return Task.CompletedTask;
        }

        public void SetInRun(bool inRun)
        {
            _gold.Visible.Value = !inRun;
            _energySlot.Visible.Value = !inRun;
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
            _energy.Changed -= OnEnergyChanged;
            _session.Changed -= OnSessionChanged;
        }

        private void OnWalletChanged()
        {
            RefreshGold();
        }

        private void OnEnergyChanged()
        {
            RefreshEnergy();
        }

        private void OnSessionChanged()
        {
            RefreshRunGold();
        }

        private void RefreshGold()
        {
            _gold.Amount.Value = _wallet.Gold.ToString();
        }

        private void RefreshEnergy()
        {
            _energySlot.Amount.Value = _energy.Current.ToString();
        }

        private void RefreshRunGold()
        {
            _runGold.Amount.Value = _session.Run.Gold.ToString();
        }

        /// <summary>View 在 OnBind 时注入手动引用的图标（Common 目录不打图集，不走运行时加载）。</summary>
        public void SetIcons(Sprite goldIcon, Sprite energyIcon)
        {
            if (goldIcon != null)
            {
                _gold.Icon.Value = goldIcon;
                _runGold.Icon.Value = goldIcon;
            }

            if (energyIcon != null)
            {
                _energySlot.Icon.Value = energyIcon;
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
