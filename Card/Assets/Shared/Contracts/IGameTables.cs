using System.Collections.Generic;
using CardShare.Contracts.Config;

#nullable enable

namespace CardShare.Contracts
{
    public interface IGameTables
    {
        string ConfigHash { get; }

        GameConst GameConst { get; }

        IReadOnlyList<HandScoreConfig> HandScores { get; }

        IReadOnlyList<LevelConfig> Levels { get; }

        IReadOnlyList<HeroConfig> Heroes { get; }

        IReadOnlyList<RelicConfig> Relics { get; }

        /// <summary>PVP 圣物池，只来自 <c>PvpRelicConfig</c>，不回落 <see cref="Relics"/>。</summary>
        IReadOnlyList<RelicConfig> PvpRelics { get; }

        IReadOnlyList<UnlockConditionConfig> UnlockConditions { get; }

        IReadOnlyList<TalentConfig> TalentRows { get; }

        IReadOnlyList<MonsterConfig> Monsters { get; }

        IReadOnlyList<ItemConfig> Items { get; }

        IReadOnlyList<HeroEntryConfig> HeroEntries { get; }

        IReadOnlyList<TalentEntryConfig> TalentEntries { get; }

        IReadOnlyList<RelicEntryConfig> RelicEntries { get; }

        /// <summary>PVP 圣物机制条目，只来自 <c>PvpRelicEntryConfig</c>，不回落 <see cref="RelicEntries"/>。</summary>
        IReadOnlyList<RelicEntryConfig> PvpRelicEntries { get; }

        IReadOnlyList<MonsterGroupConfig> MonsterGroups { get; }

        IReadOnlyList<PvpModeConfig> PvpModes { get; }

        IReadOnlyList<PvpRoundConfig> PvpRounds { get; }

        IReadOnlyList<PvpBotConfig> PvpBots { get; }

        IReadOnlyList<BindingConfig> Bindings { get; }

        IReadOnlyList<int> Difficulties { get; }

        bool TryGetLevel(int id, out LevelConfig level);

        bool TryGetItem(int id, out ItemConfig item);

        bool TryGetRelic(int id, out RelicConfig relic);

        /// <summary>PVP 专用：只查 <c>PvpRelicConfig</c>。</summary>
        bool TryGetPvpRelic(int id, out RelicConfig relic);

        bool TryGetHero(int id, out HeroConfig hero);

        bool TryGetHeroEntry(int id, out HeroEntryConfig entry);

        bool TryGetTalentEntry(int id, out TalentEntryConfig entry);

        bool TryGetRelicEntry(int id, out RelicEntryConfig entry);

        /// <summary>PVP 专用：只查 <c>PvpRelicEntryConfig</c>。</summary>
        bool TryGetPvpRelicEntry(int id, out RelicEntryConfig entry);

        bool TryGetMonsterGroup(int id, out MonsterGroupConfig group);

        bool TryGetMonster(int monsterId, int monsterLevel, out MonsterConfig monster);

        bool TryGetPvpMode(int id, out PvpModeConfig mode);

        bool TryGetPvpBot(int id, out PvpBotConfig bot);

        bool TryGetBinding(int id, out BindingConfig binding);

        IReadOnlyList<PvpRoundConfig> GetPvpRounds(int modeId);

        bool TryGetTalentRow(int talentId, int level, out TalentConfig row);

        int GetMaxLevel(int difficulty);

        int GetTalentMaxLevel(int talentId);
    }

    public interface IGameConfigLoader
    {
        IGameTables Load();
    }
}
