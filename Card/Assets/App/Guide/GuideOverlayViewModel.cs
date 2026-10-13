using App.Config;
using CardShare.Contracts.Config;
using Framework.UI.Core;
using Framework.UI.View;

namespace App.Guide
{
    public sealed class GuideOverlayViewModel : ViewModelBase
    {
        private readonly IGuideService _guide;

        public GuideOverlayViewModel(IGuideService guide)
        {
            _guide = guide;
            SkipCommand = new RelayCommand(() => _guide.Skip(), () => ShowSkip.Value);
            NextCommand = new RelayCommand(() => _guide.Advance(), () => ShowNext.Value);
            HoleClickCommand = new RelayCommand(
                () => _guide.InvokeClickTarget(),
                () => StepType.Value == GuideStepType.Click);
        }

        public ObservableProperty<string> Text { get; } = new ObservableProperty<string>(string.Empty);
        public ObservableProperty<string> TargetId { get; } = new ObservableProperty<string>(string.Empty);
        public ObservableProperty<bool> ShowMask { get; } = new ObservableProperty<bool>();
        public ObservableProperty<bool> ShowFinger { get; } = new ObservableProperty<bool>();
        public ObservableProperty<bool> ShowSkip { get; } = new ObservableProperty<bool>(true);
        public ObservableProperty<bool> ShowNext { get; } = new ObservableProperty<bool>();
        public ObservableProperty<bool> ShowBubble { get; } = new ObservableProperty<bool>();
        public ObservableProperty<int> Padding { get; } = new ObservableProperty<int>();
        public ObservableProperty<GuideStepType> StepType { get; } = new ObservableProperty<GuideStepType>(GuideStepType.Wait);
        public ObservableProperty<bool> ClickUsesButton { get; } = new ObservableProperty<bool>();

        public IRelayCommand SkipCommand { get; }
        public IRelayCommand NextCommand { get; }
        public IRelayCommand HoleClickCommand { get; }

        public void Apply(GuideStepConfig step)
        {
            if (step == null)
            {
                Text.Value = string.Empty;
                TargetId.Value = string.Empty;
                ShowMask.Value = false;
                ShowFinger.Value = false;
                ShowNext.Value = false;
                ShowBubble.Value = false;
                Padding.Value = 0;
                StepType.Value = GuideStepType.Wait;
                ClickUsesButton.Value = false;
                NextCommand.RaiseCanExecuteChanged();
                HoleClickCommand.RaiseCanExecuteChanged();
                return;
            }

            Text.Value = step.Text ?? string.Empty;
            TargetId.Value = step.TargetId ?? string.Empty;
            ShowMask.Value = step.ShowMask;
            ShowFinger.Value = step.ShowFinger;
            ShowBubble.Value = !string.IsNullOrWhiteSpace(step.Text);
            ShowNext.Value = step.StepType == GuideStepType.Dialog;
            Padding.Value = step.Padding;
            StepType.Value = step.StepType;
            ClickUsesButton.Value = false;
            NextCommand.RaiseCanExecuteChanged();
            HoleClickCommand.RaiseCanExecuteChanged();
        }
    }
}
