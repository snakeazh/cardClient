using System.Collections.Generic;
using App.AdShop;
using App.Bag;
using App.Bootstrap;
using App.Energy;
using App.Guide;
using App.Level;
using App.Talent;
using App.Unlock;
using App.Wallet;
using CardShare.Contracts;
using Framework.Log;

namespace App.Net
{
    /// <summary>
    /// 服务器同步下来的 <see cref="PlayerProfileDto"/> 写入局外缓存的唯一入口。
    /// </summary>
    public static class ServerProfileApplier
    {
        public static void Apply(PlayerProfileDto profile)
        {
            if (profile == null || !AppServices.IsReady)
            {
                return;
            }

            AppServices.Resolve<IWalletService>().ReplaceFromServer(profile.Gold);

            var energy = profile.Energy ?? new EnergyDto();
            AppServices.Resolve<IEnergyService>().ReplaceFromServer(energy.Current, energy.AdRefillCount);

            var adShop = profile.AdShop ?? new AdShopDto();
            AppServices.Resolve<IAdShopService>().ReplaceFromServer(adShop.StaminaCount, adShop.GoldCount);

            ApplyTalent(profile.Talent);
            ApplyLevel(profile.Level);
            ApplyUnlock(profile.Unlock);
            ApplyBag(profile.Bag);
            AppServices.Resolve<IGuideProgressService>().ReplaceFromServer(profile.GuideCompletedGroupIds);

            AppServices.Resolve<IWalletService>().Save();
            AppServices.Resolve<IEnergyService>().Save();
            AppServices.Resolve<IAdShopService>().Save();
            AppServices.Resolve<ITalentService>().Save();
            AppServices.Resolve<ILevelProgressService>().Save();
            AppServices.Resolve<IUnlockConditionService>().Save();
            AppServices.Resolve<IBagService>().Save();
            AppServices.Resolve<IGuideProgressService>().Save();

            AppLog.Info(LogChannel.Net, $"applied profile gold={profile.Gold} energy={energy.Current}");
        }

        private static void ApplyTalent(TalentDto talent)
        {
            var entries = new List<TalentSaveEntry>();
            if (talent?.Entries != null)
            {
                for (var i = 0; i < talent.Entries.Length; i++)
                {
                    var row = talent.Entries[i];
                    if (row == null)
                    {
                        continue;
                    }

                    entries.Add(new TalentSaveEntry { TalentId = row.TalentId, Count = row.Count });
                }
            }

            AppServices.Resolve<ITalentService>().ReplaceFromServer(entries, talent?.DrawCount ?? 0);
        }

        private static void ApplyLevel(LevelProgressDto level)
        {
            var progress = new List<DifficultyProgressEntry>();
            if (level?.DifficultyProgress != null)
            {
                for (var i = 0; i < level.DifficultyProgress.Length; i++)
                {
                    var row = level.DifficultyProgress[i];
                    if (row == null)
                    {
                        continue;
                    }

                    progress.Add(new DifficultyProgressEntry
                    {
                        Difficulty = row.Difficulty,
                        HighestClearedLevel = row.HighestClearedLevel
                    });
                }
            }

            AppServices.Resolve<ILevelProgressService>().ReplaceFromServer(
                progress,
                level?.LastHeroId ?? 0,
                level?.LastLevelId ?? 0,
                level?.LastDifficulty ?? 0,
                level?.UnlockedHeroIds);
        }

        private static void ApplyUnlock(UnlockDto unlock)
        {
            var progress = new List<UnlockConditionProgressEntry>();
            if (unlock?.Progress != null)
            {
                for (var i = 0; i < unlock.Progress.Length; i++)
                {
                    var row = unlock.Progress[i];
                    if (row == null)
                    {
                        continue;
                    }

                    progress.Add(new UnlockConditionProgressEntry
                    {
                        ConditionId = row.ConditionId,
                        Amount = row.Amount
                    });
                }
            }

            AppServices.Resolve<IUnlockConditionService>().ReplaceFromServer(progress);
        }

        private static void ApplyBag(BagEntryDto[] bag)
        {
            var entries = new List<BagEntry>();
            if (bag != null)
            {
                for (var i = 0; i < bag.Length; i++)
                {
                    var row = bag[i];
                    if (row == null)
                    {
                        continue;
                    }

                    entries.Add(new BagEntry(row.ItemId, row.Count));
                }
            }

            AppServices.Resolve<IBagService>().ReplaceFromServer(entries);
        }
    }
}
