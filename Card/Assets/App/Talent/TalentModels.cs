using System;
using App.Config;

namespace App.Talent
{
    /// <summary>Talent copy / level constants.</summary>
    public static class TalentBalance
    {
        /// <summary>Copies required to raise one talent level.</summary>
        public const int CopiesPerLevel = 1;

        /// <summary>追击额外伤害占原伤害的比例（词条 Value 只表示触发概率）。</summary>
        public const float ExtraAttackDamageRatio = 0.1f;

        /// <summary>侩子手：攻击前目标血量低于该比例才可斩杀。</summary>
        public const float ExecuteHpRatio = 0.1f;

        /// <summary>逢凶化吉 / 触底反弹：玩家血量低于该比例。</summary>
        public const float LowHpRatio = 0.2f;

        /// <summary>HeroConfig.CriticalDamage 缺省或非法时的暴击伤害倍率。</summary>
        public const float DefaultCriticalDamage = 2f;
    }

    /// <summary>
    /// One talent at a resolved level: config row + current ownership.
    /// </summary>
    public sealed class TalentSnapshot
    {
        public TalentSnapshot(
            int talentId,
            int count,
            int level,
            int maxLevel,
            TalentConfig config,
            TalentEntryConfig entry)
        {
            TalentId = talentId;
            Count = count;
            Level = level;
            MaxLevel = maxLevel;
            Config = config;
            Entry = entry;
        }

        public int TalentId { get; }

        /// <summary>Obtained copies of this talent Id.</summary>
        public int Count { get; }

        /// <summary>Derived level: min(MaxLevel, Count / CopiesPerLevel). 0 if locked.</summary>
        public int Level { get; }

        public int MaxLevel { get; }

        /// <summary>Copies already counted toward the next level (0 .. CopiesPerLevel-1). 0 when maxed.</summary>
        public int CopiesTowardNext =>
            IsMaxLevel ? 0 : Count % TalentBalance.CopiesPerLevel;

        /// <summary>Copies still needed to reach the next level. 0 when maxed.</summary>
        public int CopiesNeededForNext =>
            IsMaxLevel ? 0 : TalentBalance.CopiesPerLevel - CopiesTowardNext;

        public bool IsOwned => Level > 0;

        public bool IsMaxLevel => MaxLevel > 0 && Level >= MaxLevel;

        /// <summary>TalentConfig row for the current level, or null if locked.</summary>
        public TalentConfig Config { get; }

        /// <summary>TalentEntryConfig for the current level row, or null.</summary>
        public TalentEntryConfig Entry { get; }

        /// <summary>局内有效值：Entry.Value × Level。未解锁或无词条为 0。</summary>
        public float EffectiveValue =>
            Entry == null || Level <= 0 ? 0f : Entry.Value * Level;
    }

    public readonly struct TalentAddResult
    {
        public TalentAddResult(int previousLevel, TalentSnapshot current)
        {
            PreviousLevel = previousLevel;
            Current = current;
        }

        public int PreviousLevel { get; }

        public TalentSnapshot Current { get; }

        public bool LeveledUp => Current != null && Current.Level > PreviousLevel;
    }

    [Serializable]
    public sealed class TalentSaveEntry
    {
        public int TalentId;
        public int Count;
    }

    [Serializable]
    public sealed class TalentSaveData
    {
        public TalentSaveEntry[] Entries = Array.Empty<TalentSaveEntry>();
    }
}
