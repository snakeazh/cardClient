using System.Threading.Tasks;
using App.Config;
using App.Energy;
using App.Level;
using Framework.Assets;
using Framework.UI;
using Framework.UI.Core;
using Framework.UI.View;

namespace App.UI
{
    public sealed class HomeViewModel : ViewModelBase
    {
        private readonly IUIManager _ui;
        private readonly NavigationViewModel _navigation;
        private readonly ILevelService _levels;
        private readonly ILevelProgressService _progress;

        public HomeViewModel(
            IUIManager ui,
            NavigationViewModel navigation,
            ILevelService levels,
            ILevelProgressService progress,
            IResourceService resources)
        {
            _ui = ui;
            _navigation = navigation;
            _levels = levels;
            _progress = progress;
            Resources = resources;
            LastStageInfo = new ObservableProperty<string>();
            StaminaText = new ObservableProperty<string>();
            StartCommand = new RelayCommand(OpenLevelUI);
            Hero = HeroConfig.Get(LevelUIViewModel.GetDefaultHeroId());
        }

        public IResourceService Resources { get; }

        public HeroConfig Hero { get; }

        public ObservableProperty<string> LastStageInfo { get; }

        /// <summary>开始按钮上的每局体力消耗标注，如 "x1"。当前体力在顶部资源栏显示。</summary>
        public ObservableProperty<string> StaminaText { get; }

        public IRelayCommand StartCommand { get; }

        protected override Task OnOpen(object args)
        {
            RefreshLastStage();
            RefreshStamina();
            return Task.CompletedTask;
        }

        private void RefreshStamina()
        {
            StaminaText.Value = $"x{EnergyBalance.CostPerRun}";
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
            _navigation.HideBar();
            var registration = _ui.Registry.GetByViewModelType(typeof(LevelUIViewModel));
            var vm = (LevelUIViewModel)_ui.Registry.CreateViewModel(registration);
            await _ui.Open(vm);
        }
    }
}
