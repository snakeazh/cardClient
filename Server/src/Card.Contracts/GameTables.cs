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
    private readonly Dictionary<(int TalentId, int Level), TalentConfig> _talentByLevel;

    private readonly Dictionary<int, ItemConfig> _items;
    private readonly Dictionary<int, RelicConfig> _relics;
    private readonly Dictionary<int, HeroConfig> _heroes;
    private readonly Dictionary<int, HeroEntryConfig> _heroEntries;
    private readonly Dictionary<int, TalentEntryConfig> _talentEntries;
    private readonly Dictionary<int, RelicEntryConfig> _relicEntries;
    private readonly Dictionary<int, MonsterGroupConfig> _monsterGroups;
    private readonly Dictionary<(int MonsterId, int MonsterLevel), MonsterConfig> _monsters;
    private readonly Dictionary<int, PvpModeConfig> _pvpModes;

    public GameTables(
        GameConst gameConst,
        IReadOnlyList<HandScoreConfig> handScores,
        IReadOnlyList<LevelConfig> levels,
        IReadOnlyList<HeroConfig> heroes,
        IReadOnlyList<RelicConfig> relics,
        IReadOnlyList<UnlockConditionConfig> unlockConditions,
        IReadOnlyList<TalentConfig> talentRows,
        IReadOnlyList<MonsterConfig> monsters,
        IReadOnlyList<ItemConfig> items,
        string configHash,
        IReadOnlyList<HeroEntryConfig> heroEntries,
        IReadOnlyList<TalentEntryConfig> talentEntries,
        IReadOnlyList<RelicEntryConfig> relicEntries,
        IReadOnlyList<MonsterGroupConfig>? monsterGroups = null,
        IReadOnlyList<PvpModeConfig>? pvpModes = null,
        IReadOnlyList<PvpRoundConfig>? pvpRounds = null)
    {
        GameConst = gameConst;
        HandScores = handScores;
        Levels = levels;
        Heroes = heroes;
        Relics = relics;
        UnlockConditions = unlockConditions;
        TalentRows = talentRows;
        Monsters = monsters;
        Items = items;
        HeroEntries = heroEntries;
        TalentEntries = talentEntries;
        RelicEntries = relicEntries;
        MonsterGroups = monsterGroups ?? Array.Empty<MonsterGroupConfig>();
        PvpModes = pvpModes ?? Array.Empty<PvpModeConfig>();
        PvpRounds = pvpRounds ?? Array.Empty<PvpRoundConfig>();
        ConfigHash = configHash;
        _levels = Levels.Where(l => l.Id > 0).ToDictionary(l => l.Id);
        _items = Items.Where(i => i.Id > 0).GroupBy(i => i.Id).ToDictionary(g => g.Key, g => g.First());
        _relics = Relics.Where(r => r.Id > 0).GroupBy(r => r.Id).ToDictionary(g => g.Key, g => g.First());
        _heroes = Heroes.Where(h => h.Id > 0).GroupBy(h => h.Id).ToDictionary(g => g.Key, g => g.First());
        _heroEntries = HeroEntries.Where(e => e.Id > 0).GroupBy(e => e.Id).ToDictionary(g => g.Key, g => g.First());
        _talentEntries = TalentEntries.Where(e => e.Id > 0).GroupBy(e => e.Id).ToDictionary(g => g.Key, g => g.First());
        _relicEntries = RelicEntries.Where(e => e.Id > 0).GroupBy(e => e.Id).ToDictionary(g => g.Key, g => g.First());
        _monsterGroups = MonsterGroups.Where(g => g.Id > 0).GroupBy(g => g.Id).ToDictionary(g => g.Key, g => g.First());
        _monsters = new Dictionary<(int MonsterId, int MonsterLevel), MonsterConfig>();
        foreach (var monster in Monsters)
        {
            if (monster.MonsterId <= 0)
            {
                continue;
            }

            _monsters[(monster.MonsterId, monster.MonsterLevel)] = monster;
        }

        _pvpModes = PvpModes.Where(m => m.Id > 0).GroupBy(m => m.Id).ToDictionary(g => g.Key, g => g.First());
        Difficulties = Levels.Select(l => l.Difficulty).Distinct().OrderBy(d => d).ToArray();
        _maxLevelByDifficulty = Levels
            .GroupBy(l => l.Difficulty)
            .ToDictionary(g => g.Key, g => g.Max(l => l.Level));
        _talentMaxLevel = new Dictionary<int, int>();
        _talentByLevel = new Dictionary<(int TalentId, int Level), TalentConfig>();
        foreach (var row in TalentRows)
        {
            if (row.TalentId <= 0)
            {
                continue;
            }

            if (!_talentMaxLevel.TryGetValue(row.TalentId, out var current) || row.TalentLevel > current)
            {
                _talentMaxLevel[row.TalentId] = row.TalentLevel;
            }

            _talentByLevel[(row.TalentId, row.TalentLevel)] = row;
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

    public IReadOnlyList<HeroEntryConfig> HeroEntries { get; }

    public IReadOnlyList<TalentEntryConfig> TalentEntries { get; }

    public IReadOnlyList<RelicEntryConfig> RelicEntries { get; }

    public IReadOnlyList<MonsterGroupConfig> MonsterGroups { get; }

    public IReadOnlyList<PvpModeConfig> PvpModes { get; }

    public IReadOnlyList<PvpRoundConfig> PvpRounds { get; }

    public IReadOnlyList<int> Difficulties { get; }

    public bool TryGetLevel(int id, out LevelConfig level) => _levels.TryGetValue(id, out level!);

    public bool TryGetItem(int id, out ItemConfig item) => _items.TryGetValue(id, out item!);

    public bool TryGetRelic(int id, out RelicConfig relic) => _relics.TryGetValue(id, out relic!);

    public bool TryGetHero(int id, out HeroConfig hero) => _heroes.TryGetValue(id, out hero!);

    public bool TryGetHeroEntry(int id, out HeroEntryConfig entry) => _heroEntries.TryGetValue(id, out entry!);

    public bool TryGetTalentEntry(int id, out TalentEntryConfig entry) => _talentEntries.TryGetValue(id, out entry!);

    public bool TryGetRelicEntry(int id, out RelicEntryConfig entry) => _relicEntries.TryGetValue(id, out entry!);

    public bool TryGetMonsterGroup(int id, out MonsterGroupConfig group) => _monsterGroups.TryGetValue(id, out group!);

    public bool TryGetMonster(int monsterId, int monsterLevel, out MonsterConfig monster)
        => _monsters.TryGetValue((monsterId, monsterLevel), out monster!);

    public bool TryGetPvpMode(int id, out PvpModeConfig mode) => _pvpModes.TryGetValue(id, out mode!);

    public IReadOnlyList<PvpRoundConfig> GetPvpRounds(int modeId)
        => PvpRounds.Where(r => r.ModeId == modeId).OrderBy(r => r.Round).ToArray();

    public bool TryGetTalentRow(int talentId, int level, out TalentConfig row)
        => _talentByLevel.TryGetValue((talentId, level), out row!);

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
                new HeroConfig { Id = 1, UnlockCondition = 0, HeroEntryId = Array.Empty<int>() },
                new HeroConfig { Id = 2, UnlockCondition = 1, HeroEntryId = Array.Empty<int>() }
            },
            new[]
            {
                new RelicConfig { Id = 1, UnlockConditionId = 0, Price = 10, SellingPrice = 5, RefreshProbability = 1f, MechanismId = Array.Empty<int>() },
                new RelicConfig { Id = 2, UnlockConditionId = 10401, Price = 20, SellingPrice = 8, RefreshProbability = 1f, MechanismId = Array.Empty<int>() }
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
            "fallback",
            Array.Empty<HeroEntryConfig>(),
            Array.Empty<TalentEntryConfig>(),
            Array.Empty<RelicEntryConfig>());
    }
}
