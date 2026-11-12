using System;
using System.Threading.Tasks;
using App.Bootstrap;
using App.UI;
using Framework.Log;
using Framework.UI;
using Framework.UI.Core;
using Framework.UI.Dialog;
using Framework.UI.View;

namespace App.UI.Splash
{
    public sealed class HealthAdvisoryViewModel : ViewModelBase
    {
        private readonly IUIManager _ui;
        private readonly IDialogService _dialogs;
        private readonly MainResourceViewModel _mainResource;
        private readonly LaunchInitialization _launchInit;
        private readonly TaskCompletionSource<bool> _playRequested = new TaskCompletionSource<bool>();
        private bool _playClicked;
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
            PlayCommand = new RelayCommand(RequestPlay, () => !_playClicked);
        }

        public RelayCommand PlayCommand { get; }

        protected override Task OnOpen(object args)
        {
            _enteredHome = false;

            // 等待导航入栈后再执行后续流程。
            _ = RunFlowAsync();
            return Task.CompletedTask;
        }

        private async Task RunFlowAsync()
        {
            try
            {
                await Task.Yield();
                // 初始化失败立即提示；提前点击时保留请求，初始化完成后进入首页。
                var init = _launchInit != null && _launchInit.Completion != null
                    ? _launchInit.Completion
                    : Task.CompletedTask;
                await init;
                await _playRequested.Task;
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

        private void RequestPlay()
        {
            _playClicked = true;
            PlayCommand.RaiseCanExecuteChanged();
            _playRequested.TrySetResult(true);
        }

        protected override Task OnClose()
        {
            _playRequested.TrySetResult(false);
            return Task.CompletedTask;
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
