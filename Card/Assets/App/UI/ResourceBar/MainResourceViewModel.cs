using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using App.Energy;
using App.Game;
using App.UI.Popup;
using App.Wallet;
using Framework.Log;
using Framework.UI;
using Framework.UI.Core;
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
        private StaminaPurchasePopViewModel _shopPop;

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
            OpenShopCommand = new RelayCommand(OpenShop);
        }

        public IReadOnlyList<ResourceSlot> Slots => _slots;

        /// <summary>资源栏 AddBtn：打开广告商店（体力/金币限购）。弹窗已打开时忽略连点——
        /// 框架 OpenCore 对同层已有屏只会隐藏旧实例再叠一个新视图，连点会越叠越深。</summary>
        public IRelayCommand OpenShopCommand { get; }

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

        /// <summary>打开广告商店弹窗（体力/金币限购）。每次新建 VM：旧 VM 随关闭被 Dispose，
        /// 不可复用；VM 的 Closed 回调清引用，之后 AddBtn 才能再次打开。</summary>
        private async void OpenShop()
        {
            if (_shopPop != null)
            {
                return;
            }

            try
            {
                var registration = _ui.Registry.GetByViewModelType(typeof(StaminaPurchasePopViewModel));
                var vm = (StaminaPurchasePopViewModel)_ui.Registry.CreateViewModel(registration);
                vm.Closed += OnShopPopClosed;
                _shopPop = vm;
                await _ui.Open(vm);
            }
            catch (Exception ex)
            {
                AppLog.Exception(LogChannel.UI, ex);
                OnShopPopClosed();
            }
        }

        private void OnShopPopClosed()
        {
            if (_shopPop == null)
            {
                return;
            }

            _shopPop.Closed -= OnShopPopClosed;
            _shopPop = null;
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

        /// <summary>隐藏资源栏所在层（图鉴等整屏页签用），同 Navigation.HideBar 模式。</summary>
        public void HideBar()
        {
            SetLayerVisible(false);
        }

        /// <summary>恢复资源栏（与 HideBar 成对）。</summary>
        public void ShowBar()
        {
            SetLayerVisible(true);
        }
    }
}
