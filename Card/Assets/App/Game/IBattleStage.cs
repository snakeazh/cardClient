using System;

namespace App.Game
{
    /// <summary>
    /// 演出舞台：演出命令（App.UI.Game.Director）操作的最小接口。
    /// 命令只依赖这个窄口，不碰 <see cref="GameSession"/> 本体；PVE 迁命令模式时复用同一接缝。
    /// Begin* 拨序号触发现有动画，完成经事件回报；返回 false 表示条件不满足、命令应立即完成。
    /// </summary>
    public interface IBattleStage
    {
        event Action PvpDealFinished;

        event Action PvpRevealFinished;

        event Action PvpCombatFinished;

        void BeginPvpDeal();

        bool BeginPvpReveal(bool playerWon);

        bool BeginPvpAttack(bool incoming, int damage, HandType winType, string winLabel, string loseLabel);

        /// <summary>PVP 圣光燃烧演出：incoming=对方烧我。伤害为服务端权威值。</summary>
        bool BeginPvpHolyLight(bool incoming, int damage, int casterHeroId);

        /// <summary>PVP 开局自伤（萨满等）：只飘字扣自己，无施法特效。</summary>
        bool BeginPvpSelfChip(int damage);

        void SetPvpAttackDisplay(bool playerSide, int value);
    }
}
