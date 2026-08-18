namespace App.Score
{
    /// <summary>
    /// 按座位管理血量。每个座位独立满血/当前血。
    /// 下注不扣血：仅攻击结算扣血。不落盘。
    /// </summary>
    public interface IHpService
    {
        /// <summary>座位当前血量。未登记则为 0。</summary>
        int GetHp(int seatId);

        /// <summary>座位满血。未登记则为 0。</summary>
        int GetMaxHp(int seatId);

        /// <summary>Hp &lt;= 0 视为阵亡。</summary>
        bool IsDead(int seatId);

        /// <summary>
        /// 进关：登记该座位满血，并设当前血量。
        /// hp &lt; 0 时当前血量等于 maxHp。
        /// </summary>
        void BeginStage(int seatId, int maxHp, int hp = -1);

        /// <summary>回血。amount &lt;= 0 忽略。不封顶。</summary>
        void Heal(int seatId, int amount);

        /// <summary>扣血。amount &lt;= 0 忽略。最低扣到 0。</summary>
        void Damage(int seatId, int amount);

        /// <summary>复活：当前血量回到该座位满血。不改勇气值。</summary>
        void Revive(int seatId);
    }
}
