using System;
using System.Threading.Tasks;
using CardShare.Contracts;
using Framework.Log;
using Framework.UI.Dialog;

namespace App.Net
{
    /// <summary>启动连服、未完成对局按失败结算、结算补报。</summary>
    public static class PveSessionGate
    {
        public const string ActiveRunExists = "active_run_exists";

        public static async Task ConnectWithRetryAsync(IDialogService dialogs, string deviceCode, string nickName)
        {
            while (true)
            {
                try
                {
                    var login = await GameApi.Client.ConnectAsync(deviceCode, nickName);
                    GameApi.ApplyProfile(login.Profile);
                    AppLog.Info(LogChannel.Net, "session ok userId=" + login.Profile.UserId);
                    return;
                }
                catch (Exception ex)
                {
                    var message = ex is GameApiException api ? GameApi.Describe(api) : "无法连接服务器";
                    AppLog.Warn(LogChannel.Net, "connect failed: " + ex.Message);
                    await dialogs.ConfirmAsync(
                        "连接失败",
                        message + "\n请先启动 Server 后重试。",
                        DialogButtons.Ok,
                        "重试");
                }
            }
        }

        public static async Task FlushPendingSettleWithRetryAsync(IDialogService dialogs)
        {
            while (true)
            {
                try
                {
                    var profile = await GameApi.Client.FlushPendingSettleAsync();
                    if (profile != null)
                    {
                        GameApi.ApplyProfile(profile);
                        AppLog.Info(LogChannel.Net, "flushed pending settle");
                    }

                    return;
                }
                catch (Exception ex)
                {
                    var message = ex is GameApiException api ? GameApi.Describe(api) : "结算补报失败";
                    AppLog.Warn(LogChannel.Net, "flush settle failed: " + ex.Message);
                    await dialogs.ConfirmAsync(
                        "结算未完成",
                        message + "\n请检查网络后重试，否则进度可能丢失。",
                        DialogButtons.Ok,
                        "重试");
                }
            }
        }

        /// <summary>未结算 run 一律按失败结算，不恢复牌局。</summary>
        public static async Task ForfeitActiveRunIfAnyAsync(IDialogService dialogs)
        {
            PveRunDto run;
            try
            {
                var resp = await GameApi.Client.GetActiveRunAsync();
                run = resp != null ? resp.Run : null;
            }
            catch (Exception ex)
            {
                AppLog.Warn(LogChannel.Net, "query active run failed: " + ex.Message);
                return;
            }

            if (run == null || string.IsNullOrEmpty(run.RunId))
            {
                return;
            }

            AppLog.Info(LogChannel.Net, "forfeit active run " + run.RunId);
            await AbandonRunWithRetryAsync(dialogs, run);
        }

        public static async Task AbandonRunWithRetryAsync(IDialogService dialogs, PveRunDto run)
        {
            var request = new PveSettleRequest
            {
                RunId = run != null ? run.RunId : string.Empty,
                Cleared = false,
                Forfeit = true
            };
            GameApi.Client.SavePendingSettle(request);
            await FlushPendingSettleWithRetryAsync(dialogs);
        }
    }
}
