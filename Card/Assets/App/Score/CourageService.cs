namespace App.Score
{
    /// <summary>
    /// In-memory courage (chips). Recalculated from HP at stage start. Not persisted.
    /// </summary>
    public sealed class CourageService : ICourageService
    {
        public int Amount { get; private set; }

        public int Stake { get; private set; }

        public void BeginStage(int hp)
        {
            Amount = ScoreBalance.HpToCourage(hp);
            Stake = 0;
        }

        public void BeginRound()
        {
            Stake = 0;
        }

        public bool TryBet(int amount)
        {
            if (amount <= 0 || Amount < amount)
            {
                return false;
            }

            Amount -= amount;
            Stake += amount;
            return true;
        }

        public int Win(int chipsWon)
        {
            Stake = 0;
            if (chipsWon <= 0)
            {
                return 0;
            }

            Amount += chipsWon;
            return chipsWon;
        }

        public int Lose()
        {
            var lost = Stake;
            Stake = 0;
            return lost;
        }
    }
}
