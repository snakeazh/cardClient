using System;
using System.Collections.Generic;
using App.Atlas;
using App.Config;
using App.Talent;
using Framework.UI;
using Framework.UI.Core;
using Framework.UI.View;
using UnityEngine;

namespace App.UI.Popup
{
    /// <summary>
    /// 天赋详情：天赋入口只显示已解锁天赋，LeftBtn/RightBtn 在已解锁天赋间按 TalentId 循环切换，
    /// 不足两个时隐藏；UpgradeBtn 看广告补 1 张副本升 1 级（广告为模拟发放），满级时隐藏。
    /// 图鉴入口走 SetupDisplay 展示模式：固定一组条目循环切换，始终无升级按钮。
    /// Tip 为预制体固定文案，代码不改动。
    /// </summary>
    public sealed class TalentDetailViewModel : ViewModelBase
    {
        /// <summary>展示模式条目（图鉴等非天赋入口），图标为调用方加载好的 Sprite。</summary>
        public sealed class DisplayEntry
        {
            public string Name;
            public string Desc;
            public Sprite Icon;

            /// <summary>获得方式文案（遗物页=UnlockConditionConfig.Desc）；空表示该条目不显示。</summary>
            public string AcquireMethod;

            /// <summary>怪物页条目的 MonsterId（&gt;0 表示用 PlayerItem 敌人形态卡显示底图），其余为 0。</summary>
            public int MonsterId;
        }

        private readonly IUIManager _ui;
        private readonly ITalentService _talent;
        private readonly List<TalentSnapshot> _owned = new List<TalentSnapshot>();
        private readonly List<DisplayEntry> _display = new List<DisplayEntry>();
        private TalentSnapshot _snapshot;
        private int _displayIndex;
        private bool _displayMode;
        private bool _rewardMode;

        public TalentDetailViewModel(IUIManager ui, ITalentService talent, IAtlasService atlas)
        {
            _ui = ui;
            _talent = talent;
            Atlas = atlas;
            NameText = new ObservableProperty<string>();
            DescText = new ObservableProperty<string>();
            LevelText = new ObservableProperty<string>(string.Empty);
            IconKey = new ObservableProperty<string>(string.Empty);
            IconOverride = new ObservableProperty<Sprite>();
            Quality = new ObservableProperty<QualityType>(QualityType.Ordinary);
            ShowSwitch = new ObservableProperty<bool>(false);
            ShowUpgrade = new ObservableProperty<bool>(false);
            ShowCongratulations = new ObservableProperty<bool>(false);
            ShowAcquireMethod = new ObservableProperty<bool>(false);
            AcquireMethodText = new ObservableProperty<string>(string.Empty);
            ShowMonsterCard = new ObservableProperty<bool>(false);
            CurrentMonsterId = new ObservableProperty<int>(0);
            PrevCommand = new RelayCommand(() => Shift(-1));
            NextCommand = new RelayCommand(() => Shift(1));
            UpgradeCommand = new RelayCommand(Upgrade, () => CanUpgrade);
            CloseCommand = new RelayCommand(Dismiss);
        }

        public ObservableProperty<string> NameText { get; }

        public ObservableProperty<string> DescText { get; }

        /// <summary>等级角标文案（如 "Lv.2"），空串隐藏。</summary>
        public ObservableProperty<string> LevelText { get; }

        /// <summary>当前天赋在 Altas/Talent 图集内的 sprite 名，空表示配置未填或无选中。</summary>
        public ObservableProperty<string> IconKey { get; }

        /// <summary>展示模式的图标（调用方加载好的 Sprite，如图鉴三页）；null 表示走 IconKey 图集逻辑。</summary>
        public ObservableProperty<Sprite> IconOverride { get; }

        /// <summary>取 Altas/Talent 天赋图标 sprite。</summary>
        public IAtlasService Atlas { get; }

        /// <summary>卡面品质边框（TalentConfig.Type），View 订阅到 ItemCard.ApplyQuality。</summary>
        public ObservableProperty<QualityType> Quality { get; }

        public ObservableProperty<bool> ShowSwitch { get; }

        /// <summary>是否显示升级按钮（有选中天赋且未满级）。</summary>
        public ObservableProperty<bool> ShowUpgrade { get; }

        /// <summary>是否显示恭喜获得图（抽卡入口 Setup(reward:true) 时开）。</summary>
        public ObservableProperty<bool> ShowCongratulations { get; }

        /// <summary>是否显示获得方式文本（图鉴遗物页开，文案=UnlockConditionConfig.Desc）。</summary>
        public ObservableProperty<bool> ShowAcquireMethod { get; }

        /// <summary>获得方式文案。</summary>
        public ObservableProperty<string> AcquireMethodText { get; }

        /// <summary>是否用 PlayerItem 敌人形态卡替代 ItemCard 显示（图鉴怪物页条目开）。</summary>
        public ObservableProperty<bool> ShowMonsterCard { get; }

        /// <summary>当前怪物条目 MonsterId（ShowMonsterCard 为 true 时有效，View 据此 ApplyEnemyTheme）。</summary>
        public ObservableProperty<int> CurrentMonsterId { get; }

        public IRelayCommand PrevCommand { get; }

        public IRelayCommand NextCommand { get; }

        /// <summary>看广告升级（补 1 张副本 +1 级）。</summary>
        public IRelayCommand UpgradeCommand { get; }

        public IRelayCommand CloseCommand { get; }

        /// <summary>升级成功后触发，参数为天赋 Id；天赋列表据此刷新角标与染色。</summary>
        public event Action<int> Upgraded;

        private bool CanUpgrade => _snapshot != null && _snapshot.IsOwned && !_snapshot.IsMaxLevel;

        /// <param name="reward">抽卡获得入口：隐藏左右切换、显示恭喜获得图（升级按钮仍保留）。</param>
        /// <remarks>未拥有天赋也可打开（天赋列表点未解锁卡）：单条展示其 1 级行信息，无等级角标/切换/升级。</remarks>
        public void Setup(int talentId, bool reward = false)
        {
            _displayMode = false;
            _rewardMode = reward;
            _owned.Clear();
            _owned.AddRange(_talent.GetOwned());

            var index = _owned.FindIndex(s => s.TalentId == talentId);
            if (index >= 0)
            {
                Apply(_owned[index], inOwned: true);
            }
            else
            {
                Apply(_talent.GetCurrent(talentId), inOwned: false);
            }
        }

        /// <summary>
        /// 纯展示模式（图鉴等非天赋入口）：不走天赋服务，在传入的条目列表内循环切换，
        /// 不足两个时隐藏切换按钮；无等级角标、无升级按钮（ShowUpgrade 置 false 由 View 隐藏）。
        /// 名字传空串显示 ？？？，图标传 null 保留当前/预制体默认图。
        /// </summary>
        public void SetupDisplay(IReadOnlyList<DisplayEntry> entries, int startIndex)
        {
            _displayMode = true;
            _rewardMode = false;
            _owned.Clear();
            _snapshot = null;
            _display.Clear();
            if (entries != null)
            {
                _display.AddRange(entries);
            }

            var last = _display.Count - 1;
            _displayIndex = Mathf.Clamp(startIndex, 0, Mathf.Max(0, last));
            ApplyDisplay();
        }

        private void ApplyDisplay()
        {
            var entry = _display.Count > 0 ? _display[_displayIndex] : null;
            NameText.Value = entry != null ? entry.Name ?? string.Empty : string.Empty;
            DescText.Value = entry != null ? entry.Desc ?? string.Empty : string.Empty;
            LevelText.Value = string.Empty;
            IconKey.Value = string.Empty;
            IconOverride.Value = entry != null ? entry.Icon : null;
            Quality.Value = QualityType.Ordinary;
            ShowSwitch.Value = _display.Count > 1;
            ShowUpgrade.Value = false;
            ShowCongratulations.Value = false;
            ShowAcquireMethod.Value = entry != null && !string.IsNullOrEmpty(entry.AcquireMethod);
            AcquireMethodText.Value = entry != null ? entry.AcquireMethod ?? string.Empty : string.Empty;
            ShowMonsterCard.Value = entry != null && entry.MonsterId > 0;
            CurrentMonsterId.Value = entry != null ? entry.MonsterId : 0;
        }

        private void Shift(int delta)
        {
            if (_displayMode)
            {
                if (_display.Count <= 1)
                {
                    return;
                }

                _displayIndex = (_displayIndex + delta + _display.Count) % _display.Count;
                ApplyDisplay();
                return;
            }

            if (_owned.Count <= 1 || _snapshot == null)
            {
                return;
            }

            var index = _owned.FindIndex(s => s.TalentId == _snapshot.TalentId);
            if (index < 0)
            {
                index = 0;
            }

            var next = (index + delta + _owned.Count) % _owned.Count;
            Apply(_owned[next], inOwned: true);
        }

        /// <summary>未拥有快照 Config 为空，回退 1 级行取名字/描述/图标/品质（同天赋列表口径）。</summary>
        private TalentConfig ResolveConfig(TalentSnapshot snapshot)
        {
            if (snapshot?.Config != null)
            {
                return snapshot.Config;
            }

            return snapshot != null && _talent.TryGet(snapshot.TalentId, 1, out var row) ? row : null;
        }

        private void Apply(TalentSnapshot snapshot, bool inOwned)
        {
            _snapshot = snapshot;
            var config = ResolveConfig(snapshot);
            NameText.Value = config != null ? config.Name : string.Empty;
            DescText.Value = config != null ? config.Desc ?? string.Empty : string.Empty;
            LevelText.Value = snapshot != null && snapshot.IsOwned
                ? $"Lv.{snapshot.Level}"
                : string.Empty;
            IconKey.Value = config == null || string.IsNullOrWhiteSpace(config.Icon)
                ? string.Empty
                : config.Icon.Trim();
            Quality.Value = config != null ? config.Type : QualityType.Ordinary;
            ShowSwitch.Value = !_rewardMode && inOwned && _owned.Count > 1;
            ShowUpgrade.Value = CanUpgrade;
            ShowCongratulations.Value = _rewardMode;
            ShowAcquireMethod.Value = false;
            AcquireMethodText.Value = string.Empty;
            ShowMonsterCard.Value = false;
            CurrentMonsterId.Value = 0;
            UpgradeCommand.RaiseCanExecuteChanged();
        }

        /// <summary>
        /// 看广告升级：广告当前为模拟发放，直接补 1 张副本（CopiesPerLevel=1 即 +1 级，同 AdShop 口径）。
        /// 满级时按钮已隐藏，这里再兜一层。
        /// </summary>
        private void Upgrade()
        {
            if (!CanUpgrade)
            {
                return;
            }

            _talent.Add(_snapshot.TalentId, 1);
            var upgraded = _talent.GetCurrent(_snapshot.TalentId);
            var index = _owned.FindIndex(s => s.TalentId == upgraded.TalentId);
            if (index >= 0)
            {
                // 同步 _owned 快照，否则左右切换回来显示的还是升级前等级
                _owned[index] = upgraded;
            }

            Apply(upgraded, inOwned: true);
            Upgraded?.Invoke(upgraded.TalentId);
        }

        protected override void OnDispose()
        {
            // 详情实例关闭即销毁，顺手摘掉列表侧订阅
            Upgraded = null;
        }

        private void Dismiss()
        {
            _ = _ui.Close(this);
        }
    }
}
