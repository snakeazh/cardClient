using System;
using System.Collections.Generic;

namespace App.Game
{
    /// <summary>单局流程阶段。看牌/搓牌/下注/摊牌/攻击/商店都走这里。</summary>
    public enum GamePhase
    {
        Idle = 0,
        /// <summary>看牌后搓牌，必须选一张或跳过。</summary>
        WaitingRub = 1,
        /// <summary>下注街：跟/加/弃/开牌（旧流程保留）。</summary>
        Betting = 2,
        Showdown = 3,
        RoundSettle = 4,
        Shop = 5,
        StageFail = 6,
        RunComplete = 7,
        /// <summary>比牌后播放攻击演出。</summary>
        WaitingAttack = 8,
        /// <summary>发牌后选择看牌或闷注（旧流程保留）。</summary>
        WaitingLookChoice = 9,
        /// <summary>发牌并看牌后：只能开牌或使用技能。</summary>
        WaitingOpen = 10
    }

    public enum ConsumableId
    {
        SplashSlash = 0,
        Magnifier = 1,
        LoanTicket = 2,
        RubCharge = 3,
        XRayCharge = 4,
        ReplaceCharge = 5
    }

    /// <summary>BOSS 关随机词缀。禁搓/禁计分改牌面，其余改经济或对局规则。</summary>
    public enum BossAffix
    {
        None = 0,
        BanRubHeart = 1,
        BanRubSpade = 2,
        BanRubDiamond = 3,
        BanRubClub = 4,
        BanRubFace = 5,
        BanScoreHeart = 6,
        BanScoreSpade = 7,
        BanScoreDiamond = 8,
        BanScoreClub = 9,
        BanScoreFace = 10,
        /// <summary>结算倍率和血量收益减半。</summary>
        Flint = 11,
        /// <summary>本局随机禁用一件道具或遗物。</summary>
        Edge = 12,
        /// <summary>比牌前偷看玩家最终牌型。</summary>
        XRay = 13,
        /// <summary>玩家下注消耗 ×1.5。</summary>
        AntiRaise = 14,
        /// <summary>破产救助比例降为 5%。</summary>
        Stingy = 15
    }

    /// <summary>商店货架 UI 包装，商品来自 <c>RelicConfig</c>。</summary>
    public sealed class ShopItemDef
    {
        public string Id;
        public string Name;
        public string Effect;
        public int Price;
        public int RelicConfigId;
    }

    /// <summary>闯关数值：开局血量、敌人数量/血量、下注下限、商店与救助。</summary>
    public static class GameBalance
    {
        public const int PlayerStartChips = 800;
        public const int PlayerStartHp = 4000;
        /// <summary>最小下注单位，也是每关第一回合基础注。</summary>
        public const int MinBet = 10;
        /// <summary>每关第一回合基础下注。</summary>
        public const int BaseBetStart = 10;
        /// <summary>同一关内每过一回合，基础注 +10。</summary>
        public const int BaseBetStep = 10;
        public const int RaiseLowMult = 2;
        public const int RaiseHighMult = 4;
        /// <summary>已购道具上限，对应 GameUI equip1～equip3 与商店已拥有栏。</summary>
        public const int MaxRelics = 3;
        /// <summary>通关商店货架格子数，对应 BattleShopPop.sellHor。</summary>
        public const int ShopOfferCount = 3;
        public const float RescueRatio = 0.15f;
        public const float StingyRescueRatio = 0.05f;
        public const float SplashRatio = 0.3f;
        public const int DailyDoubleGoldAds = 3;
        /// <summary>每手搓牌技能基础次数。</summary>
        public const int SkillRubUses = 3;
        /// <summary>每手透视技能基础次数。</summary>
        public const int SkillXRayUses = 1;
        /// <summary>每手替换技能基础次数。</summary>
        public const int SkillReplaceUses = 1;
        /// <summary>座位手牌数组容量。玩家和敌人都发 5 张。</summary>
        public const int MaxCardsPerSeat = 5;
        /// <summary>玩家每手发牌张数。</summary>
        public const int PlayerCardsDealt = 5;
        /// <summary>敌人每手发牌张数。</summary>
        public const int EnemyCardsDealt = 5;
        /// <summary>开牌使用的张数。</summary>
        public const int OpenHandSize = 3;

        public static int CardsDealt(bool player)
        {
            return player ? PlayerCardsDealt : EnemyCardsDealt;
        }

        /// <summary>普通关 3 名敌人，BOSS 关只留 1 名。</summary>
        public static int EnemyCountForStage(int stage)
        {
            if (IsBossStage(stage))
            {
                return 1;
            }

            return 3;
        }

        /// <summary>第 10 / 20 / 30 … 关为 BOSS。</summary>
        public static bool IsBossStage(int stage) => ((stage - 1) % 10) + 1 == 10;

        /// <summary>关卡内第 N 个下注回合的基础注：10、20、30…</summary>
        public static int BaseBetForRound(int stageBetRound)
        {
            if (stageBetRound <= 1)
            {
                return BaseBetStart;
            }

            return BaseBetStart + (stageBetRound - 1) * BaseBetStep;
        }

        /// <summary>敌人血量 = 2200 + 关卡×700；BOSS 再 ×2.4。</summary>
        public static int EnemyHp(int stage, bool boss)
        {
            var hp = 2200 + stage * 700;
            return boss ? (int)(hp * 2.4f) : hp;
        }

        /// <summary>血量折成金币：先按开局筹码/血量比例换筹码，再分段递减汇率。</summary>
        public static int ConvertHpToGold(int hp)
        {
            if (hp <= 0)
            {
                return 0;
            }

            var equivalent = (int)Math.Floor(hp * (PlayerStartChips / (float)PlayerStartHp));
            return ConvertStackToGold(equivalent);
        }

        private static int ConvertStackToGold(int stack)
        {
            if (stack <= 0)
            {
                return 0;
            }

            var gold = 0;
            var remain = stack;
            gold += Take(ref remain, 500, 0.05f);
            gold += Take(ref remain, 500, 0.10f);
            gold += Take(ref remain, 1000, 0.15f);
            if (remain > 0)
            {
                gold += (int)Math.Floor(remain * 0.20f);
            }

            return gold;
        }

        public static string AffixName(BossAffix affix)
        {
            switch (affix)
            {
                case BossAffix.BanRubHeart: return "禁搓·红桃";
                case BossAffix.BanRubSpade: return "禁搓·黑桃";
                case BossAffix.BanRubDiamond: return "禁搓·方片";
                case BossAffix.BanRubClub: return "禁搓·梅花";
                case BossAffix.BanRubFace: return "禁搓·人头";
                case BossAffix.BanScoreHeart: return "禁用·红桃";
                case BossAffix.BanScoreSpade: return "禁用·黑桃";
                case BossAffix.BanScoreDiamond: return "禁用·方片";
                case BossAffix.BanScoreClub: return "禁用·梅花";
                case BossAffix.BanScoreFace: return "禁用·人头";
                case BossAffix.Flint: return "燧石";
                case BossAffix.Edge: return "锋芒";
                case BossAffix.XRay: return "透视眼";
                case BossAffix.AntiRaise: return "反加注";
                case BossAffix.Stingy: return "铁公鸡";
                default: return "无";
            }
        }

        public static string AffixDesc(BossAffix affix)
        {
            switch (affix)
            {
                case BossAffix.BanRubHeart: return "搓牌无法出现红桃";
                case BossAffix.BanRubSpade: return "搓牌无法出现黑桃";
                case BossAffix.BanRubDiamond: return "搓牌无法出现方片";
                case BossAffix.BanRubClub: return "搓牌无法出现梅花";
                case BossAffix.BanRubFace: return "搓牌无法出现人头牌";
                case BossAffix.BanScoreHeart: return "比牌时红桃不计入牌型";
                case BossAffix.BanScoreSpade: return "比牌时黑桃不计入牌型";
                case BossAffix.BanScoreDiamond: return "比牌时方片不计入牌型";
                case BossAffix.BanScoreClub: return "比牌时梅花不计入牌型";
                case BossAffix.BanScoreFace: return "比牌时人头牌不计入牌型";
                case BossAffix.Flint: return "结算时牌型倍率和血量收益减半";
                case BossAffix.Edge: return "本局随机禁用一件道具/遗物";
                case BossAffix.XRay: return "比牌前偷看你的最终牌型";
                case BossAffix.AntiRaise: return "你的下注消耗变为 1.5 倍";
                case BossAffix.Stingy: return "破产救助比例降为 5%";
                default: return string.Empty;
            }
        }

        private static int Take(ref int remain, int slice, float rate)
        {
            var used = Math.Min(remain, slice);
            remain -= used;
            return (int)Math.Floor(used * rate);
        }
    }

    /// <summary>桌上一个座位：玩家或敌人。血量按座位独立，进关时换算成该座位的勇气值（筹码）。</summary>
    public sealed class SeatState
    {
        public int Id;
        public string Name;
        public bool IsPlayer;
        public bool IsBoss;
        /// <summary>本关是否上场。敌人座位固定 3 个，未上场的 Hp=0。</summary>
        public bool ActiveInStage;
        /// <summary>当前血量。攻击结算才扣除；下注不扣血。</summary>
        public int Hp;
        public int MaxHp;
        /// <summary>攻击力。玩家读 HeroConfig.HeroDamage，怪物读 MonsterConfig.MonsterDamage。</summary>
        public int Attack;
        /// <summary>勇气值（筹码）。由本座位血量换算，下注从这里扣。</summary>
        public int Courage;
        /// <summary>本回合已下注、尚未结算的勇气值。</summary>
        public int CourageStake;
        /// <summary>本街已承诺的下注档位（未看牌按单倍计）。</summary>
        public int StreetUnits;
        public int StreetPaid;
        public int TotalBet;
        public int RoundStartChips;
        public bool Folded;
        /// <summary>玩家看牌后下注翻倍；AI 始终按已看牌决策。</summary>
        public bool Looked;
        public bool ShowCards;
        public bool Alive => ActiveInStage && Hp > 0;
        public Card[] Hand = new Card[GameBalance.MaxCardsPerSeat];
        /// <summary>开牌用牌。玩家点选；敌人结算时锁定最大牌型组合。最多 <see cref="GameBalance.OpenHandSize"/> 张。</summary>
        public readonly bool[] CardSelected = new bool[GameBalance.MaxCardsPerSeat];
        public string Status = string.Empty;
        public string Banner = string.Empty;
        /// <summary>透视技能看到的牌型，本手有效。</summary>
        public string PeekedType = string.Empty;
        /// <summary>敌人人格。玩家为 null。BOSS 关会覆盖成 Expert。</summary>
        public AiProfile Profile;

        public int CountSelectedCards()
        {
            var n = 0;
            var limit = Math.Min(CardSelected.Length, Hand != null ? Hand.Length : 0);
            for (var i = 0; i < limit; i++)
            {
                if (CardSelected[i])
                {
                    n++;
                }
            }

            return n;
        }

        public bool IsCardSelected(int index)
        {
            return index >= 0 && index < CardSelected.Length && CardSelected[index];
        }

        public void ClearCardSelected()
        {
            for (var i = 0; i < CardSelected.Length; i++)
            {
                CardSelected[i] = false;
            }
        }
    }

    /// <summary>整次闯关进度：金币、关卡、遗物、广告次数、BOSS 词缀。</summary>
    public sealed class RunState
    {
        public int Gold;
        public int Stage = 1;
        public int ConsecutiveLosses;
        /// <summary>连输 2 局后触发，本局最大下注限制为当前勇气值 50%。</summary>
        public bool Tilted;
        public int RubsLeft;
        public int ExtraRubCharges;
        public bool SplashThisRound;
        public bool MagnifierThisRound;
        public bool LoanTicket;
        public int PeekGoodCharges;
        public int ChaKanGoodCharges;
        public int TiHuanGoodCharges;
        /// <summary>商店提供的每关额外搓牌次数，整次闯关保留。</summary>
        public int BonusRubCharges;
        /// <summary>商店提供的每关额外透视次数，整次闯关保留。</summary>
        public int BonusXRayCharges;
        /// <summary>商店提供的每关额外替换次数，整次闯关保留。</summary>
        public int BonusReplaceCharges;
        /// <summary>透视揭示标记，下标 = seatId * MaxCardsPerSeat + cardIndex。</summary>
        public readonly bool[] SpyReveal = new bool[20];
        public bool PeekSuitUsed;
        public int PeekSuitIndex = -1;
        public Suit? PeekedSuit;
        public readonly bool[] RubbedReveal = new bool[GameBalance.MaxCardsPerSeat];
        public string LastRubMessage = string.Empty;
        public int AdsLoanThisStage;
        public int AdsReviveThisStage;
        public int AdsExtraRubThisStage;
        public int AdsDoubleGoldToday;
        public bool DoubleGoldThisStage;
        public BossAffix Affix;
        /// <summary>锋芒禁用的 RelicConfig Id，0 表示未禁用。</summary>
        public int DisabledRelicConfigId;
        public ConsumableId? DisabledConsumable;
        /// <summary>已购 RelicConfig Id。商店商品唯一持有列表。</summary>
        public readonly List<int> RelicConfigIds = new List<int>();
        /// <summary>当前商店货架上的 RelicConfig Id。</summary>
        public readonly List<int> ShopOfferIds = new List<int>();
        /// <summary>本店已付费刷新次数。下次费用 = ShopRefreshFirst + 次数 × ShopRefreshAfter。</summary>
        public int ShopRefreshCount;
        /// <summary>当前关卡 <see cref="App.Level.LevelSnapshot.Id"/>。</summary>
        public int LevelId;
        /// <summary>当前上场英雄 <see cref="App.Config.HeroConfig.Id"/>。</summary>
        public int HeroId;
        /// <summary>本关是否含 BOSS，来自关卡配置。</summary>
        public bool HasBoss;
        public readonly List<string> Log = new List<string>();
    }
}
