namespace App.Guide
{
    /// <summary>配置表 TargetId / WaitHandler 与代码登记用的约定字符串。</summary>
    public static class GuideTargetIds
    {
        public const string CompareBtn = "GameUI.CompareBtn";
        public const string PeekGood = "GameUI.PeekGood";
        public const string NextRoundBtn = "GameUI.NextRoundBtn";
        public const string PlayerHand = "Board.PlayerHand";
        public const string TalentBtn = "Navigation.TalentBtn";
        public const string TalentBuyBtn = "TalentPopup.BuyBtn";

        public static string PlayerCard(int index) => $"Board.PlayerCard.{index}";
    }

    public static class GuideGroupIds
    {
        public const int FirstBattle = 1;
        public const int FirstTalentDraw = 2;
    }

    public static class GuideWaitIds
    {
        public const string DealFinished = "DealFinished";
        public const string SelectCards = "SelectCards";
        public const string GamePhase = "GamePhase";
        public const string RubCard = "RubCard";
        public const string SelectHandType = "SelectHandType";
        public const string PeekGoodTipShown = "PeekGoodTipShown";
        public const string PeekGoodTipClosed = "PeekGoodTipClosed";
        public const string TalentPopupOpen = "TalentPopupOpen";
        public const string TalentDrawn = "TalentDrawn";
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

        /// <summary>引导组结束（完成/跳过/中止）时发出，用于清 UI 门控与 tip。</summary>
        public static void NotifyGuideEnded()
        {
            PeekGoodTipVisible = false;
            TalentDrawn = false;
            Raised?.Invoke("GuideEnded");
        }
    }
}
