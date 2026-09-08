using System;
using System.Threading.Tasks;
using App.Energy;
using Framework.UI.Core;
using Framework.UI.Dialog;
using Framework.UI.View;

namespace App.UI.Popup
{
    /// <summary>
    /// 体力不足弹窗（已改走 CommonTop，本页保留未接入）。
    /// </summary>
    public sealed class EnergyPopupViewModel : ViewModelBase
    {
        private readonly IEnergyService _energy;
        private readonly IDialogService _dialogs;

        public EnergyPopupViewModel(IEnergyService energy, IDialogService dialogs)
        {
            _energy = energy ?? throw new ArgumentNullException(nameof(energy));
            _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
            Info = new ObservableProperty<string>();
            AdInfo = new ObservableProperty<string>();
            ShowAdBtn = new ObservableProperty<bool>();
            AdCommand = new RelayCommand(WatchAd);
            CloseCommand = new RelayCommand(Close);
        }

        public ObservableProperty<string> Info { get; }

        public ObservableProperty<string> AdInfo { get; }

        public ObservableProperty<bool> ShowAdBtn { get; }

        public IRelayCommand AdCommand { get; }

        public IRelayCommand CloseCommand { get; }

        protected override Task OnOpen(object args)
        {
            Refresh();
            return Task.CompletedTask;
        }

        private void Refresh()
        {
            var left = _energy.AdRefillsLeftToday;
            Info.Value = $"体力不足（{_energy.Current}/{_energy.Max}），开局需消耗体力";
            AdInfo.Value = left > 0
                ? $"观看广告补满体力（今日剩余 {left} 次）"
                : "今日广告补充次数已用完，体力每日凌晨自动恢复";
            ShowAdBtn.Value = left > 0;
        }

        private void WatchAd()
        {
            if (_energy.TryRefillByAd())
            {
                _ = _dialogs.CloseWithResult(true);
                return;
            }

            Refresh();
        }

        private void Close()
        {
            _ = _dialogs.CloseWithResult(false);
        }
    }
}
