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
        /// <summary>下注街：跟/加/弃/开牌。</summary>
        Betting = 2,
        Showdown = 3,
        RoundSettle = 4,
        Shop = 5,
        StageFail = 6,
        RunComplete = 7,
        /// <summary>玩家赢牌后点选敌人造成伤害。</summary>
        WaitingAttack = 8,
        /// <summary>发牌后选择看牌或闷注。</summary>
        WaitingLookChoice = 9
    }

    /// <summary>遗物。战斗类改结算倍率；恐吓面具压 AI；磁力手套改搓牌。</summary>
    public enum RelicId
    {
        CrudeSword = 0,
        GreedyNecklace = 1,
        GreedyBracelet = 2,
        GreedyEarring = 3,
        GreedyRing = 4,
        WoodenSword = 5,
        IronSword = 6,
        JadeSword = 7,
        /// <summary>AI 决策时胜率下调、诈唬减少。</summary>
        ScareMask = 8,
        MagnetGloves = 9
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

    public enum RelicCategory
    {
        Combat = 0,
        Rub = 1,
        Bet = 2,
        Special = 3
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

    /// <summary>商店条目。Relic=true 为永久遗物，否则为本局消耗品。</summary>
    public sealed class ShopItemDef
    {
        public string Id;
        public string Name;
        public string Effect;
        public int Price;
        public bool Relic;
        public RelicId RelicId;
        public ConsumableId ConsumableId;
        public RelicCategory Category;
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
        public const int MaxRelics = 4;
        public const float RescueRatio = 0.15f;
        public const float StingyRescueRatio = 0.05f;
        public const float SplashRatio = 0.3f;
        public const float MagnetKeepSuitChance = 0.3f;
        public const int DailyDoubleGoldAds = 3;
        /// <summary>每关搓牌技能基础次数。</summary>
        public const int SkillRubUses = 3;
        /// <summary>每关透视技能基础次数。</summary>
        public const int SkillXRayUses = 1;
        /// <summary>每关替换技能基础次数。</summary>
        public const int SkillReplaceUses = 1;

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

        public static RelicCategory CategoryOf(RelicId id)
        {
            switch (id)
            {
                case RelicId.MagnetGloves: return RelicCategory.Rub;
                case RelicId.ScareMask: return RelicCategory.Bet;
                default: return RelicCategory.Combat;
            }
        }

        public static IReadOnlyList<ShopItemDef> Catalog { get; } = new[]
        {
            new ShopItemDef { Id = "splash", Name = "瞬劈斩", Effect = "本局胜利攻击溅射其他敌人 30%", Price = 80, ConsumableId = ConsumableId.SplashSlash },
            new ShopItemDef { Id = "magnifier", Name = "放大镜", Effect = "下注前偷看 1 张牌的花色", Price = 50, ConsumableId = ConsumableId.Magnifier },
            new ShopItemDef { Id = "loan", Name = "借贷券", Effect = "血量不足以继续下注时自动获得最低下注血量", Price = 30, ConsumableId = ConsumableId.LoanTicket },
            new ShopItemDef { Id = "crude", Name = "粗制长剑", Effect = "结算倍率 +4", Price = 120, Relic = true, RelicId = RelicId.CrudeSword, Category = RelicCategory.Combat },
            new ShopItemDef { Id = "neck", Name = "贪婪项链", Effect = "方片结算倍率 +3", Price = 90, Relic = true, RelicId = RelicId.GreedyNecklace, Category = RelicCategory.Combat },
            new ShopItemDef { Id = "bracelet", Name = "贪婪手镯", Effect = "红桃结算倍率 +3", Price = 90, Relic = true, RelicId = RelicId.GreedyBracelet, Category = RelicCategory.Combat },
            new ShopItemDef { Id = "ear", Name = "贪婪耳环", Effect = "黑桃结算倍率 +3", Price = 90, Relic = true, RelicId = RelicId.GreedyEarring, Category = RelicCategory.Combat },
            new ShopItemDef { Id = "ring", Name = "贪婪戒指", Effect = "梅花结算倍率 +3", Price = 90, Relic = true, RelicId = RelicId.GreedyRing, Category = RelicCategory.Combat },
            new ShopItemDef { Id = "wood", Name = "木制长剑", Effect = "对子结算倍率 +8", Price = 140, Relic = true, RelicId = RelicId.WoodenSword, Category = RelicCategory.Combat },
            new ShopItemDef { Id = "iron", Name = "铁质长剑", Effect = "顺子结算倍率 +8", Price = 140, Relic = true, RelicId = RelicId.IronSword, Category = RelicCategory.Combat },
            new ShopItemDef { Id = "jade", Name = "翡翠长剑", Effect = "金花结算倍率 +8", Price = 160, Relic = true, RelicId = RelicId.JadeSword, Category = RelicCategory.Combat },
            new ShopItemDef { Id = "mask", Name = "恐吓面具", Effect = "AI 跟注率降低 10%", Price = 180, Relic = true, RelicId = RelicId.ScareMask, Category = RelicCategory.Bet },
            new ShopItemDef { Id = "magnet", Name = "磁力手套", Effect = "搓牌保留原花色概率 +30%", Price = 150, Relic = true, RelicId = RelicId.MagnetGloves, Category = RelicCategory.Rub },
            new ShopItemDef { Id = "rubCharge", Name = "搓牌秘籍", Effect = "每关搓牌次数 +1", Price = 80, ConsumableId = ConsumableId.RubCharge },
            new ShopItemDef { Id = "xrayCharge", Name = "透视秘籍", Effect = "每关透视次数 +1", Price = 80, ConsumableId = ConsumableId.XRayCharge },
            new ShopItemDef { Id = "replaceCharge", Name = "替换秘籍", Effect = "每关替换次数 +1", Price = 80, ConsumableId = ConsumableId.ReplaceCharge }
        };

        private static int Take(ref int remain, int slice, float rate)
        {
            var used = Math.Min(remain, slice);
            remain -= used;
            return (int)Math.Floor(used * rate);
        }
    }

    /// <summary>桌上一个座位：玩家或敌人。血量同时充当筹码。</summary>
    public sealed class SeatState
    {
        public int Id;
        public string Name;
        public bool IsPlayer;
        public bool IsBoss;
        /// <summary>本关是否上场。敌人座位固定 3 个，未上场的 Hp=0。</summary>
        public bool ActiveInStage;
        public int Hp;
        public int MaxHp;
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
        public Card[] Hand = new Card[3];
        public string Status = string.Empty;
        public string Banner = string.Empty;
        /// <summary>透视技能看到的牌型，本手有效。</summary>
        public string PeekedType = string.Empty;
        /// <summary>敌人人格。玩家为 null。BOSS 关会覆盖成 Expert。</summary>
        public AiProfile Profile;
    }

    /// <summary>整次闯关进度：金币、关卡、遗物、广告次数、BOSS 词缀。</summary>
    public sealed class RunState
    {
        public int Gold;
        public int Stage = 1;
        public int ConsecutiveLosses;
        /// <summary>连输 2 局后触发，本局最大下注限制为当前血量 50%。</summary>
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
        /// <summary>透视揭示标记，下标 = seatId*3 + cardIndex。</summary>
        public readonly bool[] SpyReveal = new bool[12];
        public bool PeekSuitUsed;
        public int PeekSuitIndex = -1;
        public Suit? PeekedSuit;
        public readonly bool[] RubbedReveal = new bool[3];
        public string LastRubMessage = string.Empty;
        public int AdsLoanThisStage;
        public int AdsReviveThisStage;
        public int AdsExtraRubThisStage;
        public int AdsDoubleGoldToday;
        public bool DoubleGoldThisStage;
        public BossAffix Affix;
        public RelicId? DisabledRelic;
        public ConsumableId? DisabledConsumable;
        public readonly List<RelicId> Relics = new List<RelicId>();
        public readonly List<string> Log = new List<string>();
    }
}
