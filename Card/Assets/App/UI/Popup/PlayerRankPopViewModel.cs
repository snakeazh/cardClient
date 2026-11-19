using System;
using System.Threading.Tasks;
using App.Net;
using CardShare.Contracts;
using Framework.Log;
using Framework.UI;
using Framework.UI.Core;
using Framework.UI.View;

namespace App.UI.Popup
{
    public sealed class PlayerRankPopViewModel : ViewModelBase
    {
        private readonly IUIManager _ui;
        private RankShowPopArgs _rankRewards;
        private RankShowPopViewModel _rankShow;
        private bool _openingRankShow;

        public PlayerRankPopViewModel(IUIManager ui)
        {
            _ui = ui;
            CloseCommand = new RelayCommand(() => _ = _ui.Close(this));
            OpenRankRewardsCommand = new RelayCommand(OpenRankRewards);
        }

        public IRelayCommand CloseCommand { get; }
        public IRelayCommand OpenRankRewardsCommand { get; }
        public ObservableProperty<PvpLeaderboardResponse> Data { get; } = new ObservableProperty<PvpLeaderboardResponse>();
        public ObservableProperty<string> Status { get; } = new ObservableProperty<string>("排行加载中…");

        protected override Task OnOpen(object args)
        {
            _rankRewards = args as RankShowPopArgs;
            _ = LoadAsync();
            return Task.CompletedTask;
        }

        protected override async Task OnClose()
        {
            if (_rankShow != null && _ui.FindOpen<RankShowPopViewModel>() == _rankShow)
            {
                await _ui.Close(_rankShow);
            }
        }

        private async void OpenRankRewards()
        {
            if (!IsOpen || _openingRankShow || _ui.FindOpen<RankShowPopViewModel>() != null)
            {
                return;
            }

            _openingRankShow = true;
            try
            {
                var registration = _ui.Registry.GetByViewModelType(typeof(RankShowPopViewModel));
                var rankShow = (RankShowPopViewModel)_ui.Registry.CreateViewModel(registration);
                _rankShow = rankShow;
                await _ui.Open(rankShow, _rankRewards);
                if (!IsOpen && _ui.FindOpen<RankShowPopViewModel>() == rankShow)
                {
                    await _ui.Close(rankShow);
                }
            }
            catch (Exception ex)
            {
                AppLog.Exception(LogChannel.UI, ex);
            }
            finally
            {
                _openingRankShow = false;
            }
        }

        private async Task LoadAsync()
        {
            var client = GameApi.Client;
            if (!GameApiSettings.Enabled || client == null)
            {
                Status.Value = "服务器连接未开启";
                return;
            }

            if (!client.HasSession)
            {
                Status.Value = "登录后查看排行";
                return;
            }

            try
            {
                var data = await client.GetPvpLeaderboardAsync();
                if (!IsOpen || client != GameApi.Client)
                {
                    return;
                }

                if (data?.Self == null || data.Entries == null)
                {
                    Status.Value = "排行榜加载失败";
                    return;
                }

                Data.Value = data;
                Status.Value = data.Entries.Length == 0 ? "暂无排行" : string.Empty;
            }
            catch (GameApiException ex)
            {
                if (IsOpen)
                {
                    Status.Value = ex.Status == 401 ? "登录已过期" : "排行榜加载失败";
                }
                AppLog.Warn(LogChannel.Net, "排行榜加载失败：" + GameApi.Describe(ex));
            }
            catch (Exception ex)
            {
                if (IsOpen)
                {
                    Status.Value = "排行榜加载失败";
                }
                AppLog.Exception(LogChannel.UI, ex);
            }
        }
    }
}
