using System;
using System.Threading.Tasks;
using Framework.Log;
using Framework.UI;
using Framework.UI.Core;
using Framework.UI.View;
using UnityEngine;

namespace App.UI.Popup
{
    public sealed class RankShowPopArgs
    {
        public string RankName { get; set; }
        public Sprite PreviousBadge { get; set; }
        public Sprite CurrentBadge { get; set; }
        public Sprite NextBadge { get; set; }
        public int GoldAmount { get; set; }
        public bool IsClaimed { get; set; }

        /// <summary>由调用方完成服务端领取，确认成功后返回 true。</summary>
        public Func<Task<bool>> ClaimGoldAsync { get; set; }
    }

    public sealed class RankShowPopViewModel : ViewModelBase
    {
        private bool _claiming;
        private bool _claimed;

        public RankShowPopViewModel(IUIManager ui)
        {
            CloseCommand = new RelayCommand(() => _ = ui.Close(this));
            ClaimCommand = new RelayCommand(ClaimGold, CanClaim);
        }

        public IRelayCommand CloseCommand { get; }
        public IRelayCommand ClaimCommand { get; }
        public ObservableProperty<RankShowPopArgs> Data { get; } = new ObservableProperty<RankShowPopArgs>();
        public ObservableProperty<string> ClaimLabel { get; } = new ObservableProperty<string>("领取金币");

        protected override Task OnOpen(object args)
        {
            var data = args as RankShowPopArgs;
            _claimed = data?.IsClaimed == true;
            Data.Value = data;
            RefreshClaim();
            return Task.CompletedTask;
        }

        private bool CanClaim()
        {
            var data = Data.Value;
            return IsOpen && !_claiming && !_claimed && data != null &&
                data.GoldAmount > 0 && data.ClaimGoldAsync != null;
        }

        private async void ClaimGold()
        {
            _claiming = true;
            RefreshClaim();
            try
            {
                _claimed = await Data.Value.ClaimGoldAsync();
                if (IsOpen && !_claimed)
                {
                    Toast.Error("金币领取失败，请重试");
                }
            }
            catch (Exception ex)
            {
                AppLog.Exception(LogChannel.UI, ex);
                if (IsOpen)
                {
                    Toast.Error("金币领取失败，请重试");
                }
            }
            finally
            {
                _claiming = false;
                RefreshClaim();
            }
        }

        private void RefreshClaim()
        {
            ClaimLabel.Value = _claimed ? "已领取" : _claiming ? "领取中…" : "领取金币";
            ClaimCommand.RaiseCanExecuteChanged();
        }
    }
}
