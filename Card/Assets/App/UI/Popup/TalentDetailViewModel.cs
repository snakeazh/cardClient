using System.Threading.Tasks;
using Framework.UI;
using Framework.UI.Core;
using Framework.UI.View;
using UnityEngine;

namespace App.UI.Popup
{
    /// <summary>
    /// 天赋详情：单个天赋的等级翻页展示；Tip 带等级，Detail 为当前等级描述。
    /// </summary>
    public sealed class TalentDetailViewModel : ViewModelBase
    {
        private readonly IUIManager _ui;
        private TalentEntry _talent;

        public TalentDetailViewModel(IUIManager ui)
        {
            _ui = ui;
            TipText = new ObservableProperty<string>();
            DescText = new ObservableProperty<string>();
            PrevCommand = new RelayCommand(() => ShiftLevel(-1));
            NextCommand = new RelayCommand(() => ShiftLevel(1));
            CloseCommand = new RelayCommand(Dismiss);
        }

        public TalentEntry Talent => _talent;

        public int Level { get; private set; } = 1;

        public int MaxLevel => _talent != null ? _talent.Levels.Count : 0;

        public ObservableProperty<string> TipText { get; }

        public ObservableProperty<string> DescText { get; }

        public IRelayCommand PrevCommand { get; }

        public IRelayCommand NextCommand { get; }

        public IRelayCommand CloseCommand { get; }

        public void Setup(TalentEntry talent, int level = 1)
        {
            _talent = talent;
            Level = Mathf.Clamp(level, 1, Mathf.Max(1, MaxLevel));
            Refresh();
        }

        protected override Task OnOpen(object args)
        {
            Refresh();
            return Task.CompletedTask;
        }

        private void ShiftLevel(int delta)
        {
            if (MaxLevel <= 0)
            {
                return;
            }

            var level = Level + delta;
            if (level < 1)
            {
                level = MaxLevel;
            }
            else if (level > MaxLevel)
            {
                level = 1;
            }

            Level = level;
            Refresh();
        }

        private void Refresh()
        {
            if (_talent == null || Level < 1 || Level > _talent.Levels.Count)
            {
                return;
            }

            var row = _talent.Levels[Level - 1];
            TipText.Value = $"{_talent.Name}  Lv.{Level}/{_talent.Levels.Count}";
            DescText.Value = row.Desc ?? string.Empty;
        }

        private void Dismiss()
        {
            _ = _ui.Close(this);
        }
    }
}
