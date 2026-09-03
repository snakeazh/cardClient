using System.Collections.Generic;
using Framework.Save;

namespace App.Score
{
    /// <summary>
    /// 章节积分。本轮积分 = 本手打出的攻击数值（1:1，读 GameConst.ChipsForPoints），不按怪物实际扣血。
    /// 局外货币 = 总积分 / GameConst.ExchangePointsForGoldCoins（向下取整），不清空积分。
    /// 局内金币改读 LevelConfig.GetGold，对局不再调用 CollectGoldDelta。
    /// GameSession 在逐个比牌结束后 AwardRoundScore。
    /// </summary>
    public interface IScoreService : ISaveFlushable
    {
        bool IsDirty { get; }

        ScoreSnapshot Current { get; }

        /// <summary>本关每回合积分，BeginStage / BeginChapter 时清空。</summary>
        IReadOnlyList<int> StageRoundScores { get; }

        /// <summary>本关每回合击杀数，与 <see cref="StageRoundScores"/> 一一对应，清空时机相同。</summary>
        IReadOnlyList<int> StageRoundKills { get; }

        /// <summary>
        /// floor(总积分 / GameConst.ExchangePointsForGoldCoins)。预览可兑局外货币，不改积分。
        /// 对局不再用此发局内金币。
        /// </summary>
        int CollectableGold { get; }

        /// <summary>本章节已按总积分发放过的金币。对局不再累加，仅存档兼容。</summary>
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
        /// 本轮积分：本手打出的攻击数值 / GameConst.ChipsForPoints（向下取整，当前 1:1）。
        /// 换算后 &lt;= 0 则本轮为 0，不向上累加。不按怪物剩余血量截断。
        /// </summary>
        void AwardRoundScore(int chipsWon);

        /// <summary>记一次击杀：本回合积分已入账则归最后一行，否则挂下一行（由 AwardRoundScore 补齐积分行）。</summary>
        void TrackStageKill();

        /// <summary>
        /// 旧局内金币差额发放（CollectableGold - GrantedGold）。积分不清空。
        /// 对局已改读 LevelConfig.GetGold，不再调用；保留存档兼容。
        /// </summary>
        int CollectGoldDelta();

        void Load();
    }
}
