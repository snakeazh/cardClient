using System.Collections.Generic;
using App.Config;
using Framework.Save;

namespace App.Talent
{
    /// <summary>
    /// Indexes TalentConfig by (TalentId, TalentLevel) and tracks owned copies.
    /// Every <see cref="TalentBalance.CopiesPerLevel"/> copies raise the talent one level.
    /// </summary>
    public interface ITalentService : ISaveFlushable
    {
        bool IsDirty { get; }

        /// <summary>All TalentId values present in TalentConfig, ascending.</summary>
        IReadOnlyList<int> GetIds();

        int GetMaxLevel(int talentId);

        int GetCount(int talentId);

        /// <summary>min(MaxLevel, Count). 0 if not owned.</summary>
        int GetLevel(int talentId);

        bool IsOwned(int talentId);

        bool IsMaxLevel(int talentId);

        bool TryGet(int talentId, int level, out TalentConfig row);

        TalentConfig Get(int talentId, int level);

        /// <summary>Snapshot at the current derived level. Unknown Id still returns a locked snapshot.</summary>
        TalentSnapshot GetCurrent(int talentId);

        IReadOnlyList<TalentSnapshot> GetOwned();

        /// <summary>Uniformly picks one configured TalentId. 0 when none configured.</summary>
        int DrawRandomId();

        /// <summary>
        /// Adds obtained copies. Amount must be positive.
        /// Unknown TalentId: Warning, still stored.
        /// Extra copies past max level are kept but do not raise level.
        /// </summary>
        TalentAddResult Add(int talentId, int amount = 1);

        void Clear();

        void Load();
    }
}
