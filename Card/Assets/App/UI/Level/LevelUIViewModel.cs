using System.Collections.Generic;
using System.Threading.Tasks;
using App.Config;
using App.Energy;
using App.Game;
using App.Level;
using App.Talent;
using App.UI.Popup;
using Framework.Assets;
using Framework.UI;
using Framework.UI.Core;
using Framework.UI.View;

namespace App.UI
{
    public enum LevelUiPhase
    {
        Hero = 0,
        Stage = 1
    }

    /// <summary>
    /// 选角 → 选难度。每项对应该难度第 1 关。
    /// </summary>
    public sealed class LevelUIViewModel : ViewModelBase
    {
        public const string LockedText = "???";

        private readonly IUIManager _ui;
        private readonly NavigationViewModel _navigation;
        private readonly GameSession _session;
        private readonly GameTableViewModel _tableVm;
        private readonly ILevelService _levels;
        private readonly ILevelProgressService _progress;
        private readonly IEnergyService _energy;
        private readonly MainResourceViewModel _mainResource;
        private readonly TalentBonusManager _talentBonus;
        private readonly List<HeroConfig> _heroes = new List<HeroConfig>();
        private readonly List<LevelSnapshot> _stages = new List<LevelSnapshot>();

        public LevelUIViewModel(
            IUIManager ui,
            NavigationViewModel navigation,
            GameSession session,
            GameTableViewModel tableVm,
            ILevelService levels,
            ILevelProgressService progress,
            IEnergyService energy,
            MainResourceViewModel mainResource,
            TalentBonusManager talentBonus,
            IResourceService resources)
        {
            _ui = ui;
            _navigation = navigation;
            _session = session;
            _tableVm = tableVm;
            _levels = levels;
            _progress = progress;
            _energy = energy;
            _mainResource = mainResource;
            _talentBonus = talentBonus;
            Resources = resources;

            CollectHeroes();
            CollectDifficulties();
            SelectedDifficulty.Value = ResolveInitialDifficulty();
            ApplyDifficultySelection(SelectedDifficulty.Value);

            LastBtnCommand = new RelayCommand(OnLast);
            UseHeroCommand = new RelayCommand(EnterStageSelect, () => IsSelectedHeroUnlocked());
            UnlockHeroCommand = new RelayCommand(UnlockSelectedHero);
            StartGameCommand = new RelayCommand(StartGame, () =>
                Phase.Value == LevelUiPhase.Stage && IsSelectedLevelUnlocked());
            UnlockLevelCommand = new RelayCommand(
                () => { },
                () => false);
        }

        public IResourceService Resources { get; }

        public IReadOnlyList<HeroConfig> Heroes => _heroes;

        public IReadOnlyList<LevelSnapshot> Stages => _stages;

        public ObservableProperty<LevelUiPhase> Phase { get; } = new ObservableProperty<LevelUiPhase>(LevelUiPhase.Hero);

        public ObservableProperty<int> SelectedHeroId { get; } = new ObservableProperty<int>();

        public ObservableProperty<int> SelectedLevelId { get; } = new ObservableProperty<int>();

        public ObservableProperty<int> SelectedDifficulty { get; } = new ObservableProperty<int>();

        public ObservableProperty<bool> ShowHeroSelect { get; } = new ObservableProperty<bool>(true);

        public ObservableProperty<bool> ShowStageInfo { get; } = new ObservableProperty<bool>(false);

        public ObservableProperty<bool> ShowLevelSelect { get; } = new ObservableProperty<bool>(false);

        public ObservableProperty<bool> ShowUseHeroBtn { get; } = new ObservableProperty<bool>();

        public ObservableProperty<bool> ShowUnlockHeroBtn { get; } = new ObservableProperty<bool>();

        public ObservableProperty<bool> ShowStartGameBtn { get; } = new ObservableProperty<bool>();

        public ObservableProperty<bool> ShowUnlockLevelBtn { get; } = new ObservableProperty<bool>();

        public ObservableProperty<string> SkillName { get; } = new ObservableProperty<string>();

        public ObservableProperty<string> SkillInfo { get; } = new ObservableProperty<string>();

        public ObservableProperty<string> UnlockInfo { get; } = new ObservableProperty<string>();

        public ObservableProperty<string> StageNum { get; } = new ObservableProperty<string>();

        public ObservableProperty<string> StageInfoText { get; } = new ObservableProperty<string>();

        public ObservableProperty<string> DifficultyText { get; } = new ObservableProperty<string>();

        public IRelayCommand LastBtnCommand { get; }

        public IRelayCommand UseHeroCommand { get; }

        public IRelayCommand UnlockHeroCommand { get; }

        public IRelayCommand StartGameCommand { get; }

        public IRelayCommand UnlockLevelCommand { get; }

        public static int GetDefaultHeroId()
        {
            if (GameConst.IsLoaded)
            {
                var id = GameConst.Instance.DefaultHeroId;
                if (id > 0 && HeroConfig.Get(id) != null)
                {
                    return id;
                }
            }

            var min = int.MaxValue;
            foreach (var pair in HeroConfig.All)
            {
                if (pair.Key < min)
                {
                    min = pair.Key;
                }
            }

            return min == int.MaxValue ? 0 : min;
        }

        public bool IsHeroUnlocked(HeroConfig hero)
        {
            if (hero == null)
            {
                return false;
            }

            if (hero.Id == GetDefaultHeroId() || hero.UnlockCondition <= 0)
            {
                return true;
            }

            return _progress.IsHeroUnlocked(hero.Id) || MeetsDifficultyUnlock(hero);
        }

        public bool HasUnlockCondition(HeroConfig hero)
        {
            return hero != null && hero.UnlockCondition > 0;
        }

        public string GetUnlockCondition(HeroConfig hero)
        {
            if (!HasUnlockCondition(hero))
            {
                return string.Empty;
            }

            return $"通关难度{hero.UnlockCondition}可解锁";
        }

        public HeroPanelStats GetHeroPanelStats(HeroConfig hero)
        {
            return _talentBonus != null ? _talentBonus.Evaluate(hero) : TalentBonusManager.EvaluateBase(hero);
        }

        private string FormatUnlockedSkillInfo(HeroConfig hero)
        {
            var desc = hero != null && !string.IsNullOrEmpty(hero.Desc) ? hero.Desc : string.Empty;
            return TalentBonusManager.AppendPanelLines(desc, GetHeroPanelStats(hero));
        }

        public bool IsLevelUnlocked(LevelSnapshot snapshot)
        {
            return snapshot != null && _progress.IsLevelUnlocked(snapshot.Id);
        }

        public string GetLevelUnlockCondition(LevelSnapshot snapshot)
        {
            if (snapshot == null)
            {
                return string.Empty;
            }

            if (!_progress.IsDifficultyUnlocked(snapshot.Difficulty))
            {
                var diffs = _levels.GetDifficulties();
                if (diffs != null)
                {
                    for (var i = 1; i < diffs.Count; i++)
                    {
                        if (diffs[i] == snapshot.Difficulty)
                        {
                            return $"通关难度{diffs[i - 1]}可解锁";
                        }
                    }
                }

                return "未解锁该难度";
            }

            if (snapshot.Level <= 1)
            {
                return string.Empty;
            }

            return $"通关第{snapshot.Level - 1}关可解锁";
        }

        public void SelectHero(int heroId)
        {
            if (HeroConfig.Get(heroId) == null)
            {
                return;
            }

            SelectedHeroId.Value = heroId;
            _progress.SetLastHero(heroId);
            RefreshHeroPanel();
        }

        public void SelectLevel(int levelId)
        {
            if (!_levels.TryGetById(levelId, out var snapshot) || snapshot == null)
            {
                return;
            }

            ApplyDifficultySelection(snapshot.Difficulty);
            RefreshStagePanel();
        }

        public void SelectDifficulty(int difficulty)
        {
            ApplyDifficultySelection(difficulty);
            RefreshStagePanel();
        }

        protected override System.Threading.Tasks.Task OnOpen(object args)
        {
            EnterHeroSelect(true);
            return System.Threading.Tasks.Task.CompletedTask;
        }

        private void EnterHeroSelect(bool applyDefault)
        {
            Phase.Value = LevelUiPhase.Hero;
            ShowHeroSelect.Value = true;
            ShowStageInfo.Value = false;
            ShowLevelSelect.Value = false;
            ShowStartGameBtn.Value = false;
            ShowUnlockLevelBtn.Value = false;

            if (applyDefault)
            {
                var heroId = _progress.LastHeroId;
                if (HeroConfig.Get(heroId) == null)
                {
                    heroId = GetDefaultHeroId();
                }

                SelectedHeroId.Value = heroId;
            }

            RefreshHeroPanel();
        }

        private void EnterStageSelect()
        {
            if (!IsSelectedHeroUnlocked())
            {
                return;
            }

            _progress.SetLastHero(SelectedHeroId.Value);
            Phase.Value = LevelUiPhase.Stage;
            ShowHeroSelect.Value = false;
            ShowStageInfo.Value = true;
            ShowLevelSelect.Value = true;
            ShowUseHeroBtn.Value = false;
            ShowUnlockHeroBtn.Value = false;
            SelectDifficulty(ResolveInitialDifficulty());
        }

        private void ApplyDifficultySelection(int difficulty)
        {
            if (_levels.GetMaxLevel(difficulty) <= 0)
            {
                return;
            }

            SelectedDifficulty.Value = difficulty;
            _progress.SetLastDifficulty(difficulty);
            var first = _levels.Get(difficulty, 1);
            SelectedLevelId.Value = first != null ? first.Id : 0;
        }

        private void UnlockSelectedHero()
        {
            var hero = HeroConfig.Get(SelectedHeroId.Value);
            if (hero == null || IsHeroUnlocked(hero) || !MeetsDifficultyUnlock(hero))
            {
                return;
            }

            _progress.TryUnlockHero(hero.Id);
            RefreshHeroPanel();
        }

        private bool MeetsDifficultyUnlock(HeroConfig hero)
        {
            if (hero == null || hero.UnlockCondition <= 0)
            {
                return true;
            }

            return _progress.IsCleared(hero.UnlockCondition);
        }

        private async void OnLast()
        {
            if (Phase.Value == LevelUiPhase.Stage)
            {
                EnterHeroSelect(false);
                return;
            }

            await _ui.Close(this);
            await _navigation.EnsureShown();
        }

        private async void StartGame()
        {
            if (!IsSelectedLevelUnlocked())
            {
                return;
            }

            if (!_levels.TrySelect(SelectedLevelId.Value))
            {
                return;
            }

            _progress.SetLastHero(SelectedHeroId.Value);
            _progress.SetLastLevel(SelectedLevelId.Value);
            _progress.SetLastDifficulty(SelectedDifficulty.Value);
            if (!await TrySpendEnergyWithPopupAsync())
            {
                return;
            }

            _session.StartNewRun();
            await _ui.Close(this);
            await _ui.Open(_tableVm);
        }

        /// <summary>扣开局体力。不足时弹 CommonTop，确定后打开体力商店；返回是否可开局。</summary>
        private async Task<bool> TrySpendEnergyWithPopupAsync()
        {
            if (_energy.TrySpendRunCost())
            {
                return true;
            }

            await PresentEnergyInsufficientPopupAsync();
            return false;
        }

        private async Task PresentEnergyInsufficientPopupAsync()
        {
            var registration = _ui.Registry.GetByViewModelType(typeof(CommonTopViewModel));
            var popup = (CommonTopViewModel)_ui.Registry.CreateViewModel(registration);
            var confirmed = await _ui.Dialogs.ShowCustomAsync<CommonTopViewModel, bool>(
                popup,
                "体力不足，前往商店购买");
            if (confirmed)
            {
                _mainResource.OpenShop();
            }
        }

        private void RefreshHeroPanel()
        {
            var hero = HeroConfig.Get(SelectedHeroId.Value);
            var unlocked = IsHeroUnlocked(hero);
            if (unlocked)
            {
                SkillName.Value = hero != null ? hero.Name : string.Empty;
                SkillInfo.Value = FormatUnlockedSkillInfo(hero);
                UnlockInfo.Value = string.Empty;
            }
            else
            {
                SkillName.Value = LockedText;
                SkillInfo.Value = LockedText;
                UnlockInfo.Value = GetUnlockCondition(hero);
            }

            var inHeroPhase = Phase.Value == LevelUiPhase.Hero;
            ShowUseHeroBtn.Value = inHeroPhase && unlocked;
            ShowUnlockHeroBtn.Value = inHeroPhase && !unlocked;
            UseHeroCommand.RaiseCanExecuteChanged();
            UnlockHeroCommand.RaiseCanExecuteChanged();
            SelectedHeroId.ForceNotify();
        }

        private void RefreshStagePanel()
        {
            var snapshot = _levels.GetById(SelectedLevelId.Value);
            var unlocked = IsLevelUnlocked(snapshot);
            DifficultyText.Value = FormatDifficulty(SelectedDifficulty.Value);
            if (unlocked && snapshot != null)
            {
                StageNum.Value = $"难度{snapshot.Difficulty} 关卡{snapshot.Level}";
                StageInfoText.Value = FormatStageInfo(snapshot);
            }
            else
            {
                StageNum.Value = LockedText;
                StageInfoText.Value = GetLevelUnlockCondition(snapshot);
            }

            var inStagePhase = Phase.Value == LevelUiPhase.Stage;
            ShowStartGameBtn.Value = inStagePhase && unlocked;
            ShowUnlockLevelBtn.Value = false;
            StartGameCommand.RaiseCanExecuteChanged();
            SelectedLevelId.ForceNotify();
            SelectedDifficulty.ForceNotify();
        }

        private static string FormatStageInfo(LevelSnapshot snapshot)
        {
            var count = snapshot.Monsters.Count;
            if (snapshot.HasBoss)
            {
                return $"敌人 {count} 名\n含 BOSS";
            }

            return $"敌人 {count} 名";
        }

        private bool IsSelectedHeroUnlocked()
        {
            return IsHeroUnlocked(HeroConfig.Get(SelectedHeroId.Value));
        }

        private bool IsSelectedLevelUnlocked()
        {
            return IsLevelUnlocked(_levels.GetById(SelectedLevelId.Value));
        }

        private void CollectHeroes()
        {
            _heroes.Clear();
            foreach (var pair in HeroConfig.All)
            {
                _heroes.Add(pair.Value);
            }

            _heroes.Sort((a, b) => a.Id.CompareTo(b.Id));
        }

        private void CollectDifficulties()
        {
            _stages.Clear();
            var diffs = _levels.GetDifficulties();
            if (diffs == null)
            {
                return;
            }

            for (var i = 0; i < diffs.Count; i++)
            {
                var first = _levels.Get(diffs[i], 1);
                if (first != null)
                {
                    _stages.Add(first);
                }
            }
        }

        private int ResolveInitialDifficulty()
        {
            var difficulty = _progress.LastDifficulty;
            if (_levels.GetMaxLevel(difficulty) > 0 && _progress.IsDifficultyUnlocked(difficulty))
            {
                return difficulty;
            }

            if (_progress.LastLevelId > 0 &&
                _levels.TryGetById(_progress.LastLevelId, out var snapshot) &&
                snapshot != null &&
                _progress.IsDifficultyUnlocked(snapshot.Difficulty))
            {
                return snapshot.Difficulty;
            }

            return _levels.DefaultDifficulty;
        }

        private string FormatDifficulty(int difficulty)
        {
            if (!_progress.IsDifficultyUnlocked(difficulty))
            {
                return $"难度{difficulty} 未解锁";
            }

            if (_progress.IsCleared(difficulty))
            {
                return $"难度{difficulty} 已完成";
            }

            return $"难度{difficulty}";
        }
    }
}
