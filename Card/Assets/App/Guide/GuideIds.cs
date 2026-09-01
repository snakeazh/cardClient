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
    }

    public static class GuideSignals
    {
        public static int LastDealSerial { get; private set; }

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
    }
}
