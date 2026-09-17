using System.Collections.Generic;
using CardShare.Contracts;
using SharedConst = CardShare.Contracts.Config.GameConst;
using SharedHandScore = CardShare.Contracts.Config.HandScoreConfig;
using SharedHero = CardShare.Contracts.Config.HeroConfig;
using SharedLevel = CardShare.Contracts.Config.LevelConfig;
using SharedItem = CardShare.Contracts.Config.ItemConfig;
using SharedMonster = CardShare.Contracts.Config.MonsterConfig;
using SharedRelic = CardShare.Contracts.Config.RelicConfig;
using SharedRelicEntry = CardShare.Contracts.Config.RelicEntryConfig;
using SharedTalent = CardShare.Contracts.Config.TalentConfig;
using SharedTalentEntry = CardShare.Contracts.Config.TalentEntryConfig;
using SharedHeroEntry = CardShare.Contracts.Config.HeroEntryConfig;
using SharedUnlock = CardShare.Contracts.Config.UnlockConditionConfig;
using SharedMechanism = CardShare.Contracts.Config.MechanismType;
using SharedHandType = CardShare.Contracts.Config.HandType;
using SharedQuality = CardShare.Contracts.Config.QualityType;
using SharedMonsterType = CardShare.Contracts.Config.MonsterType;
using SharedCondition = CardShare.Contracts.Config.ContidionType;

namespace App.Config
{
    /// <summary>
    /// Unity 用 JsonUtility / ConfigTables 读表后，填进共享 IGameTables。服务端走 FileGameConfigLoader。
    /// </summary>
    public static class UnityGameConfigLoader
    {
        public static IGameTables Current { get; private set; }

        public static IGameTables LoadFromAppConfig()
        {
            var tables = new GameTables(
                CopyConst(GameConst.Instance),
                Map(HandScoreConfig.All.Values, ToHandScore),
                Map(LevelConfig.All.Values, ToLevel),
                Map(HeroConfig.All.Values, ToHero),
                Map(RelicConfig.All.Values, ToRelic),
                Map(UnlockConditionConfig.All.Values, ToUnlock),
                Map(TalentConfig.All.Values, ToTalent),
                Map(MonsterConfig.All.Values, ToMonster),
                Map(ItemConfig.All.Values, ToItem),
                "unity",
                Map(HeroEntryConfig.All.Values, ToHeroEntry),
                Map(TalentEntryConfig.All.Values, ToTalentEntry),
                Map(RelicEntryConfig.All.Values, ToRelicEntry));
            SharedConst.Load(tables.GameConst);
            Current = tables;
            return tables;
        }

        private static List<TOut> Map<TIn, TOut>(IEnumerable<TIn> rows, System.Func<TIn, TOut> map)
        {
            var list = new List<TOut>();
            if (rows == null)
            {
                return list;
            }

            foreach (var row in rows)
            {
                if (row != null)
                {
                    list.Add(map(row));
                }
            }

            return list;
        }

        private static SharedConst CopyConst(GameConst src)
        {
            var dst = new SharedConst();
            if (src == null)
            {
                return dst;
            }

            dst.GameFps = src.GameFps;
            dst.DefaultHeroId = src.DefaultHeroId;
            dst.ChipsForPoints = src.ChipsForPoints;
            dst.ExchangePointsForGoldCoins = src.ExchangePointsForGoldCoins;
            dst.EnergyMax = src.EnergyMax;
            dst.EnergyCostPerRun = src.EnergyCostPerRun;
            dst.EnergyDailyResetHour = src.EnergyDailyResetHour;
            dst.EnergyAdRefillDailyLimit = src.EnergyAdRefillDailyLimit;
            dst.AdShopStaminaPerBuy = src.AdShopStaminaPerBuy;
            dst.AdShopStaminaDailyLimit = src.AdShopStaminaDailyLimit;
            dst.AdShopGoldPerBuy = src.AdShopGoldPerBuy;
            dst.AdShopGoldDailyLimit = src.AdShopGoldDailyLimit;
            dst.TalentChestNeedGold = src.TalentChestNeedGold;
            dst.TalentNeedChestGold = src.TalentNeedChestGold;
            dst.KillMonsterGetGold = src.KillMonsterGetGold;
            dst.PlayerInitialGoldNum = src.PlayerInitialGoldNum;
            dst.ShopRefreshFirst = src.ShopRefreshFirst;
            dst.ShopRefreshAfter = src.ShopRefreshAfter;
            dst.ShopRefreshGoldUpNumMax = src.ShopRefreshGoldUpNumMax;
            dst.DefaultRelicNumMax = src.DefaultRelicNumMax;
            return dst;
        }

        private static SharedHandScore ToHandScore(HandScoreConfig row)
        {
            return new SharedHandScore
            {
                Id = row.Id,
                Name = row.Name,
                Level = row.Level,
                Type = (SharedHandType)(int)row.Type,
                BasicMagnification = row.BasicMagnification
            };
        }

        private static SharedLevel ToLevel(LevelConfig row)
        {
            return new SharedLevel
            {
                Id = row.Id,
                Difficulty = row.Difficulty,
                Level = row.Level,
                LevelEntryNum = row.LevelEntryNum,
                MonsterGroup = row.MonsterGroup,
                GetGold = row.GetGold,
                MonsterCardHandScoreLevelLimit = row.MonsterCardHandScoreLevelLimit
            };
        }

        private static SharedHero ToHero(HeroConfig row)
        {
            return new SharedHero
            {
                Id = row.Id,
                Name = row.Name,
                Hp = row.Hp,
                HeroDamage = row.HeroDamage,
                Critical = row.Critical,
                CriticalDamage = row.CriticalDamage,
                Icon = row.Icon,
                HeroEntryId = row.HeroEntryId,
                UnlockCondition = row.UnlockCondition,
                Desc = row.Desc
            };
        }

        private static SharedRelic ToRelic(RelicConfig row)
        {
            return new SharedRelic
            {
                Id = row.Id,
                Type = (SharedQuality)(int)row.Type,
                RefreshProbability = row.RefreshProbability,
                Name = row.Name,
                Icon = row.Icon,
                Price = row.Price,
                SellingPrice = row.SellingPrice,
                MechanismId = row.MechanismId,
                UnlockConditionId = row.UnlockConditionId,
                UseType = row.UseType,
                Desc = row.Desc
            };
        }

        private static SharedUnlock ToUnlock(UnlockConditionConfig row)
        {
            return new SharedUnlock
            {
                Id = row.Id,
                Type = (SharedCondition)(int)row.Type,
                StackedValue = row.StackedValue,
                Value = row.Value,
                Desc = row.Desc
            };
        }

        private static SharedTalent ToTalent(TalentConfig row)
        {
            return new SharedTalent
            {
                Id = row.Id,
                TalentId = row.TalentId,
                Name = row.Name,
                Type = (SharedQuality)(int)row.Type,
                ChestProbability = row.ChestProbability,
                Icon = row.Icon,
                TalentLevel = row.TalentLevel,
                TalentEntry = row.TalentEntry,
                Desc = row.Desc
            };
        }

        private static SharedHeroEntry ToHeroEntry(HeroEntryConfig row)
        {
            return new SharedHeroEntry
            {
                Id = row.Id,
                Name = row.Name,
                Type = (SharedMechanism)(int)row.Type,
                Value = row.Value,
                Desc = row.Desc
            };
        }

        private static SharedTalentEntry ToTalentEntry(TalentEntryConfig row)
        {
            return new SharedTalentEntry
            {
                Id = row.Id,
                Name = row.Name,
                Type = (SharedMechanism)(int)row.Type,
                Value = row.Value,
                Desc = row.Desc
            };
        }

        private static SharedRelicEntry ToRelicEntry(RelicEntryConfig row)
        {
            return new SharedRelicEntry
            {
                Id = row.Id,
                Name = row.Name,
                Type = (SharedMechanism)(int)row.Type,
                Value = row.Value,
                Desc = row.Desc
            };
        }

        private static SharedItem ToItem(ItemConfig row)
        {
            return new SharedItem
            {
                Id = row.Id,
                Name = row.Name,
                Icon = row.Icon,
                NumOfUsesPerRound = row.NumOfUsesPerRound,
                Desc = row.Desc
            };
        }

        private static SharedMonster ToMonster(MonsterConfig row)
        {
            return new SharedMonster
            {
                Id = row.Id,
                MonsterId = row.MonsterId,
                MonsterLevel = row.MonsterLevel,
                MonsterHp = row.MonsterHp,
                MonsterDamage = row.MonsterDamage,
                Type = (SharedMonsterType)(int)row.Type,
                Name = row.Name,
                Desc = row.Desc,
                Icon = row.Icon,
                BaseMap = row.BaseMap,
                HealthBar = row.HealthBar,
                MonsterEntry = row.MonsterEntry
            };
        }
    }
}
