using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using App.Atlas;
using App.Config;
using App.Level;
using App.Unlock;
using Framework.Assets;
using Framework.Log;
using Framework.UI;
using Framework.UI.Core;
using Framework.UI.View;
using UnityEngine;

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

        /// <summary>怪物页分区用（Boss 进 UltimateGrid，Normal 进 RareGrid）；收藏/遗物页未赋值。</summary>
        public MonsterType MonsterType;
    }

    /// <summary>
    /// 图鉴：收藏 / 遗物 / 怪物三页，数据分别来自 CollectionConfig、RelicConfig、MonsterConfig。
    /// </summary>
    public sealed class IllustratedBookPopViewModel : ViewModelBase
    {
        private readonly NavigationViewModel _navigation;
        private readonly MainResourceViewModel _mainResource;
        private readonly ILevelProgressService _progress;
        private readonly IUnlockConditionService _unlock;
        private readonly IUIManager _ui;
        private readonly List<IllustratedBookEntry> _collect = new List<IllustratedBookEntry>();
        private readonly List<IllustratedBookEntry> _relics = new List<IllustratedBookEntry>();
        private readonly List<IllustratedBookEntry> _monsters = new List<IllustratedBookEntry>();

        public IllustratedBookPopViewModel(
            NavigationViewModel navigation,
            MainResourceViewModel mainResource,
            ILevelProgressService progress,
            IUnlockConditionService unlock,
            IResourceService resources,
            IAtlasService atlas,
            IUIManager ui)
        {
            _navigation = navigation;
            _mainResource = mainResource;
            _progress = progress;
            _unlock = unlock;
            _ui = ui;
            Resources = resources;
            Atlas = atlas;
            CollectOn = new ObservableProperty<bool>(true);
            RelicOn = new ObservableProperty<bool>(false);
            MonsterOn = new ObservableProperty<bool>(false);
            ShowCollect = new ObservableProperty<bool>(true);
            ShowRelic = new ObservableProperty<bool>(false);
            ShowMonster = new ObservableProperty<bool>(false);
            ShowTitleBar = new ObservableProperty<bool>(true);
            ShowTip = new ObservableProperty<bool>(false);
            CurItemNum = new ObservableProperty<string>("0/0");
            TipTitle = new ObservableProperty<string>();
            TipText = new ObservableProperty<string>();
            CloseCommand = new RelayCommand(Close);
            BuildEntries();
            // 收藏页签已从界面移除，默认展示遗物页
            ApplyTab(IllustratedBookTab.Relic, force: true);
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

        /// <summary>界面顶部标题横幅（BG/TitleBg）：怪物页隐藏（分区横幅自带标题），其余页签显示。</summary>
        public ObservableProperty<bool> ShowTitleBar { get; }

        public ObservableProperty<bool> ShowTip { get; }

        public ObservableProperty<string> CurItemNum { get; }

        public ObservableProperty<string> TipTitle { get; }

        public ObservableProperty<string> TipText { get; }

        public IRelayCommand CloseCommand { get; }

        public IReadOnlyList<IllustratedBookEntry> CurrentEntries()
        {
            switch (Tab)
            {
                case IllustratedBookTab.Monster:
                    return _monsters;
                default:
                    return _relics;
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
            TipTitle.Value = entry.Tab == IllustratedBookTab.Monster
                ? string.Empty
                : (entry.Unlocked ? (entry.Name ?? string.Empty) : "？？？");
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

        /// <summary>
        /// 点击条目打开 TalentDetail 展示模式：左右切换在条目所在页签的整页列表内循环，
        /// 始终隐藏升级按钮。文案口径同原 tip：未解锁名字显示 ？？？（传 null）、描述显示解锁进度提示。
        /// 图标由 View 侧解析好传入（遗物图集/收藏资源/怪物头像三种来源）。
        /// </summary>
        public async void OpenDetail(IllustratedBookEntry entry, Func<IllustratedBookEntry, Sprite> iconResolver)
        {
            if (entry == null)
            {
                return;
            }

            try
            {
                var siblings = ResolveTabEntries(entry.Tab);
                var displays = new List<TalentDetailViewModel.DisplayEntry>(siblings.Count);
                var index = 0;
                for (var i = 0; i < siblings.Count; i++)
                {
                    var sibling = siblings[i];
                    // 按 Id 值匹配定位（siblings 已是同页列表）：RefreshEntries 每次打开
                    // 重建列表实例，View 点击回调持有的可能是旧实例，引用比较会失配导致 index 恒 0
                    if (sibling.Id == entry.Id)
                    {
                        index = i;
                    }

                    displays.Add(new TalentDetailViewModel.DisplayEntry
                    {
                        Name = sibling.Unlocked ? sibling.Name : null,
                        Desc = sibling.Unlocked
                            ? sibling.Desc ?? string.Empty
                            : (string.IsNullOrEmpty(sibling.UnlockTip) ? "尚未解锁" : sibling.UnlockTip),
                        Icon = iconResolver != null ? iconResolver(sibling) : null,
                        AcquireMethod = ResolveAcquireMethod(sibling),
                        MonsterId = sibling.Tab == IllustratedBookTab.Monster ? sibling.Id : 0
                    });
                }

                var registration = _ui.Registry.GetByViewModelType(typeof(TalentDetailViewModel));
                var vm = (TalentDetailViewModel)_ui.Registry.CreateViewModel(registration);
                vm.SetupDisplay(displays, index);
                await _ui.Open(vm);
            }
            catch (Exception ex)
            {
                AppLog.Exception(LogChannel.UI, ex);
            }
        }

        private IReadOnlyList<IllustratedBookEntry> ResolveTabEntries(IllustratedBookTab tab)
        {
            switch (tab)
            {
                case IllustratedBookTab.Collect:
                    return _collect;
                case IllustratedBookTab.Monster:
                    return _monsters;
                default:
                    return _relics;
            }
        }

        /// <summary>遗物获得方式："获取方式: \n" + UnlockConditionConfig.Desc（如"击杀20只敌人后解锁"）；
        /// 仅遗物页提供，未配条件（Id&lt;=0）或取不到行兜底"商店购买获得"（商店常驻货）。</summary>
        private static string ResolveAcquireMethod(IllustratedBookEntry entry)
        {
            if (entry == null || entry.Tab != IllustratedBookTab.Relic)
            {
                return null;
            }

            var relic = RelicConfig.Get(entry.Id);
            var desc = relic != null && relic.UnlockConditionId > 0
                ? UnlockConditionConfig.Get(relic.UnlockConditionId)?.Desc
                : null;
            if (string.IsNullOrEmpty(desc))
            {
                desc = "商店购买获得";
            }

            return "获取方式: \n" + desc;
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
            ApplyTab(IllustratedBookTab.Relic, force: true);
            // 整屏页签：打开期间藏顶部资源栏，关闭恢复（资源栏 VM 为 DI 单例，同实例）
            _mainResource?.HideBar();
            return Task.CompletedTask;
        }

        protected override Task OnClose()
        {
            _mainResource?.ShowBar();
            _navigation.NotifyBookClosed();
            return Task.CompletedTask;
        }

        protected override void OnDispose()
        {
            // 不走 OnClose 的销毁路径兜底恢复，避免资源栏层一直隐藏
            _mainResource?.ShowBar();
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
            ShowTitleBar.Value = tab != IllustratedBookTab.Monster;
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
                var fallbackName = isBoss ? "BOSS " + row.MonsterId : "怪物 " + row.MonsterId;
                _monsters.Add(new IllustratedBookEntry
                {
                    Tab = IllustratedBookTab.Monster,
                    Id = row.MonsterId,
                    Name = string.IsNullOrWhiteSpace(row.Name) ? fallbackName : row.Name,
                    Desc = row.Desc ?? string.Empty,
                    Icon = row.Icon,
                    Unlocked = true,
                    MonsterType = row.Type
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
