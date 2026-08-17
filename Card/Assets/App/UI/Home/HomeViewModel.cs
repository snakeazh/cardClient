using System;
using System.Collections.Generic;
using App.Game;
using App.Level;
using Framework.UI;
using Framework.UI.Core;
using Framework.UI.View;

namespace App.UI
{
    public sealed class HomeViewModel : ViewModelBase
    {
        private readonly IUIManager _ui;
        private readonly GameSession _session;
        private readonly GameTableViewModel _tableVm;
        private readonly ILevelService _levels;

        public HomeViewModel(
            IUIManager ui,
            GameSession session,
            GameTableViewModel tableVm,
            ILevelService levels)
        {
            _ui = ui;
            _session = session;
            _tableVm = tableVm;
            _levels = levels;
            Title = new ObservableProperty<string>("炸金花：搓牌对决");
            Status = new ObservableProperty<string>("心理博弈 · 盲搓改命 · 关卡闯关");

            var difficulties = _levels.GetDifficulties();
            var items = new DifficultyItemViewModel[difficulties.Count];
            for (var i = 0; i < difficulties.Count; i++)
            {
                var difficulty = difficulties[i];
                items[i] = new DifficultyItemViewModel(difficulty, () => StartGame(difficulty));
            }

            Difficulties = items;
        }

        public ObservableProperty<string> Title { get; }
        public ObservableProperty<string> Status { get; }
        public IReadOnlyList<DifficultyItemViewModel> Difficulties { get; }

        private async void StartGame(int difficulty)
        {
            if (!_levels.TrySelect(difficulty, 1))
            {
                return;
            }

            _session.StartNewRun();
            await _ui.Close(this);
            await _ui.Open(_tableVm);
        }
    }

    public sealed class DifficultyItemViewModel
    {
        public DifficultyItemViewModel(int difficulty, Action onSelect)
        {
            Difficulty = difficulty;
            Label = new ObservableProperty<string>($"难度 {difficulty}");
            SelectCommand = new RelayCommand(onSelect);
        }

        public int Difficulty { get; }
        public ObservableProperty<string> Label { get; }
        public IRelayCommand SelectCommand { get; }
    }
}
