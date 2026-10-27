using System.Collections.Generic;
using CardShare.Contracts.Config;

#nullable enable

namespace CardShare.Contracts
{
    /// <summary>
    /// PVP 对局视图：把 <see cref="IGameTables.Relics"/> / TryGetRelic(Entry) 指到 PvpRelic 表，
    /// 让 RelicCombat / BattleEngine / CombatBonuses 等共享结算无需再分 PVE/PVP 分支。
    /// </summary>
    public sealed class PvpGameTables : IGameTables
    {
        private readonly IGameTables _inner;

        private PvpGameTables(IGameTables inner)
        {
            _inner = inner;
        }

        public static IGameTables Wrap(IGameTables tables)
        {
            if (tables == null || tables is PvpGameTables)
            {
                return tables!;
            }

            return new PvpGameTables(tables);
        }

        public string ConfigHash => _inner.ConfigHash;

        public GameConst GameConst => _inner.GameConst;

        public IReadOnlyList<HandScoreConfig> HandScores => _inner.HandScores;

        public IReadOnlyList<LevelConfig> Levels => _inner.Levels;

        public IReadOnlyList<HeroConfig> Heroes => _inner.Heroes;

        public IReadOnlyList<RelicConfig> Relics => _inner.PvpRelics;

        public IReadOnlyList<RelicConfig> PvpRelics => _inner.PvpRelics;

        public IReadOnlyList<UnlockConditionConfig> UnlockConditions => _inner.UnlockConditions;

        public IReadOnlyList<TalentConfig> TalentRows => _inner.TalentRows;

        public IReadOnlyList<MonsterConfig> Monsters => _inner.Monsters;

        public IReadOnlyList<ItemConfig> Items => _inner.Items;

        public IReadOnlyList<HeroEntryConfig> HeroEntries => _inner.HeroEntries;

        public IReadOnlyList<TalentEntryConfig> TalentEntries => _inner.TalentEntries;

        public IReadOnlyList<RelicEntryConfig> RelicEntries => _inner.PvpRelicEntries;

        public IReadOnlyList<RelicEntryConfig> PvpRelicEntries => _inner.PvpRelicEntries;

        public IReadOnlyList<MonsterGroupConfig> MonsterGroups => _inner.MonsterGroups;

        public IReadOnlyList<PvpModeConfig> PvpModes => _inner.PvpModes;

        public IReadOnlyList<PvpRoundConfig> PvpRounds => _inner.PvpRounds;

        public IReadOnlyList<PvpBotConfig> PvpBots => _inner.PvpBots;

        public IReadOnlyList<BindingConfig> Bindings => _inner.Bindings;

        public IReadOnlyList<int> Difficulties => _inner.Difficulties;

        public bool TryGetLevel(int id, out LevelConfig level) => _inner.TryGetLevel(id, out level);

        public bool TryGetItem(int id, out ItemConfig item) => _inner.TryGetItem(id, out item);

        public bool TryGetRelic(int id, out RelicConfig relic) => _inner.TryGetPvpRelic(id, out relic);

        public bool TryGetPvpRelic(int id, out RelicConfig relic) => _inner.TryGetPvpRelic(id, out relic);

        public bool TryGetHero(int id, out HeroConfig hero) => _inner.TryGetHero(id, out hero);

        public bool TryGetHeroEntry(int id, out HeroEntryConfig entry) => _inner.TryGetHeroEntry(id, out entry);

        public bool TryGetTalentEntry(int id, out TalentEntryConfig entry) => _inner.TryGetTalentEntry(id, out entry);

        public bool TryGetRelicEntry(int id, out RelicEntryConfig entry) => _inner.TryGetPvpRelicEntry(id, out entry);

        public bool TryGetPvpRelicEntry(int id, out RelicEntryConfig entry) => _inner.TryGetPvpRelicEntry(id, out entry);

        public bool TryGetMonsterGroup(int id, out MonsterGroupConfig group) => _inner.TryGetMonsterGroup(id, out group);

        public bool TryGetMonster(int monsterId, int monsterLevel, out MonsterConfig monster)
            => _inner.TryGetMonster(monsterId, monsterLevel, out monster);

        public bool TryGetPvpMode(int id, out PvpModeConfig mode) => _inner.TryGetPvpMode(id, out mode);

        public bool TryGetPvpBot(int id, out PvpBotConfig bot) => _inner.TryGetPvpBot(id, out bot);

        public bool TryGetBinding(int id, out BindingConfig binding) => _inner.TryGetBinding(id, out binding);

        public IReadOnlyList<PvpRoundConfig> GetPvpRounds(int modeId) => _inner.GetPvpRounds(modeId);

        public bool TryGetTalentRow(int talentId, int level, out TalentConfig row)
            => _inner.TryGetTalentRow(talentId, level, out row);

        public int GetMaxLevel(int difficulty) => _inner.GetMaxLevel(difficulty);

        public int GetTalentMaxLevel(int talentId) => _inner.GetTalentMaxLevel(talentId);
    }
}
