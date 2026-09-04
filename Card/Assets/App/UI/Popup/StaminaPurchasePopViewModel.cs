using System;
using System.Threading.Tasks;
using App.AdShop;
using Framework.UI;
using Framework.UI.Core;
using Framework.UI.View;

namespace App.UI.Popup
{
    /// <summary>
    /// 广告商店弹窗：看广告免费领体力/金币，各限购每日 X 次（跨日重置），购买后弹窗不关。
    /// 广告当前为模拟发放。
    /// </summary>
    public sealed class StaminaPurchasePopViewModel : ViewModelBase
    {
        private readonly IAdShopService _shop;
        private readonly IUIManager _ui;
        private readonly ToastService _toast;

        public StaminaPurchasePopViewModel(
            IAdShopService shop,
            IUIManager ui,
            ToastService toast)
        {
            _shop = shop ?? throw new ArgumentNullException(nameof(shop));
            _ui = ui ?? throw new ArgumentNullException(nameof(ui));
            _toast = toast;
            StaminaAmountText = new ObservableProperty<string>($"X{shop.StaminaPerPurchase}");
            StaminaLimitText = new ObservableProperty<string>(LimitText(shop.StaminaPurchasesLeftToday, AdShopBalance.StaminaDailyLimit));
            StaminaBtnText = new ObservableProperty<string>(BtnText(shop.StaminaPurchasesLeftToday));
            CoinAmountText = new ObservableProperty<string>($"X{shop.GoldPerPurchase}");
            CoinLimitText = new ObservableProperty<string>(LimitText(shop.GoldPurchasesLeftToday, AdShopBalance.GoldDailyLimit));
            CoinBtnText = new ObservableProperty<string>(BtnText(shop.GoldPurchasesLeftToday));
            BuyStaminaCommand = new RelayCommand(BuyStamina, () => shop.StaminaPurchasesLeftToday > 0);
            BuyCoinCommand = new RelayCommand(BuyCoin, () => shop.GoldPurchasesLeftToday > 0);
            CloseCommand = new RelayCommand(Close);
        }

        public ObservableProperty<string> StaminaAmountText { get; }

        /// <summary>限购剩余次数，如「限购 (10/10)」。</summary>
        public ObservableProperty<string> StaminaLimitText { get; }

        /// <summary>购买按钮文案：免费 / 已用完。</summary>
        public ObservableProperty<string> StaminaBtnText { get; }

        public ObservableProperty<string> CoinAmountText { get; }

        public ObservableProperty<string> CoinLimitText { get; }

        public ObservableProperty<string> CoinBtnText { get; }

        public IRelayCommand BuyStaminaCommand { get; }

        public IRelayCommand BuyCoinCommand { get; }

        public IRelayCommand CloseCommand { get; }

        /// <summary>随 Dispose 触发一次，入口（资源栏 AddBtn）据此恢复可再次打开。</summary>
        public event Action Closed;

        protected override Task OnOpen(object args)
        {
            _shop.Changed -= OnShopChanged;
            _shop.Changed += OnShopChanged;
            Refresh();
            return Task.CompletedTask;
        }

        protected override Task OnClose()
        {
            _shop.Changed -= OnShopChanged;
            return Task.CompletedTask;
        }

        protected override void OnDispose()
        {
            _shop.Changed -= OnShopChanged;
            Closed?.Invoke();
        }

        private void OnShopChanged()
        {
            Refresh();
            BuyStaminaCommand.RaiseCanExecuteChanged();
            BuyCoinCommand.RaiseCanExecuteChanged();
        }

        private void BuyStamina()
        {
            if (_shop.TryPurchaseStamina())
            {
                _toast?.ShowSuccess($"体力 +{_shop.StaminaPerPurchase}");
                return;
            }

            Refresh();
        }

        private void BuyCoin()
        {
            if (_shop.TryPurchaseGold())
            {
                _toast?.ShowSuccess($"金币 +{_shop.GoldPerPurchase}");
                return;
            }

            Refresh();
        }

        private void Close()
        {
            _ = _ui.Close(this);
        }

        private void Refresh()
        {
            StaminaLimitText.Value = LimitText(_shop.StaminaPurchasesLeftToday, AdShopBalance.StaminaDailyLimit);
            StaminaBtnText.Value = BtnText(_shop.StaminaPurchasesLeftToday);
            CoinLimitText.Value = LimitText(_shop.GoldPurchasesLeftToday, AdShopBalance.GoldDailyLimit);
            CoinBtnText.Value = BtnText(_shop.GoldPurchasesLeftToday);
        }

        private static string LimitText(int left, int total)
        {
            return $"限购 ({left}/{total})";
        }

        private static string BtnText(int left)
        {
            return left > 0 ? "免费" : "已用完";
        }
    }
}
