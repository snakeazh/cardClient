using System;
using System.Collections.Generic;

namespace App.Game
{
    public enum GamePhase
    {
        Idle = 0,
        WaitingRub = 1,
        Betting = 2,
        Showdown = 3,
        RoundSettle = 4,
        Shop = 5,
        StageFail = 6,
        RunComplete = 7
    }

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
        ScareMask = 8,
        MagnetGloves = 9
    }

    public enum ConsumableId
    {
        SplashSlash = 0,
        Magnifier = 1,
        LoanTicket = 2
    }

    public enum RelicCategory
    {
        Combat = 0,
        Rub = 1,
        Bet = 2,
        Special = 3
    }

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
        Flint = 11,
        Edge = 12,
        XRay = 13,
        AntiRaise = 14,
        Stingy = 15
    }

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

    public static class GameBalance
    {
        public const int PlayerStartChips = 800;
        public const int MinBet = 50;
        public const int MaxRelics = 4;
        public const float RescueRatio = 0.15f;
        public const float StingyRescueRatio = 0.05f;
        public const float SplashRatio = 0.3f;
        public const float MagnetKeepSuitChance = 0.3f;
        public const int DailyDoubleGoldAds = 3;

        public static int EnemyCountForStage(int stage)
        {
            var local = ((stage - 1) % 10) + 1;
            if (local <= 3)
            {
                return 1;
            }

            if (local <= 6)
            {
                return 2;
            }

            if (local <= 9)
            {
                return 3;
            }

            return 1;
        }

        public static bool IsBossStage(int stage) => ((stage - 1) % 10) + 1 == 10;

        public static int EnemyHp(int stage, bool boss)
        {
            var hp = 2200 + stage * 700;
            return boss ? (int)(hp * 2.4f) : hp;
        }

        public static int EnemyChips(int stage, bool boss)
        {
            var chips = 320 + stage * 40;
            return boss ? (int)(chips * 1.6f) : chips;
        }

        public static int ConvertChipsToGold(int chips)
        {
            if (chips <= 0)
            {
                return 0;
            }

            var gold = 0;
            var remain = chips;
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
                case BossAffix.Flint: return "结算时牌型倍率和筹码减半";
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
            new ShopItemDef { Id = "loan", Name = "借贷券", Effect = "筹码归零时自动获得最低下注额", Price = 30, ConsumableId = ConsumableId.LoanTicket },
            new ShopItemDef { Id = "crude", Name = "粗制长剑", Effect = "结算倍率 +4", Price = 120, Relic = true, RelicId = RelicId.CrudeSword, Category = RelicCategory.Combat },
            new ShopItemDef { Id = "neck", Name = "贪婪项链", Effect = "方片结算倍率 +3", Price = 90, Relic = true, RelicId = RelicId.GreedyNecklace, Category = RelicCategory.Combat },
            new ShopItemDef { Id = "bracelet", Name = "贪婪手镯", Effect = "红桃结算倍率 +3", Price = 90, Relic = true, RelicId = RelicId.GreedyBracelet, Category = RelicCategory.Combat },
            new ShopItemDef { Id = "ear", Name = "贪婪耳环", Effect = "黑桃结算倍率 +3", Price = 90, Relic = true, RelicId = RelicId.GreedyEarring, Category = RelicCategory.Combat },
            new ShopItemDef { Id = "ring", Name = "贪婪戒指", Effect = "梅花结算倍率 +3", Price = 90, Relic = true, RelicId = RelicId.GreedyRing, Category = RelicCategory.Combat },
            new ShopItemDef { Id = "wood", Name = "木制长剑", Effect = "对子结算倍率 +8", Price = 140, Relic = true, RelicId = RelicId.WoodenSword, Category = RelicCategory.Combat },
            new ShopItemDef { Id = "iron", Name = "铁质长剑", Effect = "顺子结算倍率 +8", Price = 140, Relic = true, RelicId = RelicId.IronSword, Category = RelicCategory.Combat },
            new ShopItemDef { Id = "jade", Name = "翡翠长剑", Effect = "金花结算倍率 +8", Price = 160, Relic = true, RelicId = RelicId.JadeSword, Category = RelicCategory.Combat },
            new ShopItemDef { Id = "mask", Name = "恐吓面具", Effect = "AI 跟注率降低 10%", Price = 180, Relic = true, RelicId = RelicId.ScareMask, Category = RelicCategory.Bet },
            new ShopItemDef { Id = "magnet", Name = "磁力手套", Effect = "搓牌保留原花色概率 +30%", Price = 150, Relic = true, RelicId = RelicId.MagnetGloves, Category = RelicCategory.Rub }
        };

        private static int Take(ref int remain, int slice, float rate)
        {
            var used = Math.Min(remain, slice);
            remain -= used;
            return (int)Math.Floor(used * rate);
        }
    }

    public sealed class SeatState
    {
        public int Id;
        public string Name;
        public bool IsPlayer;
        public bool IsBoss;
        public bool ActiveInStage;
        public int Hp;
        public int MaxHp;
        public int Chips;
        public int StreetUnits;
        public int TotalBet;
        public bool Folded;
        public bool Looked;
        public bool Alive => ActiveInStage && Hp > 0;
        public Card[] Hand = new Card[3];
        public string Status = string.Empty;
        public string Banner = string.Empty;
    }

    public sealed class RunState
    {
        public int Gold;
        public int Stage = 1;
        public int Chips = GameBalance.PlayerStartChips;
        public int ConsecutiveLosses;
        public bool Tilted;
        public int RubsLeft;
        public int ExtraRubCharges;
        public bool SplashThisRound;
        public bool MagnifierThisRound;
        public bool LoanTicket;
        public bool PeekSuitUsed;
        public int PeekSuitIndex = -1;
        public Suit? PeekedSuit;
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
