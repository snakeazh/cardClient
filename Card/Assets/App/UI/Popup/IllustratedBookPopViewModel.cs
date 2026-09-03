using System.Collections.Generic;
using System.Threading.Tasks;
using App.Atlas;
using App.Config;
using App.Level;
using App.Unlock;
using Framework.Assets;
using Framework.UI.Core;
using Framework.UI.View;

namespace App.UI.Popup
{
    public enum IllustratedBookTab
    {
        Collect = 0,
        Relic = 1,
        Monster = 2
    }

    public sealed class IllustratedBookEntry
    {
        public IllustratedBookTab Tab;
        public int Id;
        public string Name;
        public string Desc;
        public string Icon;
        public bool Unlocked;
        public string UnlockTip;
    }

    /// <summary>
    /// 图鉴：收藏 / 遗物 / 怪物三页，数据分别来自 CollectionConfig、RelicConfig、MonsterConfig。
    /// </summary>
    public sealed class IllustratedBookPopViewModel : ViewModelBase
    {
        private readonly NavigationViewModel _navigation;
        private readonly ILevelProgressService _progress;
        private readonly IUnlockConditionService _unlock;
        private readonly List<IllustratedBookEntry> _collect = new List<IllustratedBookEntry>();
        private readonly List<IllustratedBookEntry> _relics = new List<IllustratedBookEntry>();
        private readonly List<IllustratedBookEntry> _monsters = new List<IllustratedBookEntry>();

        public IllustratedBookPopViewModel(
            NavigationViewModel navigation,
            ILevelProgressService progress,
            IUnlockConditionService unlock,
            IResourceService resources,
            IAtlasService atlas)
        {
            _navigation = navigation;
            _progress = progress;
            _unlock = unlock;
            Resources = resources;
            Atlas = atlas;
            CollectOn = new ObservableProperty<bool>(true);
            RelicOn = new ObservableProperty<bool>(false);
            MonsterOn = new ObservableProperty<bool>(false);
            ShowCollect = new ObservableProperty<bool>(true);
            ShowRelic = new ObservableProperty<bool>(false);
            ShowMonster = new ObservableProperty<bool>(false);
            ShowTip = new ObservableProperty<bool>(false);
            CurItemNum = new ObservableProperty<string>("0/0");
            TipTitle = new ObservableProperty<string>();
            TipText = new ObservableProperty<string>();
            CloseCommand = new RelayCommand(Close);
            BuildEntries();
            ApplyTab(IllustratedBookTab.Collect, force: true);
        }

        public IResourceService Resources { get; }

        public IAtlasService Atlas { get; }

        public IllustratedBookTab Tab { get; private set; }

        public int SelectedId { get; private set; }

        public IReadOnlyList<IllustratedBookEntry> CollectEntries => _collect;

        public IReadOnlyList<IllustratedBookEntry> RelicEntries => _relics;

        public IReadOnlyList<IllustratedBookEntry> MonsterEntries => _monsters;

        public ObservableProperty<bool> CollectOn { get; }

        public ObservableProperty<bool> RelicOn { get; }

        public ObservableProperty<bool> MonsterOn { get; }

        public ObservableProperty<bool> ShowCollect { get; }

        public ObservableProperty<bool> ShowRelic { get; }

        public ObservableProperty<bool> ShowMonster { get; }

        public ObservableProperty<bool> ShowTip { get; }

        public ObservableProperty<string> CurItemNum { get; }

        public ObservableProperty<string> TipTitle { get; }

        public ObservableProperty<string> TipText { get; }

        public IRelayCommand CloseCommand { get; }

        public IReadOnlyList<IllustratedBookEntry> CurrentEntries()
        {
            switch (Tab)
            {
                case IllustratedBookTab.Relic:
                    return _relics;
                case IllustratedBookTab.Monster:
                    return _monsters;
                default:
                    return _collect;
            }
        }

        public void SelectTab(IllustratedBookTab tab)
        {
            ApplyTab(tab, force: false);
        }

        public void SelectEntry(IllustratedBookEntry entry)
        {
            if (entry == null || entry.Tab != Tab)
            {
                HideTip();
                return;
            }

            if (SelectedId == entry.Id && ShowTip.Value)
            {
                HideTip();
                return;
            }

            SelectedId = entry.Id;
            TipTitle.Value = entry.Unlocked
                ? (entry.Name ?? string.Empty)
                : "？？？";
            TipText.Value = entry.Unlocked
                ? (entry.Desc ?? string.Empty)
                : (string.IsNullOrEmpty(entry.UnlockTip) ? "尚未解锁" : entry.UnlockTip);
            ShowTip.Value = true;
        }

        public void HideTip()
        {
            SelectedId = 0;
            ShowTip.Value = false;
            TipTitle.Value = string.Empty;
            TipText.Value = string.Empty;
        }

        public bool IsSelected(IllustratedBookEntry entry)
        {
            return entry != null && entry.Tab == Tab && entry.Id == SelectedId && ShowTip.Value;
        }

        public void RefreshEntries()
        {
            BuildEntries();
            RefreshCount();
        }

        protected override Task OnOpen(object args)
        {
            HideTip();
            BuildEntries();
            ApplyTab(IllustratedBookTab.Collect, force: true);
            return Task.CompletedTask;
        }

        protected override Task OnClose()
        {
            _navigation.NotifyBookClosed();
            return Task.CompletedTask;
        }

        private void ApplyTab(IllustratedBookTab tab, bool force)
        {
            if (!force && Tab == tab)
            {
                return;
            }

            Tab = tab;
            HideTip();
            CollectOn.Value = tab == IllustratedBookTab.Collect;
            RelicOn.Value = tab == IllustratedBookTab.Relic;
            MonsterOn.Value = tab == IllustratedBookTab.Monster;
            ShowCollect.Value = tab == IllustratedBookTab.Collect;
            ShowRelic.Value = tab == IllustratedBookTab.Relic;
            ShowMonster.Value = tab == IllustratedBookTab.Monster;
            RefreshCount();
        }

        private void RefreshCount()
        {
            var entries = CurrentEntries();
            var unlocked = 0;
            for (var i = 0; i < entries.Count; i++)
            {
                if (entries[i].Unlocked)
                {
                    unlocked++;
                }
            }

            CurItemNum.Value = unlocked + "/" + entries.Count;
        }

        private void BuildEntries()
        {
            BuildCollect();
            BuildRelics();
            BuildMonsters();
        }

        private void BuildCollect()
        {
            _collect.Clear();
            foreach (var kv in CollectionConfig.All)
            {
                var row = kv.Value;
                if (row == null)
                {
                    continue;
                }

                _collect.Add(new IllustratedBookEntry
                {
                    Tab = IllustratedBookTab.Collect,
                    Id = row.Id,
                    Name = row.Name,
                    Desc = row.Desc,
                    Icon = row.Icon,
                    Unlocked = IsCollectUnlocked(row.UnlockCondition)
                });
            }

            _collect.Sort((a, b) => a.Id.CompareTo(b.Id));
        }

        private void BuildRelics()
        {
            _relics.Clear();
            foreach (var kv in RelicConfig.All)
            {
                var row = kv.Value;
                if (row == null)
                {
                    continue;
                }

                var unlocked = IsRelicUnlocked(row);
                _relics.Add(new IllustratedBookEntry
                {
                    Tab = IllustratedBookTab.Relic,
                    Id = row.Id,
                    Name = row.Name,
                    Desc = row.Desc,
                    Icon = row.Icon,
                    Unlocked = unlocked,
                    UnlockTip = unlocked ? null : RelicUnlockTip(row, _unlock)
                });
            }

            _relics.Sort((a, b) => a.Id.CompareTo(b.Id));
        }

        private void BuildMonsters()
        {
            _monsters.Clear();
            var best = new Dictionary<int, MonsterConfig>();
            foreach (var kv in MonsterConfig.All)
            {
                var row = kv.Value;
                if (row == null || row.MonsterId <= 0)
                {
                    continue;
                }

                if (!best.TryGetValue(row.MonsterId, out var exist) || row.MonsterLevel < exist.MonsterLevel)
                {
                    best[row.MonsterId] = row;
                }
            }

            foreach (var kv in best)
            {
                var row = kv.Value;
                var isBoss = row.Type == MonsterType.Boss;
                _monsters.Add(new IllustratedBookEntry
                {
                    Tab = IllustratedBookTab.Monster,
                    Id = row.MonsterId,
                    Name = isBoss ? "BOSS " + row.MonsterId : "怪物 " + row.MonsterId,
                    Desc = (isBoss ? "BOSS" : "普通敌人") +
                           "\n生命 " + row.MonsterHp + "  攻击 " + row.MonsterDamage,
                    Icon = row.Icon,
                    Unlocked = true
                });
            }

            _monsters.Sort((a, b) => a.Id.CompareTo(b.Id));
        }

        private bool IsCollectUnlocked(int unlockCondition)
        {
            if (unlockCondition <= 0)
            {
                return true;
            }

            return _progress != null && _progress.IsDifficultyUnlocked(unlockCondition);
        }

        private bool IsRelicUnlocked(RelicConfig relic)
        {
            if (relic == null || relic.UnlockConditionId <= 0)
            {
                return true;
            }

            return _unlock != null && _unlock.IsRelicUnlocked(relic.Id);
        }

        private static string RelicUnlockTip(RelicConfig relic, IUnlockConditionService unlock)
        {
            if (relic == null || relic.UnlockConditionId <= 0)
            {
                return "尚未解锁";
            }

            var condition = UnlockConditionConfig.Get(relic.UnlockConditionId);
            var current = unlock != null ? unlock.GetProgress(relic.UnlockConditionId) : 0;
            var target = condition != null && condition.Value > 0 ? condition.Value : 0;
            if (target > 0 && current > target)
            {
                current = target;
            }

            var progress = target > 0 ? current + "/" + target : current.ToString();
            if (string.IsNullOrEmpty(condition?.Desc))
            {
                return "尚未解锁（" + progress + "）";
            }

            return condition.Desc + "（" + progress + "）";
        }

        private void Close()
        {
            HideTip();
            _navigation.ShowHome();
        }
    }
}
