using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using App.Config;
using Framework.Log;
using Framework.UI;
using Framework.UI.Core;
using Framework.UI.View;

namespace App.UI.Popup
{
    /// <summary>
    /// 一个天赋：TalentConfig 中同 TalentId 的多行按 TalentLevel 升序聚合。
    /// </summary>
    public sealed class TalentEntry
    {
        public int TalentId;
        public string Name;
        public string Icon;
        public readonly List<TalentConfig> Levels = new List<TalentConfig>();
    }

    /// <summary>
    /// 天赋弹窗：按 TalentId 聚合 TalentConfig 生成列表；点击条目打开天赋详情。
    /// </summary>
    public sealed class TalentPopupViewModel : ViewModelBase
    {
        private readonly IUIManager _ui;
        private readonly List<TalentEntry> _entries = new List<TalentEntry>();

        public TalentPopupViewModel(IUIManager ui)
        {
            _ui = ui;
            CloseCommand = new RelayCommand(Dismiss);
            BuildEntries();
        }

        public IReadOnlyList<TalentEntry> Entries => _entries;

        public IRelayCommand CloseCommand { get; }

        public async Task OpenDetail(TalentEntry entry)
        {
            if (entry == null || entry.Levels.Count == 0)
            {
                return;
            }

            try
            {
                var registration = _ui.Registry.GetByViewModelType(typeof(TalentDetailViewModel));
                var vm = (TalentDetailViewModel)_ui.Registry.CreateViewModel(registration);
                vm.Setup(entry);
                await _ui.Open(vm);
            }
            catch (Exception ex)
            {
                AppLog.Exception(LogChannel.UI, ex);
            }
        }

        private void BuildEntries()
        {
            _entries.Clear();
            var grouped = new Dictionary<int, TalentEntry>();
            foreach (var kv in TalentConfig.All)
            {
                var row = kv.Value;
                if (row == null || row.TalentId <= 0)
                {
                    continue;
                }

                if (!grouped.TryGetValue(row.TalentId, out var entry))
                {
                    entry = new TalentEntry
                    {
                        TalentId = row.TalentId,
                        Name = row.Name,
                        Icon = row.Icon
                    };
                    grouped[row.TalentId] = entry;
                    _entries.Add(entry);
                }

                entry.Levels.Add(row);
            }

            _entries.Sort((a, b) => a.TalentId.CompareTo(b.TalentId));
            for (var i = 0; i < _entries.Count; i++)
            {
                _entries[i].Levels.Sort((a, b) => a.TalentLevel.CompareTo(b.TalentLevel));
            }
        }

        private void Dismiss()
        {
            _ = _ui.Close(this);
        }
    }
}
