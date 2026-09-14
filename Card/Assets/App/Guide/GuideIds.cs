namespace App.Guide
{
    /// <summary>配置表 TargetId / WaitHandler 与代码登记用的约定字符串。</summary>
    public static class GuideTargetIds
    {
        public const string CompareBtn = "GameUI.CompareBtn";
        public const string PeekGood = "GameUI.PeekGood";
        public const string NextRoundBtn = "GameUI.NextRoundBtn";
        public const string PlayerHand = "Board.PlayerHand";

        public static string PlayerCard(int index) => $"Board.PlayerCard.{index}";
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
    }

    public static class GuideSignals
    {
        public static int LastDealSerial { get; private set; }

        /// <summary>搓牌技能详情 tip 是否正在显示（供 WaitHandler 判断）。</summary>
        public static bool PeekGoodTipVisible { get; private set; }

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

        /// <summary>引导组结束（完成/跳过/中止）时发出，用于清 UI 门控与 tip。</summary>
        public static void NotifyGuideEnded()
        {
            PeekGoodTipVisible = false;
            Raised?.Invoke("GuideEnded");
        }
    }
}
