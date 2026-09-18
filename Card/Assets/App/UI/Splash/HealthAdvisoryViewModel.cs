using System;
using System.Threading.Tasks;
using App.UI;
using Framework.Log;
using Framework.UI;
using Framework.UI.Core;
using Framework.UI.View;
using UnityEngine;

namespace App.UI.Splash
{
    public sealed class HealthAdvisoryViewModel : ViewModelBase
    {
        private readonly IUIManager _ui;
        private readonly NavigationViewModel _navigation;
        private readonly MainResourceViewModel _mainResource;
        private bool _enteredHome;

        public HealthAdvisoryViewModel(
            IUIManager ui,
            NavigationViewModel navigation,
            MainResourceViewModel mainResource)
        {
            _ui = ui;
            _navigation = navigation;
            _mainResource = mainResource;
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
                await DelayRealtimeAsync(HealthAdvisoryText.DisplayDurationMs);
                await EnterHomeAsync();
            }
            catch (Exception ex)
            {
                AppLog.Exception(LogChannel.UI, ex);
                try
                {
                    await EnterHomeAsync();
                }
                catch (Exception retryEx)
                {
                    AppLog.Exception(LogChannel.UI, retryEx);
                }
            }
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
            await _navigation.EnsureShown();
            await _mainResource.EnsureShown();
        }
    }
}
