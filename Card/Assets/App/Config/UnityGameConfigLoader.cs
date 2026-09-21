using System.Collections.Generic;
using CardShare.Contracts;
using CardShare.Contracts.Config;

namespace App.Config
{
    /// <summary>
    /// ConfigTables 已把 JSON 灌进各共享配表类的静态注册表，这里直接从注册表组装 IGameTables。
    /// 服务端走 FileGameConfigLoader。
    /// </summary>
    public static class UnityGameConfigLoader
    {
        public static IGameTables Current { get; private set; }

        public static IGameTables LoadFromAppConfig()
        {
            var tables = new GameTables(
                GameConst.Instance,
                Rows(HandScoreConfig.All),
                Rows(LevelConfig.All),
                Rows(HeroConfig.All),
                Rows(RelicConfig.All),
                Rows(UnlockConditionConfig.All),
                Rows(TalentConfig.All),
                Rows(MonsterConfig.All),
                Rows(ItemConfig.All),
                "unity",
                Rows(HeroEntryConfig.All),
                Rows(TalentEntryConfig.All),
                Rows(RelicEntryConfig.All),
                Rows(MonsterGroupConfig.All),
                Rows(PvpModeConfig.All),
                Rows(PvpRoundConfig.All),
                Rows(PvpBotConfig.All));
            Current = tables;
            return tables;
        }

        private static List<T> Rows<T>(IReadOnlyDictionary<int, T> map)
        {
            return new List<T>(map.Values);
        }
    }
}
