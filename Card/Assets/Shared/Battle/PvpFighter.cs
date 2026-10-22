using System;
using System.Collections.Generic;
using CardShare.Contracts;

#nullable enable

namespace CardShare.Battle
{
    public sealed class PvpFighter
    {
        public int SeatIndex { get; init; }

        public string UserId { get; init; } = string.Empty;

        public string NickName { get; init; } = string.Empty;

        public SeatSetup Combat { get; init; } = new SeatSetup();

        public int Hp { get; set; }

        public int MaxHp { get; set; }

        public int Gold { get; set; }

        /// <summary>PvP 连胜。野怪轮也加次数，但不发连胜奖励。</summary>
        public int WinStreak { get; set; }

        /// <summary>PvP 连败。野怪轮也加次数，但不发连败奖励。</summary>
        public int LoseStreak { get; set; }

        public bool Alive { get; set; } = true;

        /// <summary>死亡瞬间的战斗座位。轮空镜像机器人用它，不随后续清背包而丢掉圣物。</summary>
        public SeatSetup? DeathMirror { get; set; }

        /// <summary>死亡瞬间的持有圣物（未展开）。给快照和圣物列表用。</summary>
        public int[]? DeathRelicIds { get; set; }

        public int Rank { get; set; }

        public int RubLeft { get; set; }

        public int ReplaceLeft { get; set; }

        public int PeekLeft { get; set; }

        public bool IsBot { get; init; }

        public bool Disconnected { get; set; }

        public int RewardGold { get; set; }

        /// <summary>对局结束后的名次 PVP 分数加减；未结束为 0。可为负。</summary>
        public int RewardScore { get; set; }

        public List<int> OwnedRelicIds { get; } = new List<int>();

        /// <summary>与 <see cref="OwnedRelicIds"/> 对齐。每份为 1，多份各占一条。</summary>
        public List<int> OwnedRelicPower { get; } = new List<int>();

        /// <summary>已解锁的圣物槽。开局为 GameConst.PVPRelicInitialRelicNum，上限为初始槽加解锁价格档数。</summary>
        public int RelicSlots { get; set; } = PvpRelicBag.FreeSlots;

        public int[] ShopPoolIds { get; set; } = Array.Empty<int>();

        public List<int> ShopOfferIds { get; } = new List<int>();

        public int ShopRefreshCount { get; set; }

        public int FreeShopRefreshLeft { get; set; }

        public bool ShopDone { get; set; }

        /// <summary>各牌型本局亮出次数（炸弹恶魔）。下标对齐 Shared HandType。</summary>
        public int[] HandTypeShowCounts { get; } = new int[PvpRelicRuntime.HandTypeCountSlots];

        /// <summary>高档饮品剩余自衰减倍率。</summary>
        public Dictionary<int, float> RelicSelfDecayMag { get; } = new Dictionary<int, float>();

        /// <summary>高档甜品剩余自衰减攻击。</summary>
        public Dictionary<int, int> SelfDecayAttackLeft { get; } = new Dictionary<int, int>();

        /// <summary>消费主义商店刷新计数。</summary>
        public Dictionary<int, int> RelicShopRefreshCounts { get; } = new Dictionary<int, int>();

        /// <summary>贪婪胜负累计倍率。</summary>
        public Dictionary<int, float> RelicWinLoseMag { get; } = new Dictionary<int, float>();

        /// <summary>本局消耗品使用次数（UseConsumableGetMult / GetAttack 的计数源）。</summary>
        public int ConsumableUsesThisRun { get; set; }

        /// <summary>复制本回合选中的圣物 Id。</summary>
        public int CopiedRelicId { get; set; }

        /// <summary>凤凰羽毛（UseRoundNullify）已激活：本桌比牌结算时受到的伤害变为 0，结算后清除。</summary>
        public bool NullifyDamageNextHit { get; set; }

        /// <summary>圣光燃烧已发动次数。本局保留，后续出手按这个次数加伤。</summary>
        public int HolyLightCasts { get; set; }

        /// <summary>命运羁绊：每次攻击叠加的永久倍率。</summary>
        public float BindingMagForever { get; set; }

        /// <summary>注魔硬币：每次消费叠加的永久伤害加成。</summary>
        public float MagicCoinDamageBonus { get; set; }

        /// <summary>权力羁绊层数。</summary>
        public int NobilityStacks { get; set; }

        /// <summary>沉默诅咒：羁绊复活剩余次数。</summary>
        public int BindingReviveLeft { get; set; }

        /// <summary>沉默诅咒：是否已武装复活次数。</summary>
        public bool BindingReviveArmed { get; set; }

        /// <summary>属性强化：永久暴击率加成。</summary>
        public float BindingCritBonus { get; set; }

        /// <summary>幸运点数：已永久强化的数字牌点数（Rank 整型）。</summary>
        public HashSet<int> LuckyEnhancedRanks { get; } = new HashSet<int>();

        /// <summary>断舍离 / 牌型图鉴等永久攻击。</summary>
        public int PermanentAttackBonus { get; set; }

        /// <summary>战鼓：本回合临时攻击。</summary>
        public int RoundTempAttack { get; set; }

        /// <summary>雇佣兵：本回合付不起而失效的圣物 Id。</summary>
        public HashSet<int> RoundPayDisabledRelicIds { get; } = new HashSet<int>();

        /// <summary>牌型图鉴：已拿过首次牌型攻击奖励的牌型。</summary>
        public HashSet<int> FirstTypeAttackKinds { get; } = new HashSet<int>();

        /// <summary>本回合用过搓/透/替。</summary>
        public bool UsedSkillThisRound { get; set; }

        /// <summary>本回合实际扣过血。</summary>
        public bool TookDamageThisRound { get; set; }

        /// <summary>本回合已完成的比牌次数（出伤前用于首次比牌判定）。</summary>
        public int RoundCompareCount { get; set; }

        /// <summary>本回合击杀数。</summary>
        public int RoundKills { get; set; }

        /// <summary>心理战等：本回合自身牌型降级步数。</summary>
        public int RoundTypeDowngrade { get; set; }

        /// <summary>沉默诅咒：下一次比牌对该座位强制判负。</summary>
        public bool SilencedNextCompare { get; set; }
    }
}
