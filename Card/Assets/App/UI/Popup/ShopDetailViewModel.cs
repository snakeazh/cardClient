using System.Threading.Tasks;
using App.Atlas;
using App.Config;
using App.Game;
using App.Resources;
using Framework.UI;
using Framework.UI.Core;
using Framework.UI.View;
using UnityEngine;

namespace App.UI.Popup
{
    /// <summary>
    /// 商店详情：货架点进来买，已购点进来卖；Mask 关闭。买卖失败改 Tip 文案，成功后关掉自己。
    /// </summary>
    public sealed class ShopDetailViewModel : ViewModelBase
    {
        private const string DefaultTip = "点击空白处以关闭";
        private readonly IUIManager _ui;

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
            IconSprite = new ObservableProperty<Sprite>();
            Quality = new ObservableProperty<QualityType>(QualityType.Ordinary);
            ShowBuy = new ObservableProperty<bool>(false);
            ShowSell = new ObservableProperty<bool>(false);
            BuyCommand = new RelayCommand(ConfirmBuy);
            SellCommand = new RelayCommand(ConfirmSell);
            CloseCommand = new RelayCommand(Dismiss);
        }

        public GameSession Session { get; }

        public IAtlasService Atlas { get; }

        public int RelicId { get; private set; }

        public bool Buying { get; private set; }

        public ObservableProperty<string> NameText { get; }

        public ObservableProperty<string> DescText { get; }

        public ObservableProperty<string> PriceText { get; }

        public ObservableProperty<string> TipText { get; }

        public ObservableProperty<Sprite> IconSprite { get; }

        public ObservableProperty<QualityType> Quality { get; }

        public ObservableProperty<bool> ShowBuy { get; }

        public ObservableProperty<bool> ShowSell { get; }

        public IRelayCommand BuyCommand { get; }

        public IRelayCommand SellCommand { get; }

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

        protected override void OnDispose()
        {
            Session.Changed -= OnSessionChanged;
        }

        private void OnSessionChanged()
        {
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
            TipText.Value = DefaultTip;
            PriceText.Value = RelicId <= 0
                ? string.Empty
                : (Buying
                    ? Session.EffectiveBuyPrice(RelicId)
                    : Session.EffectiveSellPrice(RelicId)).ToString();
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
            if (!Buying || RelicId <= 0)
            {
                return;
            }

            if (Session.Run.RelicConfigIds.Count >= Session.RelicCarryMax)
            {
                Toast.Show("遗物已满");
                return;
            }

            var ownedBefore = Session.OwnsRelicConfig(RelicId);
            Session.BuyShopRelic(RelicId);
            if (Session.OwnsRelicConfig(RelicId) && !ownedBefore)
            {
                return;
            }

            ShowFail(string.IsNullOrEmpty(Session.Hint) ? "购买失败" : Session.Hint);
        }

        private void ConfirmSell()
        {
            if (Buying || RelicId <= 0)
            {
                return;
            }

            var ownedBefore = Session.OwnsRelicConfig(RelicId);
            Session.SellShopRelic(RelicId);
            if (ownedBefore && !Session.OwnsRelicConfig(RelicId))
            {
                return;
            }

            ShowFail(string.IsNullOrEmpty(Session.Hint) ? "出售失败" : Session.Hint);
        }

        private void ShowFail(string message)
        {
            TipText.Value = message ?? DefaultTip;
        }

        private void Dismiss()
        {
            _ = _ui.Close(this);
        }
    }
}
