using System;
using System.Threading.Tasks;
using App.Atlas;
using App.Config;
using CardShare.Contracts.Config;
using App.Game;
using App.Resources;
using App.UI;
using Framework.Log;
using Framework.UI;
using Framework.UI.Core;
using Framework.UI.Dialog;
using Framework.UI.View;
using UnityEngine;

namespace App.UI.Popup
{
    /// <summary>
    /// 通关商店：sellHor 货架、MineHor 已购，点击打开 ShopDetail 购买或出售。
    /// </summary>
    public sealed class BattleShopPopViewModel : ViewModelBase
    {
        private readonly IUIManager _ui;
        private readonly IDialogService _dialogs;
        private ShopDetailViewModel _detail;

        public BattleShopPopViewModel(
            GameSession session,
            IUIManager ui,
            IDialogService dialogs,
            IAtlasService atlas)
        {
            Session = session;
            Atlas = atlas;
            _ui = ui;
            _dialogs = dialogs;
            RefreshGoldNum = new ObservableProperty<string>(session.EffectiveShopRefreshCost.ToString());
            RefreshNum = new ObservableProperty<string>(FormatFreeRefreshNum(session.Run.FreeShopRefreshLeft));
            ShowRefreshNum = new ObservableProperty<bool>(session.Run.FreeShopRefreshLeft > 0);
            GoldText = new ObservableProperty<string>(session.Run.Gold.ToString());
            CarryNum = new ObservableProperty<string>(FormatCarryNum());
            AddNewNum = new ObservableProperty<string>(FormatAddNewNum());
            ShowAddNew = new ObservableProperty<bool>(CanShowAddNew());
            ShopRevision = new ObservableProperty<int>();
            RefreshCommand = new RelayCommand(() => Session.RefreshShopOffers(), () => Session.CanRefreshShop);
            // 买槽格恒可点：PVP 金币不足时点了 Toast 提示，不置灰。
            AddNewCommand = new RelayCommand(UnlockSlot);
            NextStageCommand = new RelayCommand(Leave);
            ReturnToHomeCommand = new RelayCommand(ReturnToHomeFromShop);
        }

        public GameSession Session { get; }

        public IAtlasService Atlas { get; }

        public ObservableProperty<string> RefreshGoldNum { get; }

        public ObservableProperty<string> RefreshNum { get; }

        public ObservableProperty<bool> ShowRefreshNum { get; }

        public ObservableProperty<string> GoldText { get; }

        public ObservableProperty<string> CarryNum { get; }

        /// <summary>解锁下一圣物槽的金币。PVP 显示价格；PVE 看广告解锁为空。</summary>
        public ObservableProperty<string> AddNewNum { get; }

        public ObservableProperty<bool> ShowAddNew { get; }

        /// <summary>买槽格是否显示金币价格：PVP 花金币显示，PVE 看广告解锁隐藏。</summary>
        public bool BuySlotShowsPrice => Session.IsPvp;

        public ObservableProperty<int> ShopRevision { get; }

        public IRelayCommand RefreshCommand { get; }

        public IRelayCommand AddNewCommand { get; }

        public IRelayCommand NextStageCommand { get; }

        /// <summary>返回主页：结束当前对局回 Home。确认/结算/关屏流程在对局面板。</summary>
        public IRelayCommand ReturnToHomeCommand { get; }

        public Sprite GetRelicIcon(RelicConfig relic)
        {
            if (relic == null || string.IsNullOrWhiteSpace(relic.Icon) || Atlas == null)
            {
                return null;
            }

            Atlas.TryGetSprite(ResResourcePaths.RelicAtlas, relic.Icon.Trim(), out var sprite);
            return sprite;
        }

        public async Task OpenDetail(int relicId, bool buying)
        {
            if (relicId <= 0)
            {
                return;
            }

            if (buying && !Session.Run.ShopOfferIds.Contains(relicId))
            {
                return;
            }

            if (!buying && !Session.OwnsRelicConfig(relicId))
            {
                return;
            }

            try
            {
                if (_detail != null)
                {
                    await _ui.Close(_detail);
                    _detail = null;
                }

                var registration = _ui.Registry.GetByViewModelType(typeof(ShopDetailViewModel));
                var vm = (ShopDetailViewModel)_ui.Registry.CreateViewModel(registration);
                vm.Setup(relicId, buying);
                _detail = vm;
                await _ui.Open(vm);
            }
            catch (Exception ex)
            {
                AppLog.Exception(LogChannel.UI, ex);
            }
            finally
            {
                _detail = null;
            }
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
            if (_detail != null)
            {
                var detail = _detail;
                _detail = null;
                _ = _ui.Close(detail);
            }
        }

        private void OnSessionChanged()
        {
            Refresh();
        }

        private void Refresh()
        {
            var freeLeft = Session.Run.FreeShopRefreshLeft;
            RefreshGoldNum.Value = Session.EffectiveShopRefreshCost.ToString();
            RefreshNum.Value = FormatFreeRefreshNum(freeLeft);
            ShowRefreshNum.Value = freeLeft > 0;
            GoldText.Value = Session.Run.Gold.ToString();
            CarryNum.Value = FormatCarryNum();
            AddNewNum.Value = FormatAddNewNum();
            ShowAddNew.Value = CanShowAddNew();
            RefreshCommand.RaiseCanExecuteChanged();
            ShopRevision.Value++;
        }

        private string FormatAddNewNum()
        {
            var cost = Session.PvpNextRelicSlotCost;
            return cost > 0 ? cost.ToString() : string.Empty;
        }

        private bool CanShowAddNew()
        {
            return Session.CanUnlockRelicSlot;
        }

        private async void UnlockSlot()
        {
            try
            {
                // PVP 花金币（金币不足在 Session 里写入 Hint）；PVE 看广告（模拟发放）。
                if (!await Session.TryUnlockRelicSlotAsync() && !string.IsNullOrEmpty(Session.Hint))
                {
                    Toast.Show(Session.Hint);
                }
            }
            catch (Exception ex)
            {
                AppLog.Exception(LogChannel.UI, ex);
            }
        }

        private string FormatCarryNum()
        {
            var current = Session.Run.RelicConfigIds != null ? Session.Run.RelicConfigIds.Count : 0;
            return $"当前圣物数量{current}/{Session.RelicCarryMax}";
        }

        private static string FormatFreeRefreshNum(int freeLeft)
        {
            return $"免费{freeLeft}次";
        }

        private void Leave()
        {
            if (_detail != null)
            {
                var detail = _detail;
                _detail = null;
                _ = _ui.Close(detail);
            }

            Session.LeaveShop();
            _ = _dialogs.CloseWithResult(true);
        }

        private async void ReturnToHomeFromShop()
        {
            try
            {
                if (_detail != null)
                {
                    var detail = _detail;
                    _detail = null;
                    await _ui.Close(detail);
                }

                // 确认、结算、PVP 离场、关 GameTable 都在对局面板：它持有屏幕关闭权与既有退出流程。
                var table = UnityEngine.Object.FindObjectOfType<GameUIView>()?.ViewModel;
                if (table != null)
                {
                    await table.ReturnToHomeFromShopAsync();
                }
            }
            catch (Exception ex)
            {
                AppLog.Exception(LogChannel.UI, ex);
            }
        }
    }
}
