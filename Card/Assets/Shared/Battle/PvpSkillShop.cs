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
    /// 1. 次数按「局」走：新对局开局初始化为基数；轮与轮之间不回满，用掉的就少；
    /// 2. 商店买 1 次 = 待生效 +1，下一轮开局并入剩余（不改变商店阶段手上的剩余）；
    /// 3. 本局内买到的圣物/词条若抬高技能基数，下一轮开局只把增量加进剩余，不回满；
    /// 4. 单价必须高于 <c>EverySkillProvideGold</c>（未用技能折算金币，缺省 20）：
    ///    否则「买入 → 不用 → 结算返金」就能套利。
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

        /// <summary>当前剩余次数（不含待生效购买）。商店阶段是上一轮打完剩下的量。</summary>
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
        /// 商店按钮分子：手上剩余 + 本店已购待生效。
        /// 用光 0/1 再买一次显示 1/1，而不是 0/2。
        /// </summary>
        public static int ShopLeft(PvpFighter fighter, int skillId)
            => Math.Max(0, Left(fighter, skillId) + Pending(fighter, skillId));

        /// <summary>
        /// 开局/当前已并入剩余的技能基数（英雄/圣物/天赋）。用于跨轮只加增量、不回满。
        /// </summary>
        public static int AppliedBase(PvpFighter fighter, int skillId)
        {
            if (fighter == null)
            {
                return 0;
            }

            switch (skillId)
            {
                case PvpSkillIds.Rub:
                    return Math.Max(0, fighter.AppliedSkillBaseRub);
                case PvpSkillIds.Replace:
                    return Math.Max(0, fighter.AppliedSkillBaseReplace);
                case PvpSkillIds.Peek:
                    return Math.Max(0, fighter.AppliedSkillBasePeek);
                default:
                    return 0;
            }
        }

        /// <summary>
        /// 技能基数 + 圣物/英雄/天赋词条加成（不含商店已购 pending）。
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

        /// <summary>
        /// 商店展示分母 = 当前基数（英雄/圣物/天赋），不含已购。
        /// 用光 0/3；买一次分子 +1（见 <see cref="ShopLeft"/>），分母不变，可出现 2/1。
        /// </summary>
        public static int Max(IGameTables tables, PvpFighter fighter, int skillId)
        {
            if (fighter == null || !IsValid(skillId))
            {
                return 0;
            }

            return Base(tables, fighter, skillId);
        }

        /// <summary>新对局开局：剩余次数拉到基数，记已并入基数，清 pending。</summary>
        public static void ApplyMatchStart(IGameTables tables, PvpFighter fighter)
        {
            if (tables == null || fighter == null)
            {
                return;
            }

            SetLeftAndApplied(fighter, PvpSkillIds.Rub, Base(tables, fighter, PvpSkillIds.Rub));
            SetLeftAndApplied(fighter, PvpSkillIds.Peek, Base(tables, fighter, PvpSkillIds.Peek));
            SetLeftAndApplied(fighter, PvpSkillIds.Replace, Base(tables, fighter, PvpSkillIds.Replace));
            ClearPending(fighter);
        }

        /// <summary>
        /// 同一局内进入新一轮：只把商店购买与基数增量加进剩余，不把已用次数回满。
        /// </summary>
        public static void ApplyRoundCarry(IGameTables tables, PvpFighter fighter)
        {
            if (tables == null || fighter == null)
            {
                return;
            }

            CarryOne(tables, fighter, PvpSkillIds.Rub);
            CarryOne(tables, fighter, PvpSkillIds.Peek);
            CarryOne(tables, fighter, PvpSkillIds.Replace);
            ClearPending(fighter);
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

        /// <summary>下一轮开始时把待生效次数并进剩余后清零。</summary>
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

        private static void CarryOne(IGameTables tables, PvpFighter fighter, int skillId)
        {
            var nextBase = Base(tables, fighter, skillId);
            var applied = AppliedBase(fighter, skillId);
            // 基数升高只加增量；钝手等降基数按差值扣剩余（不回满已用次数）。
            var growth = nextBase - applied;
            var pending = Pending(fighter, skillId);
            var left = Math.Max(0, Left(fighter, skillId) + growth + pending);
            SetLeftAndApplied(fighter, skillId, left, nextBase);
        }

        private static void SetLeftAndApplied(PvpFighter fighter, int skillId, int left)
            => SetLeftAndApplied(fighter, skillId, left, left);

        private static void SetLeftAndApplied(PvpFighter fighter, int skillId, int left, int appliedBase)
        {
            left = Math.Max(0, left);
            appliedBase = Math.Max(0, appliedBase);
            switch (skillId)
            {
                case PvpSkillIds.Rub:
                    fighter.RubLeft = left;
                    fighter.AppliedSkillBaseRub = appliedBase;
                    break;
                case PvpSkillIds.Replace:
                    fighter.ReplaceLeft = left;
                    fighter.AppliedSkillBaseReplace = appliedBase;
                    break;
                case PvpSkillIds.Peek:
                    fighter.PeekLeft = left;
                    fighter.AppliedSkillBasePeek = appliedBase;
                    break;
            }
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
