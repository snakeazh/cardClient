namespace App.Score
{
    /// <summary>
    /// 勇气值（筹码）。进关时由当前血量换算，下注扣勇气值，不扣血。
    /// 回合成功获得筹码再转积分；失败失去本回合已下注，并另扣血。不落盘。
    /// </summary>
    public interface ICourageService
    {
        /// <summary>当前可用勇气值。</summary>
        int Amount { get; }

        /// <summary>本回合已下注、尚未结算的勇气值。</summary>
        int Stake { get; }

        /// <summary>
        /// 进关：用当前血量换算勇气值（向下取整），已下注清零。
        /// hp &lt;= 0 则勇气值为 0。
        /// </summary>
        void BeginStage(int hp);

        /// <summary>新回合：已下注清零。可用勇气值保留。</summary>
        void BeginRound();

        /// <summary>
        /// 下注。从可用勇气值扣到本回合已下注。
        /// amount &lt;= 0 或余额不足返回 false。
        /// </summary>
        bool TryBet(int amount);

        /// <summary>
        /// 回合成功：把获得的筹码加入可用勇气值，已下注清零。
        /// chipsWon &lt;= 0 只清已下注。返回实际入账的筹码（用于转积分）。
        /// </summary>
        int Win(int chipsWon);

        /// <summary>
        /// 回合失败：失去本回合已下注（下注时已从可用额扣除），返回失去的数量并清零已下注。
        /// 扣血由 <see cref="IHpService.Damage"/> 另行处理。
        /// </summary>
        int Lose();
    }
}
