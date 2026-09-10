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
    /// 商店详情：货架点进来买，已购点进来卖；Mask 关闭。买卖失败弹 Toast，不改底部 Tip。
    /// 购买时可点 VideoBuyBtn 看广告免费拿（广告当前为模拟发放）。
    /// 消耗品在货架显示 buyUseBtn：购买并立刻使用，不播飞入 MineHor；已购栏显示 useBtn。
    /// 购买成功后商品卡飞入 BattleShopPop 的 MineHor；出售先播飞币再立刻关页，金币同时入账。
    /// </summary>
    public sealed class ShopDetailViewModel : ViewModelBase
    {
        private const string DefaultTip = "点击空白处以关闭";
        private readonly IUIManager _ui;
        private bool _awaitingSellFx;
        private bool _awaitingBuyFx;
        private int _pendingSellGold;

        public ShopDetailViewModel(
            GameSession session,
            IUIManager ui,
            IAtlasService atlas)
        {
            Session = session;
            Atlas = atlas;
            _ui = ui;
            NameText = new ObservableProperty<string>();
            DescText = new ObservableProperty<string>();
            PriceText = new ObservableProperty<string>();
            TipText = new ObservableProperty<string>(DefaultTip);
            GoldText = new ObservableProperty<string>("0");
            IconSprite = new ObservableProperty<Sprite>();
            Quality = new ObservableProperty<QualityType>(QualityType.Ordinary);
            ShowBuy = new ObservableProperty<bool>(false);
            ShowSell = new ObservableProperty<bool>(false);
            ShowBuyUse = new ObservableProperty<bool>(false);
            ShowUse = new ObservableProperty<bool>(false);
            CanAffordBuy = new ObservableProperty<bool>(true);
            ButtonsEnabled = new ObservableProperty<bool>(true);
            BuyCommand = new RelayCommand(ConfirmBuy, () => ButtonsEnabled.Value);
            VideoBuyCommand = new RelayCommand(ConfirmVideoBuy, () => ButtonsEnabled.Value);
            BuyUseCommand = new RelayCommand(ConfirmBuyUse, () => ButtonsEnabled.Value);
            SellCommand = new RelayCommand(ConfirmSell, () => ButtonsEnabled.Value);
            UseCommand = new RelayCommand(ConfirmUse, () => ButtonsEnabled.Value);
            CloseCommand = new RelayCommand(Dismiss, () => ButtonsEnabled.Value);
        }

        public GameSession Session { get; }

        public IUIManager Ui => _ui;

        public IResourceService Resources => _ui != null ? _ui.Resources : null;

        public IAtlasService Atlas { get; }

        public int RelicId { get; private set; }

        public bool Buying { get; private set; }

        public ObservableProperty<string> NameText { get; }

        public ObservableProperty<string> DescText { get; }

        public ObservableProperty<string> PriceText { get; }

        public ObservableProperty<string> TipText { get; }

        public ObservableProperty<string> GoldText { get; }

        public ObservableProperty<Sprite> IconSprite { get; }

        public ObservableProperty<QualityType> Quality { get; }

        public ObservableProperty<bool> ShowBuy { get; }

        public ObservableProperty<bool> ShowSell { get; }

        /// <summary>货架消耗品：购买并立刻使用。</summary>
        public ObservableProperty<bool> ShowBuyUse { get; }

        /// <summary>已购消耗品：直接使用。</summary>
        public ObservableProperty<bool> ShowUse { get; }

        public ObservableProperty<bool> CanAffordBuy { get; }

        public ObservableProperty<bool> ButtonsEnabled { get; }

        public IRelayCommand BuyCommand { get; }

        /// <summary>看广告免费购买货架遗物。</summary>
        public IRelayCommand VideoBuyCommand { get; }

        /// <summary>购买并立刻使用，不播飞入。</summary>
        public IRelayCommand BuyUseCommand { get; }

        public IRelayCommand SellCommand { get; }

        public IRelayCommand UseCommand { get; }

        public IRelayCommand CloseCommand { get; }

        public void Setup(int relicId, bool buying)
        {
            RelicId = relicId;
            Buying = buying;
            Apply();
        }

        protected override Task OnOpen(object args)
        {
            Session.Changed -= OnSessionChanged;
            Session.Changed += OnSessionChanged;
            Apply();
            return Task.CompletedTask;
        }

        protected override Task OnClose()
        {
            _awaitingBuyFx = false;
            ReleasePendingSellGold();
            return Task.CompletedTask;
        }

        protected override void OnDispose()
        {
            Session.Changed -= OnSessionChanged;
            _awaitingBuyFx = false;
            ReleasePendingSellGold();
        }

        public void SetBusy(bool busy)
        {
            ButtonsEnabled.Value = !busy;
            BuyCommand.RaiseCanExecuteChanged();
            VideoBuyCommand.RaiseCanExecuteChanged();
            BuyUseCommand.RaiseCanExecuteChanged();
            SellCommand.RaiseCanExecuteChanged();
            UseCommand.RaiseCanExecuteChanged();
            CloseCommand.RaiseCanExecuteChanged();
        }

        public bool TryBeginSell(GameResourceViewModel bar, out int gold)
        {
            gold = 0;
            if (Buying || RelicId <= 0)
            {
                return false;
            }

            if (!Session.OwnsRelicConfig(RelicId))
            {
                ShowFail(string.IsNullOrEmpty(Session.Hint) ? "出售失败" : Session.Hint);
                return false;
            }

            gold = Session.EffectiveSellPrice(RelicId);
            if (gold > 0)
            {
                bar?.HoldGold(gold, refresh: false);
            }

            _awaitingSellFx = true;
            _pendingSellGold = gold;
            var ownedBefore = Session.OwnsRelicConfig(RelicId);
            Session.SellShopRelic(RelicId);
            if (ownedBefore && !Session.OwnsRelicConfig(RelicId))
            {
                return true;
            }

            _awaitingSellFx = false;
            _pendingSellGold = 0;
            if (gold > 0)
            {
                bar?.ReleaseHeldGold(gold);
            }

            ShowFail(string.IsNullOrEmpty(Session.Hint) ? "出售失败" : Session.Hint);
            return false;
        }

        public void CompleteSell(GameResourceViewModel bar, int gold)
        {
            if (gold > 0)
            {
                bar?.ReleaseHeldGold(gold);
            }

            _pendingSellGold = 0;
            _awaitingSellFx = false;
            Dismiss();
        }

        public bool TryBeginBuy(bool watchAd)
        {
            if (!Buying || RelicId <= 0)
            {
                return false;
            }

            if (Session.Run.RelicConfigIds.Count >= Session.RelicCarryMax)
            {
                Toast.Show("遗物已满");
                return false;
            }

            _awaitingBuyFx = true;
            var ownedBefore = Session.OwnsRelicConfig(RelicId);
            if (watchAd)
            {
                Session.WatchAdBuyShopRelic(RelicId);
            }
            else
            {
                Session.BuyShopRelic(RelicId);
            }

            if (Session.OwnsRelicConfig(RelicId) && !ownedBefore)
            {
                return true;
            }

            _awaitingBuyFx = false;
            ShowFail(string.IsNullOrEmpty(Session.Hint) ? "购买失败" : Session.Hint);
            return false;
        }

        public void CompleteBuy()
        {
            _awaitingBuyFx = false;
            Dismiss();
        }

        public bool TryBeginBuyAndUse()
        {
            if (!Buying || RelicId <= 0)
            {
                return false;
            }

            _awaitingBuyFx = true;
            if (Session.TryBuyAndUseShopRelic(RelicId))
            {
                return true;
            }

            _awaitingBuyFx = false;
            ShowFail(string.IsNullOrEmpty(Session.Hint) ? "购买失败" : Session.Hint);
            return false;
        }

        public void TryUse()
        {
            if (Buying || RelicId <= 0)
            {
                return;
            }

            var ownedBefore = Session.OwnsRelicConfig(RelicId);
            Session.UseRelic(RelicId);
            if (ownedBefore && !Session.OwnsRelicConfig(RelicId))
            {
                return;
            }

            ShowFail(string.IsNullOrEmpty(Session.Hint) ? "使用失败" : Session.Hint);
        }

        private void OnSessionChanged()
        {
            if (_awaitingSellFx || _awaitingBuyFx)
            {
                return;
            }

            if (RelicId <= 0)
            {
                Dismiss();
                return;
            }

            if (Buying && !Session.Run.ShopOfferIds.Contains(RelicId))
            {
                Dismiss();
                return;
            }

            if (!Buying && !Session.OwnsRelicConfig(RelicId))
            {
                Dismiss();
                return;
            }

            Apply();
        }

        private void Apply()
        {
            var relic = RelicConfig.Get(RelicId);
            Quality.Value = relic != null ? relic.Type : QualityType.Ordinary;
            NameText.Value = relic != null ? relic.Name : string.Empty;
            DescText.Value = relic != null ? relic.Desc ?? string.Empty : string.Empty;
            IconSprite.Value = GetRelicIcon(relic);
            ShowBuy.Value = Buying;
            ShowSell.Value = !Buying;
            ShowBuyUse.Value = Buying && Session.CanUseRelicNow(RelicId, requireOwned: false);
            ShowUse.Value = !Buying && Session.CanUseRelicNow(RelicId, requireOwned: true);
            TipText.Value = DefaultTip;
            var price = RelicId <= 0
                ? 0
                : (Buying
                    ? Session.EffectiveBuyPrice(RelicId)
                    : Session.EffectiveSellPrice(RelicId));
            PriceText.Value = RelicId <= 0 ? string.Empty : price.ToString();
            CanAffordBuy.Value = !Buying || RelicId <= 0 || RelicMechanics.CanAfford(Session.Run, price);
            GoldText.Value = Session.Run.Gold.ToString();
        }

        private Sprite GetRelicIcon(RelicConfig relic)
        {
            if (relic == null || string.IsNullOrWhiteSpace(relic.Icon) || Atlas == null)
            {
                return null;
            }

            Atlas.TryGetSprite(ResResourcePaths.RelicAtlas, relic.Icon.Trim(), out var sprite);
            return sprite;
        }

        private void ConfirmBuy()
        {
            // View 拦截 BuyBtn 点击并播飞入；命令仅用于 CanExecute。
        }

        private void ConfirmVideoBuy()
        {
            // View 拦截 VideoBuyBtn 点击并播飞入；命令仅用于 CanExecute。
        }

        private void ConfirmBuyUse()
        {
            // View 拦截 buyUseBtn：购买并立刻使用，不播飞入。
        }

        private void ConfirmSell()
        {
            // View 拦截 SellBtn 点击并播飞币；命令仅用于 CanExecute。
        }

        private void ConfirmUse()
        {
            // View 拦截 useBtn。
        }

        private static void ShowFail(string message)
        {
            if (string.IsNullOrEmpty(message))
            {
                return;
            }

            Toast.Show(message);
        }

        private void ReleasePendingSellGold()
        {
            if (_pendingSellGold <= 0)
            {
                return;
            }

            var gold = _pendingSellGold;
            _pendingSellGold = 0;
            _awaitingSellFx = false;
            var bar = GameResourceView.FindOpen()?.ViewModel;
            bar?.ReleaseHeldGold(gold);
        }

        private void Dismiss()
        {
            _ = _ui.Close(this);
        }
    }
}
