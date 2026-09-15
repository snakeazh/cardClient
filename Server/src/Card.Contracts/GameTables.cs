using System;
using System.Collections.Generic;
using System.Linq;
using CardShare.Contracts.Config;

namespace CardShare.Contracts;

public sealed class GameTables : IGameTables
{
    private readonly Dictionary<int, LevelConfig> _levels;
    private readonly Dictionary<int, int> _maxLevelByDifficulty;
    private readonly Dictionary<int, int> _talentMaxLevel;

    private readonly Dictionary<int, ItemConfig> _items;
    private readonly Dictionary<int, RelicConfig> _relics;

    public GameTables(
        GameConst gameConst,
        IReadOnlyList<HandScoreConfig> handScores,
        IReadOnlyList<LevelConfig> levels,
        IReadOnlyList<HeroConfig> heroes,
        IReadOnlyList<RelicConfig> relics,
        IReadOnlyList<UnlockConditionConfig> unlockConditions,
        IReadOnlyList<TalentConfig> talentRows,
        IReadOnlyList<MonsterConfig> monsters,
        IReadOnlyList<ItemConfig>? items = null,
        string configHash = "")
    {
        GameConst = gameConst ?? new GameConst();
        HandScores = handScores ?? Array.Empty<HandScoreConfig>();
        Levels = levels ?? Array.Empty<LevelConfig>();
        Heroes = heroes ?? Array.Empty<HeroConfig>();
        Relics = relics ?? Array.Empty<RelicConfig>();
        UnlockConditions = unlockConditions ?? Array.Empty<UnlockConditionConfig>();
        TalentRows = talentRows ?? Array.Empty<TalentConfig>();
        Monsters = monsters ?? Array.Empty<MonsterConfig>();
        Items = items ?? Array.Empty<ItemConfig>();
        ConfigHash = configHash ?? string.Empty;
        _levels = Levels.Where(l => l != null && l.Id > 0).ToDictionary(l => l.Id);
        _items = Items.Where(i => i != null && i.Id > 0).GroupBy(i => i.Id).ToDictionary(g => g.Key, g => g.First());
        _relics = Relics.Where(r => r != null && r.Id > 0).GroupBy(r => r.Id).ToDictionary(g => g.Key, g => g.First());
        Difficulties = Levels.Select(l => l.Difficulty).Distinct().OrderBy(d => d).ToArray();
        _maxLevelByDifficulty = Levels
            .GroupBy(l => l.Difficulty)
            .ToDictionary(g => g.Key, g => g.Max(l => l.Level));
        _talentMaxLevel = new Dictionary<int, int>();
        foreach (var row in TalentRows)
        {
            if (row == null || row.TalentId <= 0)
            {
                continue;
            }

            if (!_talentMaxLevel.TryGetValue(row.TalentId, out var current) || row.TalentLevel > current)
            {
                _talentMaxLevel[row.TalentId] = row.TalentLevel;
            }
        }
    }

    public string ConfigHash { get; }

    public GameConst GameConst { get; }

    public IReadOnlyList<HandScoreConfig> HandScores { get; }

    public IReadOnlyList<LevelConfig> Levels { get; }

    public IReadOnlyList<HeroConfig> Heroes { get; }

    public IReadOnlyList<RelicConfig> Relics { get; }

    public IReadOnlyList<UnlockConditionConfig> UnlockConditions { get; }

    public IReadOnlyList<TalentConfig> TalentRows { get; }

    public IReadOnlyList<MonsterConfig> Monsters { get; }

    public IReadOnlyList<ItemConfig> Items { get; }

    public IReadOnlyList<int> Difficulties { get; }

    public bool TryGetLevel(int id, out LevelConfig level) => _levels.TryGetValue(id, out level!);

    public bool TryGetItem(int id, out ItemConfig item) => _items.TryGetValue(id, out item!);

    public bool TryGetRelic(int id, out RelicConfig relic) => _relics.TryGetValue(id, out relic!);

    public int GetMaxLevel(int difficulty)
        => _maxLevelByDifficulty.TryGetValue(difficulty, out var max) ? max : 0;

    public int GetTalentMaxLevel(int talentId)
        => _talentMaxLevel.TryGetValue(talentId, out var max) ? max : 0;

    public static GameTables Fallback()
    {
        var gameConst = new GameConst
        {
            EnergyMax = 5,
            EnergyCostPerRun = 1,
            EnergyDailyResetHour = 5,
            EnergyAdRefillDailyLimit = 3,
            AdShopStaminaPerBuy = 10,
            AdShopStaminaDailyLimit = 10,
            AdShopGoldPerBuy = 10,
            AdShopGoldDailyLimit = 10,
            DefaultHeroId = 1,
            ExchangePointsForGoldCoins = 10,
            TalentChestNeedGold = 0,
            TalentNeedChestGold = 50
        };
        GameConst.Load(gameConst);
        return new GameTables(
            gameConst,
            new[]
            {
                new HandScoreConfig { Id = 1, Level = 1, Type = Config.HandType.HighCard, BasicMagnification = 1f },
                new HandScoreConfig { Id = 2, Level = 2, Type = Config.HandType.Couplet, BasicMagnification = 2f },
                new HandScoreConfig { Id = 3, Level = 3, Type = Config.HandType.Flush, BasicMagnification = 2.5f },
                new HandScoreConfig { Id = 4, Level = 4, Type = Config.HandType.Straight, BasicMagnification = 3.5f },
                new HandScoreConfig { Id = 5, Level = 5, Type = Config.HandType.StraightFlush, BasicMagnification = 5f },
                new HandScoreConfig { Id = 6, Level = 6, Type = Config.HandType.Leopard, BasicMagnification = 6f }
            },
            new[]
            {
                new LevelConfig { Id = 1001, Difficulty = 1, Level = 1 },
                new LevelConfig { Id = 1002, Difficulty = 1, Level = 2 }
            },
            new[]
            {
                new HeroConfig { Id = 1, UnlockCondition = 0 },
                new HeroConfig { Id = 2, UnlockCondition = 1 }
            },
            new[]
            {
                new RelicConfig { Id = 1, UnlockConditionId = 0, Price = 10, SellingPrice = 5, RefreshProbability = 1f },
                new RelicConfig { Id = 2, UnlockConditionId = 10401, Price = 20, SellingPrice = 8, RefreshProbability = 1f }
            },
            new[]
            {
                new UnlockConditionConfig
                {
                    Id = 10401,
                    Type = ContidionType.KillMonster,
                    StackedValue = 1,
                    Value = 20
                }
            },
            new[]
            {
                new TalentConfig { Id = 1, TalentId = 101, TalentLevel = 10 },
                new TalentConfig { Id = 2, TalentId = 102, TalentLevel = 10 }
            },
            Array.Empty<MonsterConfig>(),
            new[] { new ItemConfig { Id = 101, Name = "测试道具" } },
            "fallback");
    }
}
