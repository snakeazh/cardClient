using System.Collections.Generic;
using CardShare.Contracts.Config;

namespace CardShare.Contracts;

public interface IGameTables
{
    string ConfigHash { get; }

    GameConst GameConst { get; }

    IReadOnlyList<HandScoreConfig> HandScores { get; }

    IReadOnlyList<LevelConfig> Levels { get; }

    IReadOnlyList<HeroConfig> Heroes { get; }

    IReadOnlyList<RelicConfig> Relics { get; }

    IReadOnlyList<UnlockConditionConfig> UnlockConditions { get; }

    IReadOnlyList<TalentConfig> TalentRows { get; }

    IReadOnlyList<MonsterConfig> Monsters { get; }

    IReadOnlyList<ItemConfig> Items { get; }

    IReadOnlyList<HeroEntryConfig> HeroEntries { get; }

    IReadOnlyList<TalentEntryConfig> TalentEntries { get; }

    IReadOnlyList<RelicEntryConfig> RelicEntries { get; }

    IReadOnlyList<MonsterGroupConfig> MonsterGroups { get; }

    IReadOnlyList<PvpModeConfig> PvpModes { get; }

    IReadOnlyList<PvpRoundConfig> PvpRounds { get; }

    IReadOnlyList<int> Difficulties { get; }

    bool TryGetLevel(int id, out LevelConfig level);

    bool TryGetItem(int id, out ItemConfig item);

    bool TryGetRelic(int id, out RelicConfig relic);

    bool TryGetHero(int id, out HeroConfig hero);

    bool TryGetHeroEntry(int id, out HeroEntryConfig entry);

    bool TryGetTalentEntry(int id, out TalentEntryConfig entry);

    bool TryGetRelicEntry(int id, out RelicEntryConfig entry);

    bool TryGetMonsterGroup(int id, out MonsterGroupConfig group);

    bool TryGetMonster(int monsterId, int monsterLevel, out MonsterConfig monster);

    bool TryGetPvpMode(int id, out PvpModeConfig mode);

    IReadOnlyList<PvpRoundConfig> GetPvpRounds(int modeId);

    bool TryGetTalentRow(int talentId, int level, out TalentConfig row);

    int GetMaxLevel(int difficulty);

    int GetTalentMaxLevel(int talentId);
}

public interface IGameConfigLoader
{
    IGameTables Load();
}
