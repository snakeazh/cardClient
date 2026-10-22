using System;
using App.Game;
using CardShare.Contracts;

namespace App.UI.Game.Director
{
    /// <summary>
    /// PVP 服务器驱动：把 match_update 快照 diff 成演出命令，交给 <see cref="BattleDirector"/> 顺序播放；
    /// match_event（round_start / duel_resolved / settle_start）为主触发，快照 diff 保留兜底。
    /// 状态同步（座位/手牌/HP/Hint）即时写入 GameSession；比牌期间的 HP 延后到攻击播完再应用，避免提前剧透。
    /// 队列忙时演出类快照暂存；非摊牌快照（含技能改牌）仍立刻 ApplyPvpState，避免搓牌/替换等死等旧牌。
    /// </summary>
    public sealed class PvpBattleDriver
    {
        private readonly GameSession _session;
        private readonly BattleDirector _director;
        private int _lastRound;
        private int _dealtRound;
        private bool _compareEnqueued;
        private PvpMatchStateDto _pending;
        private string _pendingUserId;
        private PvpMatchStateDto _lastMatch;
        private string _lastUserId;
        private int _dealPendingRound;
        private bool _compareRetryOnDrain;

        public PvpBattleDriver(GameSession session, BattleDirector director)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));
            _director = director ?? throw new ArgumentNullException(nameof(director));
            _director.Drained += OnDrained;
        }

        public bool HasPending => _pending != null;

        /// <summary>演出队列忙或有暂存快照：淘汰离场前等它排空，避免掐掉死亡/结算演出。</summary>
        public bool IsBusy => _director.IsBusy || _pending != null;

        public void Reset()
        {
            _lastRound = 0;
            _dealtRound = 0;
            _compareEnqueued = false;
            _pending = null;
            _pendingUserId = null;
            _lastMatch = null;
            _lastUserId = null;
            _dealPendingRound = 0;
            _compareRetryOnDrain = false;
            _director.Clear();
        }

        public void OnMatch(PvpMatchStateDto match, string userId)
        {
            OnMatch(match, userId, catchUp: false);
        }

        /// <param name="catchUp">sync/冷恢复：清空演出队列，静态对齐到快照，不重播发牌/比牌/死亡。</param>
        public void OnMatch(PvpMatchStateDto match, string userId, bool catchUp)
        {
            if (match == null)
            {
                return;
            }

            if (catchUp)
            {
                SnapTo(match, userId);
                return;
            }

            if (_director.IsBusy)
            {
                _pending = match;
                _pendingUserId = userId;
                // 发牌演出期间技能快照不能卡在 pending：手牌与次数立刻写入，
                // 否则搓牌/替换等 WaitNextUpdate 返回时 Session 仍是旧牌。
                // 比牌已经入队时不能在这里写 HP：settle/finished 快照会把人写成死亡，攻击还没播完。
                if (!IsShowdown(match) && !_compareEnqueued)
                {
                    _session.ApplyPvpState(match, userId);
                    _lastMatch = match;
                    _lastUserId = userId;
                    _lastRound = match.Round;
                }

                return;
            }

            Process(match, userId);
        }

        /// <summary>
        /// 重连/进桌对齐。settle/shop 带上轮摊牌时：瞬时亮出结果牌面，稍停再切商店（不发牌、不播攻击）。
        /// 其它阶段静态对齐并用 SnapDeal 落牌。
        /// </summary>
        public void SnapTo(PvpMatchStateDto match, string userId)
        {
            if (match == null)
            {
                return;
            }

            _director.Clear();
            _pending = null;
            _pendingUserId = null;
            _dealPendingRound = 0;
            _compareRetryOnDrain = false;
            _lastMatch = match;
            _lastUserId = userId;
            _lastRound = match.Round;
            _dealtRound = match.Round;

            var shopPhase = string.Equals(match.Phase, PvpPhases.Shop, StringComparison.OrdinalIgnoreCase);
            var settlePhase = string.Equals(match.Phase, PvpPhases.Settle, StringComparison.OrdinalIgnoreCase);
            var showdown = IsShowdown(match);
            _compareEnqueued = showdown || shopPhase || settlePhase
                || string.Equals(match.Phase, PvpPhases.Finished, StringComparison.OrdinalIgnoreCase);

            if ((shopPhase || settlePhase) && showdown)
            {
                // 直接亮摊牌结果 → 短停 → 进商店（不播发牌/翻牌/攻击）。
                _session.ApplyPvpState(match, userId, deferHp: false, catchUpResult: true);
                _session.RequestPvpSnapDeal();
                _director.Enqueue(new WaitCommand(0.8f));
                _director.Enqueue(new ActionCommand(() => _session.ApplyPvpState(match, userId)));
                return;
            }

            _session.ApplyPvpState(match, userId);
            if (match.Duel != null && match.Round > 0)
            {
                _session.RequestPvpSnapDeal();
            }
        }

        /// <summary>
        /// match_event 主触发（同批 match_update 先到，快照已处理或压在 pending）：
        /// round_start → 发牌；duel_resolved（本人这桌）→ 比牌链；settle_start → 兜底补排比牌。
        /// </summary>
        public void OnEvent(PvpMatchEventDto evt, string userId)
        {
            if (evt == null)
            {
                return;
            }

            if (evt.Kind == PvpEventKinds.RoundStart)
            {
                RequestDeal(evt.Round);
                return;
            }

            if (evt.Kind == PvpEventKinds.SettleStart)
            {
                TryEnqueueCompare();
                return;
            }

            if (evt.Kind == PvpEventKinds.DuelResolved && App.Net.PvpMatchSession.SameUser(evt.UserId, userId))
            {
                TryEnqueueCompare();
            }
        }

        private void OnDrained()
        {
            var pending = _pending;
            if (pending != null)
            {
                _pending = null;
                var userId = _pendingUserId;
                _pendingUserId = null;
                OnMatch(pending, userId);
            }

            if (_dealPendingRound > 0)
            {
                var round = _dealPendingRound;
                _dealPendingRound = 0;
                RequestDeal(round);
            }

            if (_compareRetryOnDrain)
            {
                _compareRetryOnDrain = false;
                TryEnqueueCompare();
            }
        }

        /// <summary>发牌：队列忙时只记轮次，等 pending 快照重放（新牌先写入座位）后再排 DealCommand，避免发牌动画发到旧牌。</summary>
        private void RequestDeal(int round)
        {
            if (round <= 0 || round == _dealtRound)
            {
                return;
            }

            if (_director.IsBusy)
            {
                _dealPendingRound = round;
                return;
            }

            _compareEnqueued = false;
            _dealtRound = round;
            _director.Enqueue(new DealCommand());
        }

        /// <summary>比牌入队：最新快照已显示自己这桌摊牌才排；快照还压在 pending 里时等排空重试（settle 窗口内一定补排上）。</summary>
        private void TryEnqueueCompare()
        {
            if (_compareEnqueued)
            {
                return;
            }

            if (_director.IsBusy || !IsShowdown(_lastMatch))
            {
                _compareRetryOnDrain = true;
                return;
            }

            EnqueueCompare(_lastMatch, _lastUserId);
        }

        private void Process(PvpMatchStateDto match, string userId)
        {
            _lastMatch = match;
            _lastUserId = userId;
            var showdown = IsShowdown(match);
            var fightPhase = string.Equals(match.Phase, PvpPhases.Fight, StringComparison.OrdinalIgnoreCase);
            var settlePhase = string.Equals(match.Phase, PvpPhases.Settle, StringComparison.OrdinalIgnoreCase);
            var finishedPhase = string.Equals(match.Phase, PvpPhases.Finished, StringComparison.OrdinalIgnoreCase);
            if (showdown && _compareEnqueued)
            {
                // 攻击命令已经播完（队列空才会进到这里）：这时才把扣血/淘汰写上。
                _session.ApplyPvpState(match, userId);
                _lastRound = match.Round;
                return;
            }

            // 全桌打完会在同一拍从 fight 进 settle/finished，快照已经带 Hp=0。
            // 这一拍也要先播攻击，不能直接 Apply。shop 上残留的 Showdown 不重播。
            if (showdown && (fightPhase || settlePhase || finishedPhase))
            {
                EnqueueCompare(match, userId);
                _lastRound = match.Round;
                return;
            }

            _session.ApplyPvpState(match, userId);
            if (match.Round != _lastRound && match.Round != _dealtRound)
            {
                _compareEnqueued = false;
                _dealtRound = match.Round;
                _director.Enqueue(new DealCommand());
            }

            _lastRound = match.Round;
        }

        /// <summary>比牌序列：状态（扣血延后）→ 必要时补发牌 → 翻牌 → 攻击力跳动 → 撞击 → 应用真实 HP。</summary>
        private void EnqueueCompare(PvpMatchStateDto match, string userId)
        {
            _compareEnqueued = true;
            _session.ApplyPvpState(match, userId, deferHp: true);
            // 进桌时手里已经是摊牌/结算快照：这一轮没发过牌就直接翻牌，DealSerial 仍是 0，牌桌什么都不画。
            if (match.Round > 0 && match.Round != _dealtRound)
            {
                _dealtRound = match.Round;
                _director.Enqueue(new DealCommand());
            }

            SplitSeats(match, userId, out var mine, out var foe, out var viewer);
            var playerWon = ResolvePlayerWon(match, mine, foe, viewer);
            var winScore = _session.EvaluateSeat(playerWon ? _session.Player : _session.Enemies[0]);
            var loseScore = _session.EvaluateSeat(playerWon ? _session.Enemies[0] : _session.Player);
            var damage = ResolveDamage(match, mine, foe, playerWon, winScore);
            // 挨打展示用：用权威 DuelDamage（可为 0），不要再用对手座位 Damage 把闪避盖成有伤。
            var foeDamage = !playerWon ? damage : 0;

            _director.Enqueue(new RevealCommand(playerWon));
            // 出伤方不要在结算链前把攻击数字改成最终伤害，否则圣物分项演出会从终值起跳。
            // 挨打时对手攻击跳到服务端伤害即可（闪避为 0 则不改，避免露出假伤害）。
            if (!playerWon && foeDamage > 0)
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

        /// <summary>
        /// 伤害纯信服务端状态同步（DuelDamage = 含轮次系数的实际扣血，含 0 = 闪避/免疫）。
        /// 仅摊牌前或未带权威值时才本地兜底，保证撞击能播。
        /// </summary>
        private int ResolveDamage(
            PvpMatchStateDto match,
            BattleSeatDto mine,
            BattleSeatDto foe,
            bool playerWon,
            HandScore winScore)
        {
            // 摊牌快照里 DuelDamage 已是权威（闪避成功时为 0），不能再走本地公式把 MISS 盖成伤害字。
            if (IsShowdown(match))
            {
                return Math.Max(0, match.DuelDamage);
            }

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
                   string.Equals(match.Duel.Phase, BattlePhaseNames.Showdown, StringComparison.OrdinalIgnoreCase);
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
