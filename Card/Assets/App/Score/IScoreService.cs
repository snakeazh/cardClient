using Framework.Save;

namespace App.Score
{
    /// <summary>
    /// 章节积分。本轮积分 = 成功获得的筹码 / GameConst.ChipsForPoints（向下取整）。
    /// 金币 = 总积分 / GameConst.ExchangePointsForGoldCoins（向下取整），不清空积分。
    /// 尚未接入 GameSession。
    /// </summary>
    public interface IScoreService : ISaveFlushable
    {
        bool IsDirty { get; }

        ScoreSnapshot Current { get; }

        /// <summary>floor(总积分 / GameConst.ExchangePointsForGoldCoins)。预览可换金币，不改积分。</summary>
        int CollectableGold { get; }

        /// <summary>本章节已按总积分发放过的金币。</summary>
        int GrantedGold { get; }

        /// <summary>
        /// 失败未复活 / 新章节：Total、Stage、Round、已发放金币全清。
        /// 下次从当前章节第一关重开（选关由关卡模块负责）。
        /// </summary>
        void BeginChapter();

        /// <summary>Clears Stage / Round. Total is kept.</summary>
        void BeginStage();

        /// <summary>Clears Round. Stage / Total are kept.</summary>
        void BeginRound();

        /// <summary>
        /// 本轮积分：成功获得的筹码 / GameConst.ChipsForPoints（向下取整）。
        /// 换算后 &lt;= 0 则本轮为 0，不向上累加。
        /// </summary>
        void AwardRoundScore(int chipsWon);

        /// <summary>
        /// 关卡胜利结算：总积分 / GameConst.ExchangePointsForGoldCoins 向下取整换金币，
        /// 发放差额（CollectableGold - GrantedGold）。积分不清空。
        /// </summary>
        int CollectGoldDelta();

        void Load();
    }
}
