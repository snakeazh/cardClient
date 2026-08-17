namespace App.Score
{
    /// <summary>
    /// 玩家血量。只管玩家，敌人 HP 仍在 SeatState。
    /// 下注不扣血：仅回合失败扣血，成功不扣。不落盘。尚未接入 GameSession。
    /// </summary>
    public interface IHpService
    {
        /// <summary>当前血量。进关时用来换算勇气值；回合失败才下降。</summary>
        int Hp { get; }

        /// <summary>本关满血基准，默认 <see cref="ScoreBalance.PlayerStartHp"/>（4000）。</summary>
        int MaxHp { get; }

        /// <summary>Hp &lt;= 0 视为阵亡，关卡结算为失败。</summary>
        bool IsDead { get; }

        /// <summary>进关 / 下一关：Hp = MaxHp。随后用当前 Hp 换算本关勇气值。</summary>
        void BeginStage();

        /// <summary>回血（借贷 / 救助等）。amount &lt;= 0 忽略。不封顶。回合胜利吃池不走这里。</summary>
        void Heal(int amount);

        /// <summary>回合失败扣血。amount &lt;= 0 忽略。最低扣到 0。成功不调用。</summary>
        void Damage(int amount);

        /// <summary>广告复活：Hp = MaxHp。不影响积分与勇气值。</summary>
        void Revive();
    }
}
