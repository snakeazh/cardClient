using System;
using CardShare.Contracts;
using CardShare.Contracts.Config;

#nullable enable

namespace CardShare.Battle
{
    /// <summary>
    /// PvP 商店专属：花金币补充技能使用次数（搓牌 / 替换 / 透视）。
    ///
    /// 规则：
    /// 1. 一次购买 = 下一轮开始时该技能 +1 次，不改变商店阶段手上剩余的次数；
    /// 2. 只在下一轮生效（<see cref="PvpMatch"/> 的 ResetSkills 并入上限后清零），不跨轮累积；
    /// 3. 单价必须高于 <c>EverySkillProvideGold</c>（未用技能折算金币，缺省 20）：
    ///    否则「买入 → 不用 → 结算返金」就能套利。一次性生效也保证不会每轮白拿返金。
    ///
    /// 价格与基础次数口径都放在这里，服务端（PvpMatch/ProjectShop）与客户端（展示）同源。
    /// 未配置 GameConst 的 PVPSkillNeedGold 时按下表；将来要进表，照 <see cref="PvpRelicBag"/> 的写法加可选覆盖即可。
    /// </summary>
    public static class PvpSkillShop
    {
        /// <summary>第一次购买搓牌次数（搓牌 = PvpSkillIds.Rub = fighter.RubLeft）。</summary>
        public const int RubBaseGold = 60;

        /// <summary>第一次购买替换次数。</summary>
        public const int ReplaceBaseGold = 80;

        /// <summary>第一次购买透视次数。</summary>
        public const int PeekBaseGold = 100;

        /// <summary>同一技能本局每多买一次，价格递增的金币。</summary>
        public const int StepGold = 20;

        /// <summary>搓牌基础次数（未配置 GameConst.DefaultSkillShuffleNum 时）。</summary>
        public const int RubBaseCount = 3;

        /// <summary>替换基础次数（未配置 GameConst.DefaultSkillReplaceNum 时）。</summary>
        public const int ReplaceBaseCount = 1;

        /// <summary>透视基础次数（未配置 GameConst.DefaultSkillPerspectiveNum 时）。</summary>
        public const int PeekBaseCount = 1;

        public static bool IsValid(int skillId)
            => skillId >= PvpSkillIds.Rub && skillId < PvpSkillIds.Rub + PvpSkillIds.Count;

        /// <summary>本局该技能已购买次数（价格递增的依据）。</summary>
        public static int Bought(PvpFighter fighter, int skillId)
        {
            if (fighter == null)
            {
                return 0;
            }

            switch (skillId)
            {
                case PvpSkillIds.Rub:
                    return fighter.BoughtRub;
                case PvpSkillIds.Replace:
                    return fighter.BoughtReplace;
                case PvpSkillIds.Peek:
                    return fighter.BoughtPeek;
                default:
                    return 0;
            }
        }

        /// <summary>本商店买了、要等下一轮才生效的次数。</summary>
        public static int Pending(PvpFighter fighter, int skillId)
        {
            if (fighter == null)
            {
                return 0;
            }

            switch (skillId)
            {
                case PvpSkillIds.Rub:
                    return fighter.PendingRub;
                case PvpSkillIds.Replace:
                    return fighter.PendingReplace;
                case PvpSkillIds.Peek:
                    return fighter.PendingPeek;
                default:
                    return 0;
            }
        }

        /// <summary>下一次购买的金币价；技能 Id 非法返回 0。</summary>
        public static int Price(PvpFighter fighter, int skillId)
        {
            if (fighter == null || !IsValid(skillId))
            {
                return 0;
            }

            var first = FirstGold(skillId);
            var step = StepGold * Math.Max(0, Bought(fighter, skillId));
            return Math.Max(0, first + step);
        }

        /// <summary>当前剩余次数。商店阶段是上一轮打完剩下的量。</summary>
        public static int Left(PvpFighter fighter, int skillId)
        {
            if (fighter == null)
            {
                return 0;
            }

            switch (skillId)
            {
                case PvpSkillIds.Rub:
                    return Math.Max(0, fighter.RubLeft);
                case PvpSkillIds.Replace:
                    return Math.Max(0, fighter.ReplaceLeft);
                case PvpSkillIds.Peek:
                    return Math.Max(0, fighter.PeekLeft);
                default:
                    return 0;
            }
        }

        /// <summary>
        /// 每轮刷新时的基础次数 + 圣物/英雄/天赋词条加成（口径同 <see cref="PvpMatch"/> ResetSkills）。
        /// 不含本商店已购的部分。
        /// </summary>
        public static int Base(IGameTables tables, PvpFighter fighter, int skillId)
        {
            if (tables == null || fighter == null || !IsValid(skillId))
            {
                return 0;
            }

            var baseCount = BaseCount(tables.GameConst, skillId);
            var mechanism = MechanismOf(skillId);
            var combat = fighter.Combat;
            var carried = PvpRelicBag.Expand(fighter);
            var bonus = CombatBonuses.SumSkillCountBonus(tables, carried, combat.HeroId, combat.Talents, mechanism);
            return Math.Max(0, baseCount + bonus);
        }

        /// <summary>下一轮开始时的次数上限 = 基础加成 + 本商店已购。</summary>
        public static int Max(IGameTables tables, PvpFighter fighter, int skillId)
        {
            if (fighter == null || !IsValid(skillId))
            {
                return 0;
            }

            return Base(tables, fighter, skillId) + Pending(fighter, skillId);
        }

        /// <summary>购买计数 +1（价格递增）与待生效次数 +1。扣金/校验在 PvpMatch 里做。</summary>
        public static void AddPending(PvpFighter fighter, int skillId)
        {
            if (fighter == null || !IsValid(skillId))
            {
                return;
            }

            switch (skillId)
            {
                case PvpSkillIds.Rub:
                    fighter.PendingRub++;
                    fighter.BoughtRub++;
                    break;
                case PvpSkillIds.Replace:
                    fighter.PendingReplace++;
                    fighter.BoughtReplace++;
                    break;
                case PvpSkillIds.Peek:
                    fighter.PendingPeek++;
                    fighter.BoughtPeek++;
                    break;
            }
        }

        /// <summary>下一轮开始时把待生效次数并进上限，然后清零。</summary>
        public static void ClearPending(PvpFighter fighter)
        {
            if (fighter == null)
            {
                return;
            }

            fighter.PendingRub = 0;
            fighter.PendingReplace = 0;
            fighter.PendingPeek = 0;
        }

        private static int FirstGold(int skillId)
        {
            switch (skillId)
            {
                case PvpSkillIds.Rub:
                    return RubBaseGold;
                case PvpSkillIds.Replace:
                    return ReplaceBaseGold;
                case PvpSkillIds.Peek:
                    return PeekBaseGold;
                default:
                    return 0;
            }
        }

        private static int BaseCount(GameConst gameConst, int skillId)
        {
            switch (skillId)
            {
                case PvpSkillIds.Rub:
                    var rub = gameConst != null ? gameConst.DefaultSkillShuffleNum : 0;
                    return rub > 0 ? rub : RubBaseCount;
                case PvpSkillIds.Replace:
                    var replace = gameConst != null ? gameConst.DefaultSkillReplaceNum : 0;
                    return replace > 0 ? replace : ReplaceBaseCount;
                case PvpSkillIds.Peek:
                    var peek = gameConst != null ? gameConst.DefaultSkillPerspectiveNum : 0;
                    return peek > 0 ? peek : PeekBaseCount;
                default:
                    return 0;
            }
        }

        private static MechanismType MechanismOf(int skillId)
        {
            switch (skillId)
            {
                case PvpSkillIds.Rub:
                    return MechanismType.RubbingCardsNum;
                case PvpSkillIds.Replace:
                    return MechanismType.ReplaceCardsNum;
                case PvpSkillIds.Peek:
                    return MechanismType.PerspectiveNum;
                default:
                    return MechanismType.RubbingCardsNum;
            }
        }
    }
}
