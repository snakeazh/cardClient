using System;
using App.Game;
using CardShare.Contracts;

namespace App.UI.Game.Director
{
    /// <summary>
    /// PVP 服务器驱动：把 match_update 快照 diff 成演出命令，交给 <see cref="BattleDirector"/> 顺序播放。
    /// 状态同步（座位/手牌/HP/Hint）即时写入 GameSession；比牌期间的 HP 延后到攻击播完再应用，避免提前剧透。
    /// 队列忙时新快照整体暂存，排空后重放——时序由队列保证，不再散落各处打补丁。
    /// </summary>
    public sealed class PvpBattleDriver
    {
        private readonly GameSession _session;
        private readonly BattleDirector _director;
        private int _lastRound;
        private bool _compareEnqueued;
        private PvpMatchStateDto _pending;
        private string _pendingUserId;

        public PvpBattleDriver(GameSession session, BattleDirector director)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));
            _director = director ?? throw new ArgumentNullException(nameof(director));
            _director.Drained += OnDrained;
        }

        public bool HasPending => _pending != null;

        public void Reset()
        {
            _lastRound = 0;
            _compareEnqueued = false;
            _pending = null;
            _pendingUserId = null;
            _director.Clear();
        }

        public void OnMatch(PvpMatchStateDto match, string userId)
        {
            if (match == null)
            {
                return;
            }

            if (_director.IsBusy)
            {
                _pending = match;
                _pendingUserId = userId;
                return;
            }

            Process(match, userId);
        }

        private void OnDrained()
        {
            var pending = _pending;
            if (pending == null)
            {
                return;
            }

            _pending = null;
            var userId = _pendingUserId;
            _pendingUserId = null;
            OnMatch(pending, userId);
        }

        private void Process(PvpMatchStateDto match, string userId)
        {
            var showdown = IsShowdown(match);
            if (showdown && _compareEnqueued)
            {
                // 同一桌比牌的后续快照（如 finished）：状态已在比牌序列末尾应用，这里只同步（排名 Hint 等）。
                _session.ApplyPvpState(match, userId);
                _lastRound = match.Round;
                return;
            }

            if (showdown)
            {
                EnqueueCompare(match, userId);
                _lastRound = match.Round;
                return;
            }

            _session.ApplyPvpState(match, userId);
            if (match.Round != _lastRound)
            {
                _compareEnqueued = false;
                _director.Enqueue(new DealCommand());
            }

            _lastRound = match.Round;
        }

        /// <summary>比牌序列：状态（扣血延后）→ 翻牌 → 攻击力跳动 → 撞击 → 应用真实 HP。</summary>
        private void EnqueueCompare(PvpMatchStateDto match, string userId)
        {
            _compareEnqueued = true;
            _session.ApplyPvpState(match, userId, deferHp: true);
            SplitSeats(match, userId, out var mine, out var foe, out var viewer);
            var playerWon = ResolvePlayerWon(match, mine, foe, viewer);
            var winScore = _session.EvaluateSeat(playerWon ? _session.Player : _session.Enemies[0]);
            var loseScore = _session.EvaluateSeat(playerWon ? _session.Enemies[0] : _session.Player);
            var damage = ResolveDamage(match, mine, foe, playerWon, winScore);
            var mineDamage = mine != null && mine.Damage != null ? Math.Max(1, mine.Damage.Value) : 0;
            var foeDamage = foe != null && foe.Damage != null ? Math.Max(1, foe.Damage.Value) : 0;

            _director.Enqueue(new RevealCommand(playerWon));
            if (mineDamage > 0)
            {
                _director.Enqueue(new SetAttackCommand(true, mineDamage));
            }

            if (foeDamage > 0)
            {
                _director.Enqueue(new SetAttackCommand(false, foeDamage));
            }

            _director.Enqueue(new WaitCommand(0.4f));
            _director.Enqueue(new AttackCommand(!playerWon, damage, winScore.Type, winScore.Label, loseScore.Label));
            _director.Enqueue(new ActionCommand(() => _session.ApplyPvpState(match, userId)));
        }

        private bool ResolvePlayerWon(PvpMatchStateDto match, BattleSeatDto mine, BattleSeatDto foe, int viewer)
        {
            if (match.Duel != null && match.Duel.Winners != null && match.Duel.Winners.Count == 1)
            {
                return match.Duel.Winners[0] == viewer;
            }

            if (mine != null && foe != null && mine.Damage != null && foe.Damage != null &&
                mine.Damage.Value != foe.Damage.Value)
            {
                return mine.Damage.Value > foe.Damage.Value;
            }

            var playerScore = _session.EvaluateSeat(_session.Player);
            var enemyScore = _session.EvaluateSeat(_session.Enemies[0]);
            return playerScore.CompareTo(enemyScore) >= 0;
        }

        /// <summary>伤害纯信服务端状态同步（DuelDamage = 含轮次系数的实际扣血）；缺失时才本地兜底，保证撞击能播。</summary>
        private int ResolveDamage(
            PvpMatchStateDto match,
            BattleSeatDto mine,
            BattleSeatDto foe,
            bool playerWon,
            HandScore winScore)
        {
            if (match.DuelDamage > 0)
            {
                return match.DuelDamage;
            }

            var winnerDto = playerWon ? mine : foe;
            var raw = winnerDto != null && winnerDto.Damage != null ? Math.Max(0, winnerDto.Damage.Value) : 0;
            if (raw > 0)
            {
                return raw;
            }

            return _session.ComputePvpAttackFallback(playerWon, winScore);
        }

        private static bool IsShowdown(PvpMatchStateDto match)
        {
            return match.Duel != null &&
                   string.Equals(match.Duel.Phase, "showdown", StringComparison.OrdinalIgnoreCase);
        }

        private static void SplitSeats(
            PvpMatchStateDto match,
            string userId,
            out BattleSeatDto mine,
            out BattleSeatDto foe,
            out int viewer)
        {
            mine = null;
            foe = null;
            var duel = match.Duel;
            viewer = duel != null ? duel.ViewerSeat : 0;
            if (duel == null || duel.Seats == null)
            {
                return;
            }

            for (var i = 0; i < duel.Seats.Count; i++)
            {
                var seat = duel.Seats[i];
                if (seat.SeatId == viewer || App.Net.PvpMatchSession.SameUser(seat.UserId, userId))
                {
                    mine = seat;
                }
                else
                {
                    foe = seat;
                }
            }
        }
    }
}
