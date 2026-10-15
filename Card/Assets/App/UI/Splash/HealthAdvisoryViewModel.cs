using System;
using System.Threading.Tasks;
using App.Bootstrap;
using App.UI;
using Framework.Log;
using Framework.UI;
using Framework.UI.Core;
using Framework.UI.Dialog;
using Framework.UI.View;
using UnityEngine;

namespace App.UI.Splash
{
    public sealed class HealthAdvisoryViewModel : ViewModelBase
    {
        private readonly IUIManager _ui;
        private readonly IDialogService _dialogs;
        private readonly MainResourceViewModel _mainResource;
        private readonly LaunchInitialization _launchInit;
        private bool _enteredHome;

        public HealthAdvisoryViewModel(
            IUIManager ui,
            IDialogService dialogs,
            MainResourceViewModel mainResource,
            LaunchInitialization launchInit)
        {
            _ui = ui;
            _dialogs = dialogs;
            _mainResource = mainResource;
            _launchInit = launchInit;
            Title = new ObservableProperty<string>(HealthAdvisoryText.Title);
            Body = new ObservableProperty<string>(HealthAdvisoryText.Body);
            Footer = new ObservableProperty<string>(HealthAdvisoryText.WeChatFooter);
        }

        public ObservableProperty<string> Title { get; }

        public ObservableProperty<string> Body { get; }

        public ObservableProperty<string> Footer { get; }

        protected override Task OnOpen(object args)
        {
            _enteredHome = false;

            Title.Value = HealthAdvisoryText.Title;
            Body.Value = HealthAdvisoryText.Body;
            Footer.Value = HealthAdvisoryText.WeChatFooter;

            // OnOpen 在 UINavigator 入栈前 await 完毕；计时须延后到 Open 流程完成之后。
            // WebGL/微信小游戏上 Task.Delay 依赖的计时器续跑不稳定，改用 realtime + Yield。
            _ = RunFlowAsync();
            return Task.CompletedTask;
        }

        private async Task RunFlowAsync()
        {
            try
            {
                await Task.Yield();
                // 展示倒计时与重型初始化并发：两者都完成才进首页，初始化快则不等、慢则兜底。
                var delay = DelayRealtimeAsync(HealthAdvisoryText.DisplayDurationMs);
                var init = _launchInit != null && _launchInit.Completion != null
                    ? _launchInit.Completion
                    : Task.CompletedTask;
                await Task.WhenAll(delay, init);
                await EnterHomeAsync();
            }
            catch (Exception ex)
            {
                // 重型初始化失败时绝不能进首页：HomeViewModel 依赖尚未注册的服务
                //（如 TalentBonusManager），会把真正的启动异常掩盖成 DI 未注册。
                var root = Unwrap(ex);
                AppLog.Exception(LogChannel.UI, root);
                try
                {
                    if (_dialogs != null)
                    {
                        await _dialogs.ConfirmAsync(
                            "启动失败",
                            root.Message + "\n请查看日志中更早的异常后重启。",
                            DialogButtons.Ok,
                            "确定");
                    }
                }
                catch (Exception dialogEx)
                {
                    AppLog.Exception(LogChannel.UI, dialogEx);
                }
            }
        }

        private static Exception Unwrap(Exception ex)
        {
            if (ex is AggregateException aggregate)
            {
                var flat = aggregate.Flatten();
                if (flat.InnerException != null)
                {
                    return flat.InnerException;
                }
            }

            return ex ?? new InvalidOperationException("unknown launch failure");
        }

        private static async Task DelayRealtimeAsync(int milliseconds)
        {
            if (milliseconds <= 0)
            {
                return;
            }

            var end = Time.realtimeSinceStartup + milliseconds / 1000f;
            while (Time.realtimeSinceStartup < end)
            {
                await Task.Yield();
            }
        }

        private async Task EnterHomeAsync()
        {
            if (_enteredHome || !IsOpen)
            {
                return;
            }

            _enteredHome = true;

            await _ui.Close(this);

            var registration = _ui.Registry.GetByViewModelType(typeof(HomeViewModel));
            var home = (HomeViewModel)_ui.Registry.CreateViewModel(registration);
            await _ui.Open(home);
            await _mainResource.EnsureShown();
        }
    }
}
