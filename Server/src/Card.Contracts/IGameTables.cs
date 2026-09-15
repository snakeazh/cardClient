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

    IReadOnlyList<int> Difficulties { get; }

    bool TryGetLevel(int id, out LevelConfig level);

    bool TryGetItem(int id, out ItemConfig item);

    bool TryGetRelic(int id, out RelicConfig relic);

    int GetMaxLevel(int difficulty);

    int GetTalentMaxLevel(int talentId);
}

public interface IGameConfigLoader
{
    IGameTables Load();
}
