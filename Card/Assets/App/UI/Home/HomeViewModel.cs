using App.Level;
using Framework.UI;
using Framework.UI.Core;
using Framework.UI.View;

namespace App.UI
{
    public sealed class HomeViewModel : ViewModelBase
    {
        private readonly IUIManager _ui;
        private readonly ILevelService _levels;
        private readonly ILevelProgressService _progress;

        public HomeViewModel(
            IUIManager ui,
            ILevelService levels,
            ILevelProgressService progress)
        {
            _ui = ui;
            _levels = levels;
            _progress = progress;
            LastStageInfo = new ObservableProperty<string>();
            StartCommand = new RelayCommand(OpenLevelUI);
        }

        public ObservableProperty<string> LastStageInfo { get; }

        public IRelayCommand StartCommand { get; }

        protected override System.Threading.Tasks.Task OnOpen(object args)
        {
            RefreshLastStage();
            return System.Threading.Tasks.Task.CompletedTask;
        }

        private void RefreshLastStage()
        {
            if (_progress.LastLevelId > 0 && _levels.TryGetById(_progress.LastLevelId, out var snapshot) &&
                snapshot != null)
            {
                LastStageInfo.Value = $"第{snapshot.Level}关";
                return;
            }

            LastStageInfo.Value = "尚未闯关";
        }

        private async void OpenLevelUI()
        {
            var registration = _ui.Registry.GetByViewModelType(typeof(LevelUIViewModel));
            var vm = (LevelUIViewModel)_ui.Registry.CreateViewModel(registration);
            await _ui.Open(vm);
        }
    }
}
