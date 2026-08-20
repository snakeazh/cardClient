using App.Config;
using App.Level;
using App.UI.Popup;
using Framework.Assets;
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
            ILevelProgressService progress,
            IResourceService resources)
        {
            _ui = ui;
            _levels = levels;
            _progress = progress;
            Resources = resources;
            LastStageInfo = new ObservableProperty<string>();
            StartCommand = new RelayCommand(OpenLevelUI);
            CollectCommand = new RelayCommand(OpenIllustratedBook);
            Hero = HeroConfig.Get(LevelUIViewModel.GetDefaultHeroId());
        }

        public IResourceService Resources { get; }

        public HeroConfig Hero { get; }

        public ObservableProperty<string> LastStageInfo { get; }

        public IRelayCommand StartCommand { get; }

        public IRelayCommand CollectCommand { get; }

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
                LastStageInfo.Value = $"难度{snapshot.Difficulty} 第{snapshot.Level}关";
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

        private async void OpenIllustratedBook()
        {
            var registration = _ui.Registry.GetByViewModelType(typeof(IllustratedBookPopViewModel));
            var vm = (IllustratedBookPopViewModel)_ui.Registry.CreateViewModel(registration);
            await _ui.Open(vm);
        }
    }
}
