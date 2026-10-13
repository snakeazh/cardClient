using System;
using App.Game;
using DG.Tweening;

namespace App.UI.Game.Director
{
    /// <summary>新一轮发牌：驱动 DealSerial，等发牌动画播完。</summary>
    public sealed class DealCommand : BattleCommand
    {
        public override string Name => "deal";

        public override void Execute(IBattleStage stage, Action onComplete)
        {
            stage.BeginPvpDeal();
            AwaitOnce(
                h => stage.PvpDealFinished += h,
                h => stage.PvpDealFinished -= h,
                onComplete);
        }
    }

    /// <summary>比牌翻牌：按胜方摆好座位，播逐座翻牌，等播完。</summary>
    public sealed class RevealCommand : BattleCommand
    {
        private readonly bool _playerWon;

        public RevealCommand(bool playerWon)
        {
            _playerWon = playerWon;
        }

        public override string Name => "reveal";

        public override void Execute(IBattleStage stage, Action onComplete)
        {
            if (!stage.BeginPvpReveal(_playerWon))
            {
                onComplete();
                return;
            }

            AwaitOnce(
                h => stage.PvpRevealFinished += h,
                h => stage.PvpRevealFinished -= h,
                onComplete);
        }
    }

    /// <summary>攻击撞击：播攻击演出（含伤害飘字、致死溶解），等播完。</summary>
    public sealed class AttackCommand : BattleCommand
    {
        private readonly bool _incoming;
        private readonly int _damage;
        private readonly HandType _winType;
        private readonly string _winLabel;
        private readonly string _loseLabel;

        public AttackCommand(bool incoming, int damage, HandType winType, string winLabel, string loseLabel)
        {
            _incoming = incoming;
            _damage = damage;
            _winType = winType;
            _winLabel = winLabel;
            _loseLabel = loseLabel;
        }

        public override string Name => "attack";

        public override void Execute(IBattleStage stage, Action onComplete)
        {
            if (!stage.BeginPvpAttack(_incoming, _damage, _winType, _winLabel, _loseLabel))
            {
                onComplete();
                return;
            }

            AwaitOnce(
                h => stage.PvpCombatFinished += h,
                h => stage.PvpCombatFinished -= h,
                onComplete);
        }
    }

    /// <summary>攻击力数值跳动：即时命令，改座位攻击并刷新（UI 自动播抖动）。</summary>
    public sealed class SetAttackCommand : BattleCommand
    {
        private readonly bool _playerSide;
        private readonly int _value;

        public SetAttackCommand(bool playerSide, int value)
        {
            _playerSide = playerSide;
            _value = value;
        }

        public override string Name => "set_attack";

        public override void Execute(IBattleStage stage, Action onComplete)
        {
            stage.SetPvpAttackDisplay(_playerSide, _value);
            onComplete();
        }
    }

    /// <summary>节奏停顿。</summary>
    public sealed class WaitCommand : BattleCommand
    {
        private readonly float _seconds;

        public WaitCommand(float seconds)
        {
            _seconds = seconds;
        }

        public override string Name => "wait";

        public override void Execute(IBattleStage stage, Action onComplete)
        {
            DOVirtual.DelayedCall(_seconds, () => onComplete());
        }
    }

    /// <summary>即时回调：把非演出动作（如战斗后应用真实 HP）排进队列保证顺序。</summary>
    public sealed class ActionCommand : BattleCommand
    {
        private readonly Action _action;

        public ActionCommand(Action action)
        {
            _action = action;
        }

        public override string Name => "action";

        public override void Execute(IBattleStage stage, Action onComplete)
        {
            _action();
            onComplete();
        }
    }
}
