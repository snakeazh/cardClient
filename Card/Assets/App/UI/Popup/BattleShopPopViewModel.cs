using System;
using System.Threading.Tasks;
using App.Atlas;
using App.Config;
using App.Game;
using App.Resources;
using Framework.Assets;
using Framework.UI.Core;
using Framework.UI.Dialog;
using Framework.UI.View;
using UnityEngine;

namespace App.UI.Popup
{
    public enum ShopInteractMode
    {
        Idle = 0,
        Buying = 1,
        Selling = 2
    }

    /// <summary>
    /// 通关商店：点击看详情（Tip 跟随鼠标），拖到 buy / Sell 完成购买或出售。
    /// </summary>
    public sealed class BattleShopPopViewModel : ViewModelBase
    {
        private readonly IDialogService _dialogs;
        private bool _dragging;

        public BattleShopPopViewModel(
            GameSession session,
            IDialogService dialogs,
            IResourceService resources,
            IAtlasService atlas)
        {
            Session = session;
            Resources = resources;
            Atlas = atlas;
            _dialogs = dialogs;
            RefreshGoldNum = new ObservableProperty<string>(session.EffectiveShopRefreshCost.ToString());
            GoldText = new ObservableProperty<string>(session.Run.Gold.ToString());
            ShopRevision = new ObservableProperty<int>();
            ShowSellHor = new ObservableProperty<bool>(true);
            ShowMineHor = new ObservableProperty<bool>(true);
            ShowBuy = new ObservableProperty<bool>(false);
            ShowSell = new ObservableProperty<bool>(false);
            ShowTip = new ObservableProperty<bool>(false);
            BuyNum = new ObservableProperty<string>();
            SellNum = new ObservableProperty<string>();
            TipText = new ObservableProperty<string>();
            RefreshCommand = new RelayCommand(() => Session.RefreshShopOffers(), () => Session.CanRefreshShop);
            NextStageCommand = new RelayCommand(Leave);
            CloseCommand = new RelayCommand(Leave);
        }

        public GameSession Session { get; }

        public IResourceService Resources { get; }

        public IAtlasService Atlas { get; }

        public ObservableProperty<string> RefreshGoldNum { get; }

        public ObservableProperty<string> GoldText { get; }

        public ObservableProperty<int> ShopRevision { get; }

        public ObservableProperty<bool> ShowSellHor { get; }

        public ObservableProperty<bool> ShowMineHor { get; }

        public ObservableProperty<bool> ShowBuy { get; }

        public ObservableProperty<bool> ShowSell { get; }

        public ObservableProperty<bool> ShowTip { get; }

        public ObservableProperty<string> BuyNum { get; }

        public ObservableProperty<string> SellNum { get; }

        public ObservableProperty<string> TipText { get; }

        public IRelayCommand RefreshCommand { get; }

        public IRelayCommand NextStageCommand { get; }

        public IRelayCommand CloseCommand { get; }

        public ShopInteractMode Mode { get; private set; }

        public int SelectedRelicId { get; private set; }

        public Sprite GetRelicIcon(RelicConfig relic)
        {
            if (relic == null || string.IsNullOrWhiteSpace(relic.Icon) || Atlas == null)
            {
                return null;
            }

            Atlas.TryGetSprite(ResResourcePaths.RelicAtlas, relic.Icon.Trim(), out var sprite);
            return sprite;
        }

        public void PreviewShopOffer(int relicId)
        {
            Preview(relicId, fromShop: true);
        }

        public void PreviewOwned(int relicId)
        {
            Preview(relicId, fromShop: false);
        }

        public bool BeginDragTrade(int relicId, bool buying)
        {
            if (buying && Session.Run.RelicConfigIds.Count >= GameBalance.MaxRelics)
            {
                return false;
            }

            if (!TryBindRelic(relicId, buying ? ShopInteractMode.Buying : ShopInteractMode.Selling))
            {
                return false;
            }

            _dragging = true;
            ShowTip.Value = false;
            ShowDropZones(buying);
            return true;
        }

        public void EndDragTrade()
        {
            _dragging = false;
            HideDropZones();
            Mode = ShopInteractMode.Idle;
            SelectedRelicId = 0;
            ShowTip.Value = false;
            BuyNum.Value = string.Empty;
            SellNum.Value = string.Empty;
            TipText.Value = string.Empty;
        }

        public void ConfirmBuy()
        {
            if (Mode != ShopInteractMode.Buying || SelectedRelicId <= 0)
            {
                return;
            }

            Session.BuyShopRelic(SelectedRelicId);
        }

        public void ConfirmSell()
        {
            if (Mode != ShopInteractMode.Selling || SelectedRelicId <= 0)
            {
                return;
            }

            Session.SellShopRelic(SelectedRelicId);
        }

        public void HideTip()
        {
            if (_dragging)
            {
                return;
            }

            ClearSelection();
        }

        public void ClearSelection()
        {
            _dragging = false;
            Mode = ShopInteractMode.Idle;
            SelectedRelicId = 0;
            HideDropZones();
            ShowTip.Value = false;
            BuyNum.Value = string.Empty;
            SellNum.Value = string.Empty;
            TipText.Value = string.Empty;
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
            RefreshGoldNum.Value = Session.EffectiveShopRefreshCost.ToString();
            GoldText.Value = Session.Run.Gold.ToString();
            RefreshCommand.RaiseCanExecuteChanged();
            if (SelectedRelicId > 0 && !IsSelectionValid())
            {
                ClearSelection();
            }
            else if (_dragging && SelectedRelicId > 0)
            {
                ShowDropZones(Mode == ShopInteractMode.Buying);
            }
            else
            {
                HideDropZones();
            }

            ShopRevision.Value++;
        }

        private void Preview(int relicId, bool fromShop)
        {
            var mode = fromShop ? ShopInteractMode.Buying : ShopInteractMode.Selling;
            if (Mode == mode && SelectedRelicId == relicId && !_dragging)
            {
                ClearSelection();
                return;
            }

            if (!TryBindRelic(relicId, mode))
            {
                return;
            }

            HideDropZones();
            ShowTip.Value = true;
        }

        private bool TryBindRelic(int relicId, ShopInteractMode mode)
        {
            if (relicId <= 0)
            {
                return false;
            }

            if (mode == ShopInteractMode.Buying && !Session.Run.ShopOfferIds.Contains(relicId))
            {
                return false;
            }

            if (mode == ShopInteractMode.Selling && !Session.OwnsRelicConfig(relicId))
            {
                return false;
            }

            var relic = RelicConfig.Get(relicId);
            if (relic == null)
            {
                return false;
            }

            Mode = mode;
            SelectedRelicId = relicId;
            TipText.Value = relic.Desc ?? string.Empty;
            BuyNum.Value = Session.EffectiveBuyPrice(relicId).ToString();
            SellNum.Value = Session.EffectiveSellPrice(relicId).ToString();
            return true;
        }

        private bool IsSelectionValid()
        {
            if (Mode == ShopInteractMode.Buying)
            {
                return Session.Run.ShopOfferIds.Contains(SelectedRelicId);
            }

            if (Mode == ShopInteractMode.Selling)
            {
                return Session.OwnsRelicConfig(SelectedRelicId);
            }

            return false;
        }

        private void ShowDropZones(bool buying)
        {
            ShowSellHor.Value = buying;
            ShowMineHor.Value = !buying;
            ShowBuy.Value = buying;
            ShowSell.Value = !buying;
        }

        private void HideDropZones()
        {
            ShowSellHor.Value = true;
            ShowMineHor.Value = true;
            ShowBuy.Value = false;
            ShowSell.Value = false;
        }

        private void Leave()
        {
            ClearSelection();
            Session.LeaveShop();
            _ = _dialogs.CloseWithResult(true);
        }
    }
}
