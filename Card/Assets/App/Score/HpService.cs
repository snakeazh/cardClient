namespace App.Score
{
    /// <summary>
    /// In-memory player HP. Not persisted. Betting does not change HP.
    /// </summary>
    public sealed class HpService : IHpService
    {
        public int Hp { get; private set; } = ScoreBalance.PlayerStartHp;

        public int MaxHp { get; private set; } = ScoreBalance.PlayerStartHp;

        public bool IsDead => Hp <= 0;

        public void BeginStage()
        {
            Hp = MaxHp;
        }

        public void Heal(int amount)
        {
            if (amount <= 0)
            {
                return;
            }

            Hp += amount;
        }

        public void Damage(int amount)
        {
            if (amount <= 0)
            {
                return;
            }

            Hp -= amount;
            if (Hp < 0)
            {
                Hp = 0;
            }
        }

        public void Revive()
        {
            Hp = MaxHp;
        }
    }
}
