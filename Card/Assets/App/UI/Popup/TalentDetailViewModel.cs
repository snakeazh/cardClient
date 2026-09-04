using System;
using System.Collections.Generic;
using App.Atlas;
using App.Talent;
using Framework.UI;
using Framework.UI.Core;
using Framework.UI.View;

namespace App.UI.Popup
{
    /// <summary>
    /// 天赋详情：只显示已解锁天赋；LeftBtn/RightBtn 在已解锁天赋间按 TalentId 循环切换，
    /// 已解锁不足两个时隐藏切换按钮。Tip 为预制体固定文案，代码不改动。
    /// </summary>
    public sealed class TalentDetailViewModel : ViewModelBase
    {
        private readonly IUIManager _ui;
        private readonly ITalentService _talent;
        private readonly List<TalentSnapshot> _owned = new List<TalentSnapshot>();
        private TalentSnapshot _snapshot;

        public TalentDetailViewModel(IUIManager ui, ITalentService talent, IAtlasService atlas)
        {
            _ui = ui;
            _talent = talent;
            Atlas = atlas;
            NameText = new ObservableProperty<string>();
            DescText = new ObservableProperty<string>();
            LevelText = new ObservableProperty<string>(string.Empty);
            IconKey = new ObservableProperty<string>(string.Empty);
            ShowSwitch = new ObservableProperty<bool>(false);
            PrevCommand = new RelayCommand(() => Shift(-1));
            NextCommand = new RelayCommand(() => Shift(1));
            CloseCommand = new RelayCommand(Dismiss);
        }

        public ObservableProperty<string> NameText { get; }

        public ObservableProperty<string> DescText { get; }

        /// <summary>等级角标文案（如 "Lv.2"），空串隐藏。</summary>
        public ObservableProperty<string> LevelText { get; }

        /// <summary>当前天赋在 Altas/Talent 图集内的 sprite 名，空表示配置未填或无选中。</summary>
        public ObservableProperty<string> IconKey { get; }

        /// <summary>取 Altas/Talent 天赋图标 sprite。</summary>
        public IAtlasService Atlas { get; }

        public ObservableProperty<bool> ShowSwitch { get; }

        public IRelayCommand PrevCommand { get; }

        public IRelayCommand NextCommand { get; }

        public IRelayCommand CloseCommand { get; }

        public void Setup(int talentId)
        {
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

        private void Shift(int delta)
        {
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
        }

        private void Dismiss()
        {
            _ = _ui.Close(this);
        }
    }
}
