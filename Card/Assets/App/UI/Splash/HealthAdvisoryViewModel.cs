using System.Threading.Tasks;
using App.UI;
using Framework.UI;
using Framework.UI.Core;
using Framework.UI.View;

namespace App.UI.Splash
{
    public sealed class HealthAdvisoryViewModel : ViewModelBase
    {
        private readonly IUIManager _ui;
        private readonly NavigationViewModel _navigation;
        private bool _enteredHome;

        public HealthAdvisoryViewModel(IUIManager ui, NavigationViewModel navigation)
        {
            _ui = ui;
            _navigation = navigation;
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
            _ = RunFlowAsync();
            return Task.CompletedTask;
        }

        private async Task RunFlowAsync()
        {
            await Task.Yield();
            await Task.Delay(HealthAdvisoryText.DisplayDurationMs);
            await EnterHomeAsync();
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
        }
    }
}
