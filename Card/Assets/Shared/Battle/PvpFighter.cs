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

        /// <summary>PvP 连胜。野怪轮不改、不发连胜奖励。</summary>
        public int WinStreak { get; set; }

        /// <summary>PvP 连败。野怪轮不改、不发连败奖励。</summary>
        public int LoseStreak { get; set; }

        public bool Alive { get; set; } = true;

        public int Rank { get; set; }

        public int RubLeft { get; set; }

        public int ReplaceLeft { get; set; }

        public int PeekLeft { get; set; }

        public bool IsBot { get; init; }

        public bool Disconnected { get; set; }

        public int RewardGold { get; set; }

        public List<int> OwnedRelicIds { get; } = new List<int>();

        /// <summary>与 <see cref="OwnedRelicIds"/> 对齐。1 = 未合成，3 = 三合一。</summary>
        public List<int> OwnedRelicPower { get; } = new List<int>();

        /// <summary>已解锁的圣物槽。开局 4，最多 10。</summary>
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
    }
}
