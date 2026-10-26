using CardShare.Contracts.Config;

namespace App.Guide
{
    /// <summary>配置表 TargetId / WaitHandler 与代码登记用的约定字符串。</summary>
    public static class GuideTargetIds
    {
        public const string CompareBtn = "GameUI.CompareBtn";
        public const string PeekGood = "GameUI.PeekGood";
        /// <summary>左侧三技能（搓牌/替换/透视）并集挖孔。</summary>
        public const string SkillButtons = "GameUI.SkillButtons";
        public const string NextRoundBtn = "GameUI.NextRoundBtn";
        public const string YiwuBtn = "GameUI.YiwuBtn";
        /// <summary>局内右侧羁绊图标。</summary>
        public const string Bonds = "GameUI.Bonds";
        public const string PlayerHand = "Board.PlayerHand";
        public const string TalentBtn = "Navigation.TalentBtn";
        public const string TalentBuyBtn = "TalentPopup.BuyBtn";
        public const string ShopSellOffers = "Shop.SellOffers";
        public const string ShopBoughtMine = "Shop.BoughtMine";
        public const string ShopSellBtn = "Shop.SellBtn";

        public static string PlayerCard(int index) => $"Board.PlayerCard.{index}";
    }

    public static class GuideGroupIds
    {
        public const int FirstBattle = 1;
        public const int FirstTalentDraw = 2;
        public const int FirstShop = 3;
        public const int FirstSkill = 4;
        public const int FirstBond = 5;
    }

    public static class GuideWaitIds
    {
        public const string DealFinished = "DealFinished";
        public const string SelectCards = "SelectCards";
        public const string SelectGuideCards = "SelectGuideCards";
        public const string GamePhase = "GamePhase";
        public const string RubCard = "RubCard";
        public const string SelectHandType = "SelectHandType";
        public const string PeekGoodTipShown = "PeekGoodTipShown";
        public const string PeekGoodTipClosed = "PeekGoodTipClosed";
        public const string TalentPopupOpen = "TalentPopupOpen";
        public const string TalentDrawn = "TalentDrawn";
        public const string ShopOfferClicked = "ShopOfferClicked";
        public const string ShopBought = "ShopBought";
        public const string ShopMineClicked = "ShopMineClicked";
        public const string ShopBuyAbandoned = "ShopBuyAbandoned";
        public const string ShopMineUnavailable = "ShopMineUnavailable";
        public const string RemainListClosed = "RemainListClosed";
        public const string Delay = "Delay";
    }

    public static class GuideSignals
    {
        public static int LastDealSerial { get; private set; }

        /// <summary>搓牌技能详情 tip 是否正在显示（供 WaitHandler 判断）。</summary>
        public static bool PeekGoodTipVisible { get; private set; }

        /// <summary>天赋弹窗是否已打开。</summary>
        public static bool TalentPopupOpened { get; private set; }

        /// <summary>本引导周期内是否已成功抽过天赋。</summary>
        public static bool TalentDrawn { get; private set; }

        /// <summary>本引导周期内是否已点击商店货架圣物。</summary>
        public static bool ShopOfferClicked { get; private set; }

        /// <summary>本引导周期内是否已成功购买圣物。</summary>
        public static bool ShopBought { get; private set; }

        /// <summary>本引导周期内是否已点击已购栏圣物。</summary>
        public static bool ShopMineClicked { get; private set; }

        /// <summary>最近一次引导内购买成功的圣物 Id（登记已购挖孔用）。</summary>
        public static int LastBoughtShopRelicId { get; private set; }

        /// <summary>买过可凑羁绊的圣物后，等离开商店回局内再开 FirstBond。</summary>
        public static bool BondGuidePending { get; private set; }

        public static event System.Action<string> Raised;

        public static void Raise(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return;
            }

            if (id == GuideWaitIds.DealFinished)
            {
                LastDealSerial = CurrentDealSerial;
            }

            Raised?.Invoke(id);
        }

        public static int CurrentDealSerial { get; set; }

        public static void NotifyDealFinished(int dealSerial)
        {
            CurrentDealSerial = dealSerial;
            LastDealSerial = dealSerial;
            Raised?.Invoke(GuideWaitIds.DealFinished);
        }

        public static void NotifyPeekGoodTipShown()
        {
            PeekGoodTipVisible = true;
            Raised?.Invoke(GuideWaitIds.PeekGoodTipShown);
        }

        public static void NotifyPeekGoodTipClosed()
        {
            PeekGoodTipVisible = false;
            Raised?.Invoke(GuideWaitIds.PeekGoodTipClosed);
        }

        public static void NotifyTalentPopupOpen()
        {
            TalentPopupOpened = true;
            Raised?.Invoke(GuideWaitIds.TalentPopupOpen);
        }

        public static void NotifyTalentPopupClosed()
        {
            TalentPopupOpened = false;
        }

        public static void NotifyTalentDrawn()
        {
            TalentDrawn = true;
            Raised?.Invoke(GuideWaitIds.TalentDrawn);
        }

        public static void ResetTalentDrawn()
        {
            TalentDrawn = false;
        }

        public static void NotifyShopOfferClicked()
        {
            ShopOfferClicked = true;
            Raised?.Invoke(GuideWaitIds.ShopOfferClicked);
        }

        public static void ResetShopOfferClicked()
        {
            ShopOfferClicked = false;
        }

        public static void NotifyShopBought(int relicId)
        {
            ShopBought = true;
            if (relicId > 0)
            {
                LastBoughtShopRelicId = relicId;
                var relic = RelicConfig.Get(relicId);
                if (relic?.BindingID != null && relic.BindingID.Length > 0)
                {
                    BondGuidePending = true;
                }
            }

            Raised?.Invoke(GuideWaitIds.ShopBought);
        }

        public static void ClearBondGuidePending()
        {
            BondGuidePending = false;
        }

        public static void ResetShopBought()
        {
            ShopBought = false;
            LastBoughtShopRelicId = 0;
        }

        public static void NotifyShopMineClicked()
        {
            ShopMineClicked = true;
            Raised?.Invoke(GuideWaitIds.ShopMineClicked);
        }

        public static void ResetShopMineClicked()
        {
            ShopMineClicked = false;
        }

        public static void NotifyShopBuyAbandoned()
        {
            Raised?.Invoke(GuideWaitIds.ShopBuyAbandoned);
        }

        public static void NotifyShopMineUnavailable()
        {
            Raised?.Invoke(GuideWaitIds.ShopMineUnavailable);
        }

        public static void NotifyRemainListClosed()
        {
            RemainListOpen = false;
            Raised?.Invoke(GuideWaitIds.RemainListClosed);
        }

        public static void NotifyRemainListOpened()
        {
            RemainListOpen = true;
        }

        /// <summary>圣物列表面板是否正在打开。</summary>
        public static bool RemainListOpen { get; private set; }

        /// <summary>引导组结束（完成/跳过/中止）时发出，用于清 UI 门控与 tip。</summary>
        public static void NotifyGuideEnded()
        {
            PeekGoodTipVisible = false;
            TalentDrawn = false;
            ShopOfferClicked = false;
            ShopBought = false;
            ShopMineClicked = false;
            LastBoughtShopRelicId = 0;
            Raised?.Invoke("GuideEnded");
        }
    }
}
