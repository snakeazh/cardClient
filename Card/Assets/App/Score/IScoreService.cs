using Framework.Save;

namespace App.Score
{
    /// <summary>
    /// 章节积分。本轮积分 = 本手对怪造成的总伤害（1:1，读 GameConst.ChipsForPoints）。
    /// 金币 = 总积分 / GameConst.ExchangePointsForGoldCoins（向下取整），不清空积分。
    /// GameSession 在逐个比牌结束后 AwardRoundScore，通关时 CollectGoldDelta。
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
        /// 本轮积分：本手对怪伤害 / GameConst.ChipsForPoints（向下取整，当前 1:1）。
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
