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
        }

        private readonly IUIManager _ui;
        private readonly ITalentService _talent;
        private readonly List<TalentSnapshot> _owned = new List<TalentSnapshot>();
        private readonly List<DisplayEntry> _display = new List<DisplayEntry>();
        private TalentSnapshot _snapshot;
        private int _displayIndex;
        private bool _displayMode;

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

        public IRelayCommand PrevCommand { get; }

        public IRelayCommand NextCommand { get; }

        /// <summary>看广告升级（补 1 张副本 +1 级）。</summary>
        public IRelayCommand UpgradeCommand { get; }

        public IRelayCommand CloseCommand { get; }

        /// <summary>升级成功后触发，参数为天赋 Id；天赋列表据此刷新角标与染色。</summary>
        public event Action<int> Upgraded;

        private bool CanUpgrade => _snapshot != null && !_snapshot.IsMaxLevel;

        public void Setup(int talentId)
        {
            _displayMode = false;
            _owned.Clear();
            _owned.AddRange(_talent.GetOwned());
            ShowSwitch.Value = _owned.Count > 1;

            var index = _owned.FindIndex(s => s.TalentId == talentId);
            if (index < 0)
            {
                index = 0;
            }

            Apply(_owned.Count > 0 ? _owned[index] : null);
        }

        /// <summary>
        /// 纯展示模式（图鉴等非天赋入口）：不走天赋服务，在传入的条目列表内循环切换，
        /// 不足两个时隐藏切换按钮；无等级角标、无升级按钮（ShowUpgrade 置 false 由 View 隐藏）。
        /// 名字传空串显示 ？？？，图标传 null 保留当前/预制体默认图。
        /// </summary>
        public void SetupDisplay(IReadOnlyList<DisplayEntry> entries, int startIndex)
        {
            _displayMode = true;
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
            Apply(_owned[next]);
        }

        private void Apply(TalentSnapshot snapshot)
        {
            _snapshot = snapshot;
            NameText.Value = snapshot != null && snapshot.Config != null
                ? snapshot.Config.Name
                : string.Empty;
            DescText.Value = snapshot != null && snapshot.Config != null
                ? snapshot.Config.Desc ?? string.Empty
                : string.Empty;
            LevelText.Value = snapshot != null && snapshot.IsOwned
                ? $"Lv.{snapshot.Level}"
                : string.Empty;
            IconKey.Value = snapshot?.Config == null || string.IsNullOrWhiteSpace(snapshot.Config.Icon)
                ? string.Empty
                : snapshot.Config.Icon.Trim();
            Quality.Value = snapshot?.Config != null ? snapshot.Config.Type : QualityType.Ordinary;
            ShowUpgrade.Value = CanUpgrade;
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

            Apply(upgraded);
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
