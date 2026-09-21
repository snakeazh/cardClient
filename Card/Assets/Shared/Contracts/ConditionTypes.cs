#nullable enable

namespace CardShare.Contracts
{
    /// <summary>Matches client ContidionType values from EnumConfig.</summary>
    public static class ConditionTypes
    {
        public const int KillMonster = 1;
        public const int ShuffleCard = 2;
        public const int RefreshStore = 3;
        public const int Straight = 4;
        public const int TwoThreeFive = 5;
        public const int ShuffleCardAndVictory = 6;
        public const int Seven = 7;
        public const int Flush = 8;
        public const int ClearDifficulty = 9;
        public const int AccumulateGold = 10;
        public const int SingleDamage = 11;
        public const int Couplet = 12;
        public const int Failure = 13;
        public const int Defeat = 14;
        public const int LuxuryGoods = 15;
        public const int Angel = 16;
        public const int DeathNum = 17;
        public const int Perspective = 18;
        public const int OneDamage = 19;
        public const int NumberOfCoinsOwned = 20;
        public const int CriticalNum = 21;
        public const int ThreeCardAttack = 22;

        public static bool UsesMax(int type)
        {
            return type == ClearDifficulty
                   || type == NumberOfCoinsOwned
                   || type == OneDamage
                   || type == SingleDamage;
        }
    }
}
