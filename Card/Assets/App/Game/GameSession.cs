using System;
using System.Collections.Generic;
using App.Bootstrap;
using App.Config;
using App.Level;
using App.Score;

namespace App.Game
{
    /// <summary>
    /// 炸金花闯关对局状态机：发牌并看牌 → 开牌或技能 → 与敌人逐个比牌 → (攻击力+牌面点数)×牌型倍率结算伤害。
    /// 敌人座位固定 3 个，人格在 <see cref="CreateSeat"/> 绑定，BOSS 关覆盖成 Expert。
    /// </summary>
    public sealed class GameSession
    {
        public const int MaxEnemies = 3;

        private readonly Random _rng;
        private Deck _deck;
        /// <summary>本街已出现的最高下注档位（闷注单位）。</summary>
        private int _maxStreetUnits;
        /// <summary>当前关卡回合基础注。第一回合 10，之后每回合 +10。</summary>
        private int _roundBaseBet = GameBalance.BaseBetStart;
        /// <summary>当轮基础单注，等于 <see cref="_roundBaseBet"/>。</summary>
        private int _betStep = GameBalance.BaseBetStart;
        /// <summary>本关已开始的手数。第一手基础注 10，之后每手 +10。</summary>
        private int _stageBetRound;
        /// <summary>连续无加注的街数，满 2 街强制摊牌。</summary>
        private int _streetsWithoutRaise;
        private int _bettingRound = 1;
        private bool _streetHadRaise;
        private bool _playerActedThisStreet;
        private bool _aiStreetActive;
        private int _aiPass;
        private int _aiCursor;
        private bool _aiPendingOpen;
        private bool _needAiRescan;
        private int _pendingRubIndex = -1;
        private RevealKind _revealKind;
        private SeatState _pendingWinner;
        private HandScore _pendingBest;
        private SeatState _pendingOpener;
        private SeatState _pendingOpenTarget;
        private bool _pendingOpenerWins;
        private SeatState _pendingAttackTarget;
        private readonly List<SeatState> _compareQueue = new List<SeatState>();
        private int _compareCursor;
        private int _roundDamageDealt;
        private bool _sequentialCompare;
        /// <summary>本回合在血量换算之外额外获得的勇气值（借贷券 / 广告借贷）。</summary>
        private int _loanCourageBonus;
        /// <summary>本关商店已按积分发放的金币，供双倍广告再发一份。</summary>
        private int _shopGoldGranted;
        /// <summary>跨手记录玩家弃/加/看/闷，供 AI 读线。</summary>
        public readonly PlayerHistory History = new PlayerHistory();

        public GameSession() : this(new Random())
        {
        }

        public GameSession(Random rng)
        {
            _rng = rng ?? new Random();
            Run = new RunState();
            Player = CreateSeat(0, "你", true);
            // 座位写死 3 个：A 保守 / B 平衡偏激进 / C 激进。每关再按关卡配置决定谁上场。
            Enemies = new[]
            {
                CreateSeat(1, "敌人A", false),
                CreateSeat(2, "敌人B", false),
                CreateSeat(3, "敌人C", false)
            };
        }

        public event Action Changed;

        public RunState Run { get; }
        public SeatState Player { get; }
        public SeatState[] Enemies { get; }
        public GamePhase Phase { get; private set; } = GamePhase.Idle;
        public int Pot { get; private set; }
        public int BetUnits { get; set; } = GameBalance.MinBet;
        public string Hint { get; private set; } = "点击开始闯关";
        public string LastResult { get; private set; } = string.Empty;
        public bool CardsRevealed { get; private set; }
        public int DealSerial { get; private set; }
        public int BettingRound => _bettingRound;
        /// <summary>本关第几手，从 1 起。</summary>
        public int StageRoundIndex => _stageBetRound < 1 ? 1 : _stageBetRound;
        public int RoundBaseBet => _roundBaseBet;
        public int CurrentCallUnits => CurrentRoundUnits();
        public int PlayerCallCost => CostToReach(CurrentRoundUnits());
        public int RaiseLowUnits => RaiseUnits(GameBalance.RaiseLowMult);
        public int RaiseHighUnits => RaiseUnits(GameBalance.RaiseHighMult);
        public ScoreSnapshot Score => ScoreSvc()?.Current ?? new ScoreSnapshot(0, 0, 0);
        /// <summary>本关结算刚发放的金币（含双倍）。</summary>
        public int ShopGoldGranted => _shopGoldGranted;
        /// <summary>本关每回合积分，进下一关 BeginStage 后清空。</summary>
        public IReadOnlyList<int> StageRoundScores =>
            ScoreSvc()?.StageRoundScores ?? Array.Empty<int>();
        public int PendingAttackDamage { get; private set; }
        public int AttackPlaySerial { get; private set; }
        public int AttackVisualSlot { get; private set; } = -1;
        public int AttackDamage { get; private set; }
        /// <summary>攻击演出强度：1 低 / 2 中 / 3 高。</summary>
        public int AttackLevel { get; private set; } = 1;
        public bool AttackPlaying => _pendingAttackTarget != null;
        /// <summary>当前攻击由敌人打向玩家。</summary>
        public bool IncomingAttack { get; private set; }
        /// <summary>本手正在逐个与敌人比牌。</summary>
        public bool SequentialCompare => _sequentialCompare;
        /// <summary>回合指示箭头：玩家 -1，敌人视觉槽 0/1/2，无人 -2。</summary>
        public const int TurnArrowNone = -2;
        public const int TurnArrowPlayer = -1;
        public int TurnArrowSlot
        {
            get
            {
                if (SequentialCompare ||
                    Phase == GamePhase.Showdown ||
                    Phase == GamePhase.WaitingAttack)
                {
                    return EnemyTurnSlot(_pendingAttackTarget ?? _pendingOpenTarget);
                }

                if (AiActing)
                {
                    return EnemyTurnSlot(FindEnemyById(ActingAiId));
                }

                if (Phase == GamePhase.WaitingOpen ||
                    Phase == GamePhase.WaitingRub ||
                    Phase == GamePhase.WaitingLookChoice ||
                    Phase == GamePhase.Betting)
                {
                    return TurnArrowPlayer;
                }

                return TurnArrowNone;
            }
        }
        public int RevealPlaySerial { get; private set; }
        public int RevealWinnerId { get; private set; } = -1;
        public readonly List<int> RevealSeatIds = new List<int>();
        public bool SelectingOpenTarget { get; private set; }
        public bool SelectingXRayTarget { get; private set; }
        public bool AiActing { get; private set; }
        public int ActingAiId { get; private set; } = -1;
        public const float AiActionDelay = 1f;
        public bool PlayerMayLookCards =>
            !AiActing &&
            Phase == GamePhase.WaitingLookChoice &&
            !Player.Looked &&
            !Player.Folded;
        public bool PlayerMayCancelLookOrRub =>
            Phase == GamePhase.WaitingRub;
        public bool PlayerCanOpen => Phase == GamePhase.WaitingOpen && !Player.Folded && AnyEnemyAlive();
        public bool PlayerMayCompare =>
            !AiActing &&
            Phase == GamePhase.WaitingOpen &&
            !Player.Folded &&
            AnyEnemyAlive() &&
            Player.CountSelectedCards() == GameBalance.OpenHandSize &&
            _revealKind == RevealKind.None &&
            !AttackPlaying;

        public IEnumerable<SeatState> AllSeats()
        {
            yield return Player;
            for (var i = 0; i < Enemies.Length; i++)
            {
                yield return Enemies[i];
            }
        }

        public void StartNewRun()
        {
            Run.Gold = 0;
            Run.Stage = 1;
            Run.ConsecutiveLosses = 0;
            Run.Tilted = false;
            Run.Relics.Clear();
            Run.RelicConfigIds.Clear();
            Run.ShopOfferIds.Clear();
            Run.ShopRefreshCount = 0;
            Run.LoanTicket = false;
            Run.SplashThisRound = false;
            Run.MagnifierThisRound = false;
            Run.AdsDoubleGoldToday = 0;
            Run.BonusRubCharges = 0;
            Run.BonusXRayCharges = 0;
            Run.BonusReplaceCharges = 0;
            Run.Log.Clear();
            if (AppServices.IsReady)
            {
                AppServices.Resolve<IScoreService>().BeginChapter();
            }

            StartStage(inheritPlayerHp: false);
        }

        public void RestartStage()
        {
            StartStage(inheritPlayerHp: false);
        }

        public void SelectRubCard(int index)
        {
            if (Phase != GamePhase.WaitingRub || index < 0 || index >= GameBalance.PlayerCardsDealt)
            {
                return;
            }

            _pendingRubIndex = index;
            Hint = $"已选中第 {index + 1} 张牌，点选即随机替换花色和点数";
            Notify();
        }

        public bool TryRubSelected()
        {
            if (Phase != GamePhase.WaitingRub)
            {
                return false;
            }

            if (_pendingRubIndex < 0)
            {
                Hint = "请先点选一张手牌再搓牌";
                Notify();
                return false;
            }

            RubCard(_pendingRubIndex);
            return true;
        }

        public void RubCard(int index)
        {
            if (Phase != GamePhase.WaitingRub || index < 0 || index >= GameBalance.PlayerCardsDealt || Run.RubsLeft <= 0)
            {
                return;
            }

            var old = Player.Hand[index];
            var next = DrawRubCard(old);
            if (!next.IsValid || next.Equals(old))
            {
                Hint = "没有可换的新牌";
                Notify();
                return;
            }

            Player.Hand[index] = next;
            Run.RubsLeft--;
            if (Run.PeekGoodCharges > 0)
            {
                Run.PeekGoodCharges--;
            }
            _pendingRubIndex = -1;
            Run.LastRubMessage = $"第 {index + 1} 张换成 {next.DisplayName}";
            Log(Run.LastRubMessage);

            if (Run.RubsLeft > 0)
            {
                Hint = $"{Run.LastRubMessage}。还可再搓 {Run.RubsLeft} 次，或点取消跳过";
                Notify();
                return;
            }

            ReturnToOpenReady(Run.LastRubMessage);
        }

        public void CancelLookOrRub()
        {
            SkipRub();
        }

        public void SkipRub()
        {
            if (Phase != GamePhase.WaitingRub)
            {
                return;
            }

            _pendingRubIndex = -1;
            Run.RubsLeft = 0;
            ReturnToOpenReady("已跳过搓牌");
        }

        public void PeekMagnifier(int index)
        {
            if (Phase != GamePhase.WaitingOpen || !Run.MagnifierThisRound || Run.PeekSuitUsed)
            {
                return;
            }

            if (index < 0 || index >= GameBalance.PlayerCardsDealt)
            {
                return;
            }

            Run.PeekSuitUsed = true;
            Run.PeekSuitIndex = index;
            Run.PeekedSuit = Player.Hand[index].Suit;
            Hint = $"放大镜：第 {index + 1} 张是{Card.SuitName(Player.Hand[index].Suit)}（未见点数）";
            Notify();
        }

        /// <summary>看牌后进入下注栏。搓牌改为技能，不再强制进入搓牌。</summary>
        public void LookCards()
        {
            if (!PlayerMayLookCards)
            {
                return;
            }

            Player.Looked = true;
            Player.Status = "已看牌";
            History.NoteLook();
            ReturnToOpenReady("点选 3 张牌后开牌。可使用技能");
        }

        /// <summary>点选手牌。选中上移，开牌用这 3 张；再点取消。</summary>
        public void TogglePlayerCard(int index)
        {
            if (AiActing || Player.Folded)
            {
                return;
            }

            if (Phase != GamePhase.WaitingOpen)
            {
                return;
            }

            if (index < 0 || index >= GameBalance.PlayerCardsDealt)
            {
                return;
            }

            if (Player.IsCardSelected(index))
            {
                Player.CardSelected[index] = false;
            }
            else
            {
                if (Player.CountSelectedCards() >= GameBalance.OpenHandSize)
                {
                    Hint = "已经选了 3 张，再点已选中的牌可取消";
                    Notify();
                    return;
                }

                Player.CardSelected[index] = true;
            }

            var picked = Player.CountSelectedCards();
            Hint = picked >= GameBalance.OpenHandSize
                ? "已选 3 张，可开牌"
                : $"已选 {picked}/{GameBalance.OpenHandSize} 张，点选卡牌上移表示开牌用牌";
            Notify();
        }

        public void AdjustBetUnits(int delta)
        {
            Notify();
        }

        /// <summary>闷注只是选择不看牌，进入下注栏；真正扣血要再点跟注 / 加注 / 全下。</summary>
        public void BlindBet()
        {
            if (AiActing || Player.Folded)
            {
                return;
            }

            if (Phase == GamePhase.WaitingLookChoice)
            {
                EnterBetting();
                return;
            }

            if (Phase != GamePhase.Betting && Phase != GamePhase.WaitingRub)
            {
                return;
            }

            LeaveRubIfNeeded();
            PlacePlayerBet(false);
        }

        /// <summary>加注到 当前跟注 + 基础注×2。</summary>
        public void RaiseBet()
        {
            RaiseBet(GameBalance.RaiseLowMult);
        }

        /// <summary>加注到 当前跟注 + 基础注×4。</summary>
        public void RaiseBetHigh()
        {
            RaiseBet(GameBalance.RaiseHighMult);
        }

        public void RaiseBet(int multiplier)
        {
            LeaveRubIfNeeded();
            if (!PlayerMayRaise)
            {
                return;
            }

            PlacePlayerBet(true, multiplier);
        }

        public bool PlayerMayAllIn =>
            !AiActing &&
            !Player.Folded &&
            Player.Hp > 0 &&
            Player.Courage > 0 &&
            (Phase == GamePhase.Betting || Phase == GamePhase.WaitingRub);

        public bool PlayerMayRaise =>
            !AiActing &&
            !Player.Folded &&
            (Phase == GamePhase.Betting || Phase == GamePhase.WaitingRub);

        public bool PlayerMayFold => false;

        private bool PlayerMayUseItems =>
            !Player.Folded &&
            (Phase == GamePhase.WaitingOpen ||
             Phase == GamePhase.WaitingRub);

        public bool PlayerMayUsePeekGood =>
            PlayerMayUseItems &&
            Player.Looked &&
            Run.PeekGoodCharges > 0 &&
            Phase != GamePhase.WaitingRub;

        public bool PlayerMayUseChaKanGood =>
            PlayerMayUseItems &&
            Run.ChaKanGoodCharges > 0;

        public bool PlayerMayUseTiHuanGood =>
            PlayerMayUseItems &&
            Run.TiHuanGoodCharges > 0 &&
            _deck != null;

        /// <summary>把剩余勇气值推进底池。全下后若还有人能下注，对手继续打边池。</summary>
        public void AllIn()
        {
            LeaveRubIfNeeded();
            if (!PlayerMayAllIn)
            {
                return;
            }

            SelectingOpenTarget = false;
            var roundUnits = CurrentRoundUnits();
            TryCommitUnits(Player, roundUnits, out var paid);
            if (Player.Courage > 0)
            {
                var rest = Player.Courage;
                SpendCourage(Player, rest);
                Player.TotalBet += rest;
                Player.StreetPaid += rest;
                Pot += rest;
                paid += rest;
            }

            _playerActedThisStreet = true;
            Player.Status = $"全下 {paid}";
            Log($"{Player.Status}，奖池 {Pot}");
            ResolveAiStreet();
        }

        public void UsePeekGood()
        {
            if (!PlayerMayUsePeekGood)
            {
                return;
            }

            Player.Status = "已看牌";
            Run.RubsLeft = 1;
            _pendingRubIndex = -1;
            Phase = GamePhase.WaitingRub;
            Hint = $"搓牌（剩余 {Run.PeekGoodCharges}）。点选一张手牌，随机替换花色和点数";
            Log("使用技能：搓牌");
            Notify();
        }

        public void UseChaKanGood()
        {
            if (!PlayerMayUseChaKanGood)
            {
                return;
            }

            SelectingXRayTarget = !SelectingXRayTarget;
            Hint = SelectingXRayTarget
                ? $"透视（剩余 {Run.ChaKanGoodCharges}）。点选一名角色透视其手牌"
                : "已取消透视";
            Notify();
        }

        public void TryXRayPlayer()
        {
            if (!SelectingXRayTarget)
            {
                return;
            }

            TryXRaySeat(Player);
        }

        public void TryXRayEnemySlot(int visualSlot)
        {
            if (!SelectingXRayTarget)
            {
                return;
            }

            TryXRaySeat(EnemyAtVisualSlot(visualSlot));
        }

        public void UseTiHuanGood()
        {
            if (!PlayerMayUseTiHuanGood)
            {
                return;
            }

            SyncDeckWithTable();
            var nextCards = new Card[GameBalance.PlayerCardsDealt];
            for (var i = 0; i < nextCards.Length; i++)
            {
                if (!_deck.TryDraw(out nextCards[i]) || !nextCards[i].IsValid)
                {
                    SyncDeckWithTable();
                    Hint = "牌堆不足，无法替换";
                    Notify();
                    return;
                }
            }

            for (var i = 0; i < nextCards.Length; i++)
            {
                Player.Hand[i] = nextCards[i];
            }

            Player.PeekedType = string.Empty;
            Run.TiHuanGoodCharges--;
            if (Player.Looked)
            {
                Hint =
                    $"替换：{FormatPlayerHand()}（剩余 {Run.TiHuanGoodCharges}）";
                Log($"替换手牌为 {FormatPlayerHand()}");
            }
            else
            {
                Hint = $"已替换 {GameBalance.PlayerCardsDealt} 张手牌（未看牌，剩余 {Run.TiHuanGoodCharges}）";
                Log("替换手牌（未看牌）");
            }

            Notify();
        }

        private void TryXRaySeat(SeatState seat)
        {
            if (!PlayerMayUseChaKanGood || seat == null || !HasHand(seat))
            {
                return;
            }

            if (!seat.IsPlayer && (!seat.Alive || seat.Folded))
            {
                return;
            }

            if (!string.IsNullOrEmpty(seat.PeekedType))
            {
                Hint = $"{seat.Name} 已经透视过";
                Notify();
                return;
            }

            var count = GameBalance.CardsDealt(seat.IsPlayer);
            for (var i = 0; i < count; i++)
            {
                SetSpyReveal(seat.Id, i, true);
            }

            var score = EvaluateSeat(seat);
            seat.PeekedType = seat.IsPlayer && seat.CountSelectedCards() != GameBalance.OpenHandSize
                ? "未选定开牌"
                : score.Label;
            Run.ChaKanGoodCharges--;
            SelectingXRayTarget = false;
            Hint = $"透视 {seat.Name}：{seat.PeekedType}（剩余 {Run.ChaKanGoodCharges}）";
            Log($"透视 {seat.Name} {seat.PeekedType}");
            Notify();
        }

        public bool IsSpyRevealed(int seatId, int cardIndex)
        {
            var key = SpyKey(seatId, cardIndex);
            return key >= 0 && key < Run.SpyReveal.Length && Run.SpyReveal[key];
        }

        /// <summary>玩家弃牌。剩余对手立刻亮牌，牌型最高者获胜并攻击玩家。</summary>
        public void Fold()
        {
            if (!PlayerMayFold)
            {
                return;
            }

            LeaveRubIfNeeded();
            if (Phase == GamePhase.WaitingLookChoice)
            {
                Phase = GamePhase.Betting;
            }

            if (_streetHadRaise)
            {
                History.FacedRaiseChances++;
            }

            History.NoteFold();
            SelectingOpenTarget = false;
            FoldSeat(Player, "弃牌");
            Log("你弃牌");
            LastResult = "你弃牌";
            ResolveAfterPlayerFold();
        }

        /// <summary>玩家付双倍注额，强制与一名未弃牌敌人比牌。</summary>
        public void OpenCompare()
        {
            RequestShowdown();
        }

        public void RequestShowdown()
        {
            if (!PlayerMayCompare)
            {
                return;
            }

            SelectingOpenTarget = false;
            StartSequentialCompare();
        }

        /// <summary>赢牌后点选敌人造成伤害。溅射斩会额外打其他存活敌人 30%。</summary>
        public void AttackEnemyAtSlot(int visualSlot)
        {
            if (SelectingXRayTarget)
            {
                TryXRayEnemySlot(visualSlot);
                return;
            }

            if (Phase == GamePhase.Betting && SelectingOpenTarget)
            {
                var duel = EnemyAtVisualSlot(visualSlot);
                if (duel == null || !duel.Alive || duel.Folded)
                {
                    Hint = "请点选一名未弃牌的敌人开牌";
                    Notify();
                    return;
                }

                SelectingOpenTarget = false;
                ForceOpen(Player, duel);
                return;
            }

            if (Phase != GamePhase.WaitingAttack || _sequentialCompare)
            {
                return;
            }

            var target = EnemyAtVisualSlot(visualSlot);
            if (target == null || !target.Alive)
            {
                Hint = "请选择一名存活敌人攻击";
                Notify();
                return;
            }

            BeginPlayerAttack(target);
        }

        public void CompletePlayerAttack()
        {
            if (Phase != GamePhase.WaitingAttack || _pendingAttackTarget == null)
            {
                return;
            }

            var target = _pendingAttackTarget;
            _pendingAttackTarget = null;
            AttackVisualSlot = -1;
            AttackLevel = 1;
            FinishPlayerAttack(target);
        }

        private void BeginPlayerAttack(SeatState target)
        {
            if (Phase != GamePhase.WaitingAttack || target == null || !target.Alive || _pendingAttackTarget != null)
            {
                return;
            }

            _pendingAttackTarget = target;
            AttackVisualSlot = FindVisualSlot(target);
            AttackDamage = Math.Max(1, PendingAttackDamage);
            if (AttackLevel < 1 || AttackLevel > 3)
            {
                AttackLevel = 1;
            }

            AttackPlaySerial++;
            Hint = $"攻击 {target.Name}！";
            Notify();
        }

        public bool CanAttackSlot(int visualSlot)
        {
            var target = EnemyAtVisualSlot(visualSlot);
            if (target == null || !target.Alive)
            {
                return false;
            }

            if (SelectingXRayTarget)
            {
                return HasHand(target) && !target.Folded;
            }

            if (Phase == GamePhase.WaitingAttack)
            {
                return !_sequentialCompare && !AttackPlaying;
            }

            return Phase == GamePhase.Betting && SelectingOpenTarget && !target.Folded;
        }

        public void AnnounceSeatRevealed(int seatId)
        {
            if (_revealKind == RevealKind.None)
            {
                return;
            }

            var seat = SeatById(seatId);
            if (seat == null)
            {
                return;
            }

            RevealHand(seat);
            var score = EvaluateSeat(seat);
            Hint = seat.Folded
                ? $"{seat.Name} 弃牌，亮出 {score.Label}"
                : $"{seat.Name} 亮出 {score.Label}！";
            Notify();
        }

        public void FinishRevealPlay()
        {
            var kind = _revealKind;
            _revealKind = RevealKind.None;
            if (kind == RevealKind.Showdown)
            {
                ApplyShowdownSettlement();
                return;
            }

            if (kind == RevealKind.OpenDuel)
            {
                ApplyOpenDuelSettlement();
            }
        }

        private void FinishPlayerAttack(SeatState target)
        {
            IncomingAttack = false;
            var damage = Math.Max(1, PendingAttackDamage);
            PendingAttackDamage = 0;
            var dealt = ApplyDamage(target, damage, true);
            if (!target.IsPlayer && Run.SplashThisRound)
            {
                for (var i = 0; i < Enemies.Length; i++)
                {
                    if (Enemies[i] != target && Enemies[i].Alive)
                    {
                        dealt += ApplyDamage(Enemies[i], (int)Math.Round(damage * GameBalance.SplashRatio), false);
                    }
                }

                Run.SplashThisRound = false;
            }

            if (!target.IsPlayer)
            {
                _roundDamageDealt += dealt;
            }

            if (!_sequentialCompare)
            {
                AfterRound();
                return;
            }

            if (Player.Hp <= 0)
            {
                FinishSequentialCompare();
                return;
            }

            _compareCursor++;
            RunNextCompare();
        }

        private void BeginIncomingAttack(SeatState attacker)
        {
            if (Phase != GamePhase.WaitingAttack || attacker == null || Player.Hp <= 0 || _pendingAttackTarget != null)
            {
                return;
            }

            IncomingAttack = true;
            _pendingAttackTarget = Player;
            AttackVisualSlot = FindVisualSlot(attacker);
            AttackDamage = Math.Max(1, PendingAttackDamage);
            if (AttackLevel < 1 || AttackLevel > 3)
            {
                AttackLevel = 1;
            }

            AttackPlaySerial++;
            Hint = $"{attacker.Name} 攻击你！";
            Notify();
        }

        private void StartSequentialCompare()
        {
            _sequentialCompare = true;
            _compareQueue.Clear();
            _compareCursor = 0;
            _roundDamageDealt = 0;
            IncomingAttack = false;
            SelectingXRayTarget = false;
            for (var i = 0; i < Enemies.Length; i++)
            {
                if (Enemies[i].Alive)
                {
                    _compareQueue.Add(Enemies[i]);
                }
            }

            if (_compareQueue.Count == 0)
            {
                FinishSequentialCompare();
                return;
            }

            Player.Status = "开牌";
            Log("开牌，与敌人逐一比牌");
            RunNextCompare();
        }

        private void RunNextCompare()
        {
            if (!_sequentialCompare)
            {
                AfterRound();
                return;
            }

            if (Player.Hp <= 0)
            {
                FinishSequentialCompare();
                return;
            }

            while (_compareCursor < _compareQueue.Count &&
                   (_compareQueue[_compareCursor] == null || !_compareQueue[_compareCursor].Alive))
            {
                _compareCursor++;
            }

            if (_compareCursor >= _compareQueue.Count || !AnyEnemyAlive())
            {
                FinishSequentialCompare();
                return;
            }

            var enemy = _compareQueue[_compareCursor];
            LockBestOpenCardsIfEnemy(enemy);
            var openScore = EvaluateSeat(Player);
            var targetScore = EvaluateSeat(enemy);
            _pendingOpener = Player;
            _pendingOpenTarget = enemy;
            _pendingOpenerWins = openScore.CompareTo(targetScore) > 0;
            _pendingWinner = _pendingOpenerWins ? Player : enemy;
            _pendingBest = _pendingOpenerWins ? openScore : targetScore;
            BeginRevealPlay(RevealKind.OpenDuel, BuildDuelRevealOrder(Player, enemy), _pendingWinner);
            Hint = $"开牌：你 vs {enemy.Name}";
            LastResult = Hint;
            Notify();
        }

        private void FinishSequentialCompare()
        {
            _sequentialCompare = false;
            IncomingAttack = false;
            if (_roundDamageDealt > 0)
            {
                AwardPlayerRoundScore(_roundDamageDealt);
            }

            if (string.IsNullOrEmpty(LastResult))
            {
                LastResult = _roundDamageDealt > 0
                    ? $"本手造成 {_roundDamageDealt} 伤害"
                    : "本手比牌结束";
            }

            AfterRound();
        }

        private void ResolveSequentialDuel()
        {
            var opener = _pendingOpener;
            var target = _pendingOpenTarget;
            if (opener != null)
            {
                RevealHand(opener);
            }

            if (target != null)
            {
                RevealHand(target);
            }

            var openScore = opener != null ? EvaluateSeat(opener) : default;
            var targetScore = target != null ? EvaluateSeat(target) : default;
            if (_pendingOpenerWins)
            {
                var damage = ComputeAttackDamage(opener, openScore);
                PendingAttackDamage = damage;
                AttackLevel = MapAttackLevel(openScore.Type);
                LastResult = $"{HandDrama(openScore.Type)}！你的{openScore.Label}压过 {target?.Name} 的{targetScore.Label}，造成 {damage} 伤害";
                Log(LastResult);
                Phase = GamePhase.WaitingAttack;
                IncomingAttack = false;
                BeginPlayerAttack(target);
                if (!AttackPlaying)
                {
                    _compareCursor++;
                    RunNextCompare();
                }

                return;
            }

            var loss = ComputeAttackDamage(target, targetScore);
            PendingAttackDamage = loss;
            AttackLevel = MapAttackLevel(targetScore.Type);
            LastResult = $"{target?.Name} 的{targetScore.Label}压过你的{openScore.Label}，受到 {loss} 伤害";
            Log(LastResult);
            Phase = GamePhase.WaitingAttack;
            BeginIncomingAttack(target);
            if (!AttackPlaying)
            {
                ApplyDamage(Player, loss, true);
                PendingAttackDamage = 0;
                if (Player.Hp <= 0)
                {
                    FinishSequentialCompare();
                    return;
                }

                _compareCursor++;
                RunNextCompare();
            }
        }

        private int ComputeAttackDamage(SeatState attacker, HandScore score)
        {
            if (attacker == null)
            {
                return 1;
            }

            var mag = HandTypeMagnification(score.Type);
            var relic = attacker.IsPlayer
                ? RelicMultiplier(score)
                : (Run.Affix == BossAffix.Flint ? 0.5f : 1f);
            // BaseChips：亮出三张牌 ChipValue 全加（A=11），加在配置攻击力上再乘倍率。
            return HandEvaluator.ComputeAttackDamage(attacker.Attack, score.BaseChips, mag, relic);
        }

        public static float HandTypeMagnification(HandType type)
        {
            var configType = ToConfigHandType(type);
            foreach (var row in HandScoreConfig.All.Values)
            {
                if (row != null && row.Type == configType)
                {
                    return row.BasicMagnification > 0f ? row.BasicMagnification : 1f;
                }
            }

            switch (type)
            {
                case HandType.Pair: return 2f;
                case HandType.Straight: return 3f;
                case HandType.Flush: return 3.5f;
                case HandType.StraightFlush: return 5f;
                case HandType.ThreeOfAKind: return 6f;
                default: return 1f;
            }
        }

        private static App.Config.HandType ToConfigHandType(HandType type)
        {
            switch (type)
            {
                case HandType.Pair:
                    return App.Config.HandType.Couplet;
                case HandType.ThreeOfAKind:
                    return App.Config.HandType.Leopard;
                default:
                    return (App.Config.HandType)((int)type + 1);
            }
        }

        private void DealPlayerLossDamage(SeatState winner)
        {
            if (winner == null || winner.IsPlayer)
            {
                return;
            }

            var score = EvaluateSeat(winner);
            var damage = HandEvaluator.ComputeDamage(score, ShowdownStake(), 1f);
            ApplyDamage(Player, damage, true);
        }

        private SeatState EnemyAtVisualSlot(int visualSlot)
        {
            var activeCount = 0;
            for (var i = 0; i < Enemies.Length; i++)
            {
                if (Enemies[i].ActiveInStage)
                {
                    activeCount++;
                }
            }

            var placed = 0;
            for (var i = 0; i < Enemies.Length; i++)
            {
                if (!Enemies[i].ActiveInStage)
                {
                    continue;
                }

                if (TableVisualSlot(placed, activeCount) == visualSlot)
                {
                    return Enemies[i];
                }

                placed++;
            }

            return null;
        }

        private static int TableVisualSlot(int enemyIndex, int activeCount)
        {
            if (activeCount <= 1)
            {
                return 1;
            }

            if (activeCount == 2)
            {
                return enemyIndex == 0 ? 0 : 2;
            }

            return enemyIndex;
        }

        public void Buy(string itemId)
        {
            if (Phase != GamePhase.Shop)
            {
                return;
            }

            ShopItemDef item = null;
            for (var i = 0; i < GameBalance.Catalog.Count; i++)
            {
                if (GameBalance.Catalog[i].Id == itemId)
                {
                    item = GameBalance.Catalog[i];
                    break;
                }
            }

            if (item == null || Run.Gold < item.Price)
            {
                Hint = "金币不足";
                Notify();
                return;
            }

            if (item.Relic)
            {
                if (Run.Relics.Contains(item.RelicId))
                {
                    Hint = "已拥有该遗物";
                    Notify();
                    return;
                }

                if (Run.Relics.Count >= GameBalance.MaxRelics)
                {
                    Hint = "遗物槽已满（最多 4 件）";
                    Notify();
                    return;
                }

                var category = GameBalance.CategoryOf(item.RelicId);
                for (var i = 0; i < Run.Relics.Count; i++)
                {
                    if (GameBalance.CategoryOf(Run.Relics[i]) == category &&
                        category != RelicCategory.Combat)
                    {
                        Hint = "该类型遗物只能装备 1 件";
                        Notify();
                        return;
                    }
                }

                Run.Gold -= item.Price;
                Run.Relics.Add(item.RelicId);
                Log($"购入遗物 {item.Name}");
            }
            else
            {
                Run.Gold -= item.Price;
                switch (item.ConsumableId)
                {
                    case ConsumableId.SplashSlash:
                        Run.SplashThisRound = true;
                        break;
                    case ConsumableId.Magnifier:
                        Run.MagnifierThisRound = true;
                        break;
                    case ConsumableId.LoanTicket:
                        Run.LoanTicket = true;
                        break;
                    case ConsumableId.RubCharge:
                        Run.BonusRubCharges++;
                        break;
                    case ConsumableId.XRayCharge:
                        Run.BonusXRayCharges++;
                        break;
                    case ConsumableId.ReplaceCharge:
                        Run.BonusReplaceCharges++;
                        break;
                }

                Log($"购入道具 {item.Name}");
            }

            Hint = $"已购买 {item.Name}";
            Notify();
        }

        /// <summary>下次刷新商店所需金币：首次 <see cref="GameConst.ShopRefreshFirst"/>，之后每次 + <see cref="GameConst.ShopRefreshAfter"/>。</summary>
        public int ShopRefreshCost
        {
            get
            {
                var first = GameConst.IsLoaded ? Math.Max(0, GameConst.Instance.ShopRefreshFirst) : 5;
                var after = GameConst.IsLoaded ? Math.Max(0, GameConst.Instance.ShopRefreshAfter) : 3;
                return first + Math.Max(0, Run.ShopRefreshCount) * after;
            }
        }

        public bool OwnsRelicConfig(int relicId) => Run.RelicConfigIds.Contains(relicId);

        public bool CanRefreshShop =>
            Phase == GamePhase.Shop &&
            Run.Gold >= ShopRefreshCost &&
            HasUnownedRelicConfig();

        public void RefreshShopOffers()
        {
            if (Phase != GamePhase.Shop)
            {
                return;
            }

            if (!HasUnownedRelicConfig())
            {
                Hint = "没有可刷新的遗物";
                Notify();
                return;
            }

            var cost = ShopRefreshCost;
            if (Run.Gold < cost)
            {
                Hint = "金币不足";
                Notify();
                return;
            }

            Run.Gold -= cost;
            Run.ShopRefreshCount++;
            RollShopOffers();
            Log($"刷新商店，花费 {cost} 金币（下次 {ShopRefreshCost}）");
            Hint = $"商店已刷新，下次刷新 {ShopRefreshCost} 金币";
            Notify();
        }

        public void BuyShopRelic(int relicId)
        {
            if (Phase != GamePhase.Shop)
            {
                return;
            }

            if (!Run.ShopOfferIds.Contains(relicId))
            {
                return;
            }

            var relic = RelicConfig.Get(relicId);
            if (relic == null)
            {
                return;
            }

            if (OwnsRelicConfig(relicId))
            {
                Hint = "已拥有该遗物";
                Notify();
                return;
            }

            if (Run.Gold < relic.Price)
            {
                Hint = "金币不足";
                Notify();
                return;
            }

            Run.Gold -= relic.Price;
            Run.RelicConfigIds.Add(relicId);
            Run.ShopOfferIds.Remove(relicId);
            Log($"购入遗物 {relic.Name}");
            Hint = $"已购买 {relic.Name}";
            Notify();
        }

        public void SellShopRelic(int relicId)
        {
            if (Phase != GamePhase.Shop)
            {
                return;
            }

            if (!OwnsRelicConfig(relicId))
            {
                Hint = "未拥有该遗物";
                Notify();
                return;
            }

            var relic = RelicConfig.Get(relicId);
            if (relic == null)
            {
                return;
            }

            Run.RelicConfigIds.Remove(relicId);
            Run.Gold += Math.Max(0, relic.SellingPrice);
            Log($"出售遗物 {relic.Name}，获得 {relic.SellingPrice} 金币");
            Hint = $"已出售 {relic.Name}";
            Notify();
        }

        public void LeaveShop()
        {
            if (Phase != GamePhase.Shop)
            {
                return;
            }

            if (!TryAdvanceLevel())
            {
                Phase = GamePhase.RunComplete;
                if (string.IsNullOrEmpty(Hint) || Hint.Contains("关卡胜利") || Hint.Contains("兑换"))
                {
                    Hint = "你已打完该难度全部关卡！";
                }

                Notify();
                return;
            }

            StartStage(inheritPlayerHp: true);
        }

        public void WatchAdLoan()
        {
            if (Phase != GamePhase.StageFail || Run.AdsLoanThisStage >= 1)
            {
                return;
            }

            Run.AdsLoanThisStage++;
            _loanCourageBonus = Math.Max(_roundBaseBet, GameBalance.MinBet);
            Log("观看广告，借贷获得本回合基础注勇气值");
            ContinueAfterLoan();
        }

        public void WatchAdRevive()
        {
            if (Phase != GamePhase.StageFail || Run.AdsReviveThisStage >= 1)
            {
                return;
            }

            Run.AdsReviveThisStage++;
            Player.Hp = Player.MaxHp;
            HpSvc()?.Revive(Player.Id);
            Log("观看广告复活，生命已回满");
            ContinueAfterLoan();
        }

        public void WatchAdExtraRub()
        {
            if (Run.AdsExtraRubThisStage >= 2)
            {
                Hint = "本关额外搓牌广告已达上限";
                Notify();
                return;
            }

            Run.AdsExtraRubThisStage++;
            Run.BonusRubCharges++;
            Run.PeekGoodCharges++;
            Log("观看广告，本关额外获得 1 次搓牌");
            Hint = $"搓牌次数 +1（剩余 {Run.PeekGoodCharges}，本关还可广告 {2 - Run.AdsExtraRubThisStage} 次）";
            Notify();
        }

        public void WatchAdDoubleGold()
        {
            if (Phase != GamePhase.Shop)
            {
                return;
            }

            if (Run.AdsDoubleGoldToday >= GameBalance.DailyDoubleGoldAds)
            {
                Hint = "今日双倍金币广告已用完";
                Notify();
                return;
            }

            if (Run.DoubleGoldThisStage)
            {
                Hint = "本关已双倍";
                Notify();
                return;
            }

            Run.AdsDoubleGoldToday++;
            Run.DoubleGoldThisStage = true;
            var extra = _shopGoldGranted;
            if (extra > 0)
            {
                Run.Gold += extra;
                _shopGoldGranted += extra;
            }

            Log($"双倍金币结算 +{extra}");
            Hint = extra > 0 ? $"金币翻倍，额外获得 {extra}" : "本关已标记双倍金币";
            Notify();
        }

        public bool HasRelic(RelicId id) => Run.Relics.Contains(id) && Run.DisabledRelic != id;

        /// <summary>牌型倍率 + 花色/牌型遗物；燧石词缀再打五折。</summary>
        public float RelicMultiplier(HandScore score)
        {
            var extra = 0f;
            if (HasRelic(RelicId.CrudeSword))
            {
                extra += 4f;
            }

            if (HasRelic(RelicId.WoodenSword) && score.Type == HandType.Pair)
            {
                extra += 8f;
            }

            if (HasRelic(RelicId.IronSword) && score.Type == HandType.Straight)
            {
                extra += 8f;
            }

            if (HasRelic(RelicId.JadeSword) && score.Type == HandType.Flush)
            {
                extra += 8f;
            }

            if (HasRelic(RelicId.GreedyNecklace) && ContainsSuit(score, Suit.Diamond))
            {
                extra += 3f;
            }

            if (HasRelic(RelicId.GreedyBracelet) && ContainsSuit(score, Suit.Heart))
            {
                extra += 3f;
            }

            if (HasRelic(RelicId.GreedyEarring) && ContainsSuit(score, Suit.Spade))
            {
                extra += 3f;
            }

            if (HasRelic(RelicId.GreedyRing) && ContainsSuit(score, Suit.Club))
            {
                extra += 3f;
            }

            var flint = Run.Affix == BossAffix.Flint ? 0.5f : 1f;
            return (1f + extra) * flint;
        }

        private void GetScoreBan(out Suit? banned, out bool banFaces)
        {
            banned = null;
            banFaces = Run.Affix == BossAffix.BanScoreFace;
            switch (Run.Affix)
            {
                case BossAffix.BanScoreHeart: banned = Suit.Heart; break;
                case BossAffix.BanScoreSpade: banned = Suit.Spade; break;
                case BossAffix.BanScoreDiamond: banned = Suit.Diamond; break;
                case BossAffix.BanScoreClub: banned = Suit.Club; break;
            }
        }

        /// <summary>结算前为敌人锁定 5 选 3 的最大牌型。透视阶段不调用，避免提前抬牌。</summary>
        private void LockBestOpenCardsIfEnemy(SeatState seat)
        {
            if (seat == null || seat.IsPlayer || seat.Hand == null)
            {
                return;
            }

            GetScoreBan(out var banned, out var banFaces);
            HandEvaluator.SelectBestOpen(
                seat.Hand,
                seat.CardSelected,
                GameBalance.CardsDealt(false),
                banned,
                banFaces);
        }

        /// <summary>评估座位牌型。BOSS 禁用花色/人头会先过滤，燧石减半筹码和倍率。</summary>
        public HandScore EvaluateSeat(SeatState seat)
        {
            GetScoreBan(out var banned, out var banFaces);
            var score = HandEvaluator.Evaluate(CollectEvalCards(seat, banned, banFaces), banned, banFaces);
            if (Run.Affix == BossAffix.Flint)
            {
                score = new HandScore(
                    score.Type,
                    Math.Max(1, score.BaseChips / 2),
                    score.Multiplier * 0.5f,
                    score.Keys,
                    score.UsedCards,
                    score.Label + "（燧石）");
            }

            return score;
        }

        private void ResetSkillCharges()
        {
            Run.PeekGoodCharges = GameBalance.SkillRubUses + Run.BonusRubCharges;
            Run.ChaKanGoodCharges = GameBalance.SkillXRayUses + Run.BonusXRayCharges;
            Run.TiHuanGoodCharges = GameBalance.SkillReplaceUses + Run.BonusReplaceCharges;
        }

        /// <summary>开新关：玩家满血读英雄表，通关进下一关时继承残血。怪物血量读关卡配置。BOSS 人格改为 Expert 并随机词缀。</summary>
        private void StartStage(bool inheritPlayerHp)
        {
            Run.AdsLoanThisStage = 0;
            Run.AdsReviveThisStage = 0;
            Run.AdsExtraRubThisStage = 0;
            Run.DoubleGoldThisStage = false;
            Run.PeekSuitUsed = false;
            Run.PeekSuitIndex = -1;
            Run.PeekedSuit = null;
            Run.DisabledRelic = null;
            Run.DisabledConsumable = null;
            Run.ExtraRubCharges = 0;
            Run.Affix = BossAffix.None;
            _stageBetRound = 0;
            _loanCourageBonus = 0;
            _shopGoldGranted = 0;
            if (AppServices.IsReady)
            {
                AppServices.Resolve<IScoreService>().BeginStage();
            }

            ApplyHeroToPlayer(inheritPlayerHp);
            var enemyCount = ApplyLevelEnemies();

            var title = Run.HasBoss
                ? $"第 {Run.Stage} 关 BOSS · {GameBalance.AffixName(Run.Affix)}"
                : $"第 {Run.Stage} 关 · {enemyCount} 名敌人";
            Log(title);
            if (Run.HasBoss)
            {
                Log(GameBalance.AffixDesc(Run.Affix));
            }

            StartRound();
        }

        /// <summary>重置本手状态并发牌，然后直接看牌进入开牌阶段。技能次数每手重置。</summary>
        private void StartRound()
        {
            CardsRevealed = false;
            Pot = 0;
            AdvanceStageBetRound();
            ResetSkillCharges();
            _streetsWithoutRaise = 0;
            _bettingRound = 1;
            _streetHadRaise = false;
            _playerActedThisStreet = false;
            _pendingRubIndex = -1;
            LastResult = string.Empty;
            Run.RubsLeft = 0;
            SelectingOpenTarget = false;
            SelectingXRayTarget = false;
            RevealWinnerId = -1;
            RevealSeatIds.Clear();
            _revealKind = RevealKind.None;
            _pendingAttackTarget = null;
            AttackVisualSlot = -1;
            AttackLevel = 1;
            Run.PeekSuitUsed = false;
            Run.PeekSuitIndex = -1;
            Run.PeekedSuit = null;
            Run.LastRubMessage = string.Empty;
            PendingAttackDamage = 0;
            IncomingAttack = false;
            _sequentialCompare = false;
            _compareQueue.Clear();
            _compareCursor = 0;
            _roundDamageDealt = 0;
            ClearSpyReveal();
            for (var i = 0; i < Run.RubbedReveal.Length; i++)
            {
                Run.RubbedReveal[i] = false;
            }

            ClearRound(Player);
            for (var i = 0; i < Enemies.Length; i++)
            {
                ClearRound(Enemies[i]);
                Enemies[i].Banner = string.Empty;
            }

            BeginRoundCourage();
            _deck = new Deck(_rng);
            DealAll();
            EnterOpenReady();
        }

        /// <summary>每人发牌。玩家和敌人都发 5 张。未上场的敌人不发。一副牌不重复。</summary>
        private void DealAll()
        {
            DealSerial++;
            foreach (var seat in AllSeats())
            {
                ClearSeatHand(seat);
            }

            SyncDeckWithTable();
            foreach (var seat in AllSeats())
            {
                if (!seat.IsPlayer && !seat.Alive)
                {
                    continue;
                }

                var count = GameBalance.CardsDealt(seat.IsPlayer);
                for (var i = 0; i < seat.Hand.Length; i++)
                {
                    if (i >= count)
                    {
                        seat.Hand[i] = default;
                        continue;
                    }

                    if (!_deck.TryDraw(out var card) || !card.IsValid)
                    {
                        SyncDeckWithTable();
                        if (!_deck.TryDraw(out card) || !card.IsValid)
                        {
                            seat.Hand[i] = default;
                            continue;
                        }
                    }

                    seat.Hand[i] = card;
                }
            }
        }

        private static void ClearSeatHand(SeatState seat)
        {
            if (seat == null || seat.Hand == null)
            {
                return;
            }

            for (var i = 0; i < seat.Hand.Length; i++)
            {
                seat.Hand[i] = default;
            }

            seat.ClearCardSelected();
        }

        /// <summary>搓牌换一张。只从牌堆未发牌里抽，不会与桌上已有牌重复。</summary>
        private Card DrawRubCard(Card original)
        {
            SyncDeckWithTable();
            Suit? bannedSuit = null;
            var banFaces = Run.Affix == BossAffix.BanRubFace;
            switch (Run.Affix)
            {
                case BossAffix.BanRubHeart: bannedSuit = Suit.Heart; break;
                case BossAffix.BanRubSpade: bannedSuit = Suit.Spade; break;
                case BossAffix.BanRubDiamond: bannedSuit = Suit.Diamond; break;
                case BossAffix.BanRubClub: bannedSuit = Suit.Club; break;
            }

            var keepSuit = HasRelic(RelicId.MagnetGloves) && _rng.NextDouble() < GameBalance.MagnetKeepSuitChance;
            if (_deck.TryDrawMatching(card => RubCardAllowed(card, original, bannedSuit, banFaces, keepSuit), out var next))
            {
                return next;
            }

            if (keepSuit &&
                _deck.TryDrawMatching(card => RubCardAllowed(card, original, bannedSuit, banFaces, false), out next))
            {
                return next;
            }

            return _deck.TryDraw(out next) ? next : original;
        }

        private static bool RubCardAllowed(
            Card card,
            Card original,
            Suit? bannedSuit,
            bool banFaces,
            bool keepSuit)
        {
            if (!card.IsValid || card.Equals(original))
            {
                return false;
            }

            if (bannedSuit.HasValue && card.Suit == bannedSuit.Value)
            {
                return false;
            }

            if (banFaces && card.IsFace)
            {
                return false;
            }

            return !keepSuit || card.Suit == original.Suit;
        }

        private void SyncDeckWithTable()
        {
            if (_deck == null)
            {
                _deck = new Deck(_rng);
            }

            _deck.RestoreUnused(CollectDealtCards());
        }

        private List<Card> CollectDealtCards()
        {
            var list = new List<Card>(Deck.Size);
            foreach (var seat in AllSeats())
            {
                if (seat?.Hand == null)
                {
                    continue;
                }

                var count = GameBalance.CardsDealt(seat.IsPlayer);
                for (var i = 0; i < count && i < seat.Hand.Length; i++)
                {
                    var card = seat.Hand[i];
                    if (card.IsValid)
                    {
                        list.Add(card);
                    }
                }
            }

            return list;
        }

        /// <summary>发牌后直接看牌，进入开牌/技能阶段。</summary>
        private void EnterOpenReady()
        {
            History.BeginHand(Player.Courage);
            Player.Looked = true;
            Player.Status = "已看牌";
            History.NoteLook();
            ReturnToOpenReady("点选 3 张牌后开牌。可使用技能");
        }

        private void ReturnToOpenReady(string hint)
        {
            Phase = GamePhase.WaitingOpen;
            Player.Looked = true;
            if (!string.IsNullOrEmpty(hint))
            {
                Hint = hint;
            }
            else
            {
                Hint = "点选 3 张牌后开牌。可使用技能";
            }

            Notify();
        }

        /// <summary>发牌后先让玩家选看牌或闷注，并开始记录本手 History。</summary>
        private void EnterLookChoice()
        {
            EnterOpenReady();
        }

        /// <summary>每轮所有人下完后，未看牌则再次选择闷注或看牌；已看牌则进入跟注/加注。</summary>
        private void OfferLookOrBlindForStreet()
        {
            if (Player.Looked || Player.Folded || IsAllIn(Player))
            {
                Phase = GamePhase.Betting;
                Player.Status = "待下注";
                Hint = $"第 {_bettingRound} 轮下注，基础注 {_roundBaseBet}。请跟注、加注或弃牌";
                Notify();
                return;
            }

            OfferLookOrBlind($"第 {_bettingRound} 轮：你尚未看牌，请选择闷注或看牌。基础注 {_roundBaseBet}");
        }

        private void OfferLookOrBlind(string hint)
        {
            Phase = GamePhase.WaitingLookChoice;
            Player.Status = "待选择";
            Hint = hint;
            Notify();
        }

        private void LeaveRubIfNeeded()
        {
            if (Phase != GamePhase.WaitingRub)
            {
                return;
            }

            _pendingRubIndex = -1;
            Run.RubsLeft = 0;
            Phase = GamePhase.WaitingOpen;
        }

        private void EnterBetting()
        {
            Phase = GamePhase.Betting;
            Player.Status = "待下注";
            Hint = string.IsNullOrEmpty(Run.LastRubMessage)
                ? string.Empty
                : Run.LastRubMessage + "。";
            Hint += Run.Tilted
                ? "心态崩了：本局最大下注为当前血量 50%。请跟注或加注"
                : Player.Looked
                    ? "看牌后请跟注、x2/x4下注、全下或弃牌。可用搓牌技能替换一张牌"
                    : "请跟注、x2/x4下注、全下或弃牌。你闷着时只需付看牌玩家的一半";

            if (Run.MagnifierThisRound && !Run.PeekSuitUsed)
            {
                Hint += "。点击一张手牌可偷看花色";
            }

            Notify();
        }

        /// <summary>玩家跟注或加注。跟满后调用 <see cref="ResolveAiStreet"/>。</summary>
        private void PlacePlayerBet(bool raise, int raiseMult = GameBalance.RaiseLowMult)
        {
            if (Player.Folded)
            {
                return;
            }

            SelectingOpenTarget = false;
            SelectingXRayTarget = false;

            var units = raise ? RaiseUnits(raiseMult) : CurrentRoundUnits();
            units = Clamp(units, _roundBaseBet, MaxBetUnits());
            if (raise && units <= _maxStreetUnits)
            {
                units = Math.Min(MaxBetUnits(), RaiseUnits(raiseMult));
            }

            var facingRaise = !raise && Player.StreetUnits < CurrentRoundUnits();
            var stackBefore = Player.Courage;
            if (!TryCommitUnits(Player, units, out var paid))
            {
                Hint = "勇气值不足";
                Notify();
                return;
            }

            if (raise && Player.StreetUnits >= units)
            {
                ApplyRaisedCall(units);
            }

            _playerActedThisStreet = true;
            History.NotePlayerBet(raise, Player.Looked, paid, stackBefore, facingRaise);
            if (Player.Courage == 0 && paid > 0)
            {
                Player.Status = raise ? $"全下加注 {paid}" : $"全下 {paid}";
            }
            else
            {
                Player.Status = raise
                    ? (Player.Looked ? $"看牌加注 {paid}" : $"闷加注 {paid}")
                    : (Player.Looked ? $"看牌下注 {paid}" : $"闷注 {paid}");
            }
            Log($"{Player.Status}，奖池 {Pot}");
            if (!IsAllIn(Player) && Player.StreetUnits < CurrentRoundUnits())
            {
                Hint = "有人加注，请跟注、再加注或弃牌";
                Notify();
                return;
            }

            ResolveAiStreet();
        }

        /// <summary>
        /// AI 行动街。每位敌人先显示「操作中」，1 秒后再跟/加/弃，形成思考间隔。
        /// </summary>
        private void ResolveAiStreet()
        {
            _aiStreetActive = true;
            _aiPass = 0;
            _aiCursor = 0;
            _aiPendingOpen = false;
            BeginNextAiStep();
        }

        /// <summary>UI 在思考延迟结束后调用，执行当前敌人的实际操作。</summary>
        public void AdvanceAiAction()
        {
            if (!AiActing)
            {
                return;
            }

            var ai = FindEnemyById(ActingAiId);
            AiActing = false;
            ActingAiId = -1;
            if (ai == null || !ai.Alive || ai.Folded)
            {
                _aiCursor++;
                BeginNextAiStep();
                return;
            }

            var scare = HasRelic(RelicId.ScareMask);
            if (_aiPendingOpen)
            {
                _aiPendingOpen = false;
                if (CanAffordOpen(ai) && ForceOpen(ai, Player))
                {
                    StopAiStreet();
                    return;
                }

                _aiCursor++;
                BeginNextAiStep();
                return;
            }

            if (DecideAi(ai, scare))
            {
                StopAiStreet();
                return;
            }

            if (_needAiRescan)
            {
                _needAiRescan = false;
                _aiCursor = 0;
                _aiPass = 0;
            }
            else
            {
                _aiCursor++;
            }

            BeginNextAiStep();
        }

        private void BeginNextAiStep()
        {
            while (_aiPass < 6)
            {
                if (CountInHand() <= 1)
                {
                    StopAiStreet();
                    AwardUncontestedAndSettle();
                    return;
                }

                for (; _aiCursor < Enemies.Length; _aiCursor++)
                {
                    var ai = Enemies[_aiCursor];
                    if (!ai.Alive || ai.Folded || IsAllIn(ai))
                    {
                        continue;
                    }

                    if (ai.StreetUnits < CurrentRoundUnits())
                    {
                        QueueAiThink(ai, false);
                        return;
                    }
                }

                _aiCursor = 0;
                _aiPass++;
                if (AllNonPlayerMatched())
                {
                    break;
                }
            }

            StopAiStreet();
            FinishStreetOrShowdown();
        }

        private void QueueAiThink(SeatState ai, bool pendingOpen)
        {
            _aiPendingOpen = pendingOpen;
            ActingAiId = ai.Id;
            AiActing = true;
            ai.Status = "操作中";
            Hint = $"{ai.Name} 操作中…";
            Notify();
        }

        private void StopAiStreet()
        {
            _aiStreetActive = false;
            AiActing = false;
            ActingAiId = -1;
            _aiPendingOpen = false;
            _needAiRescan = false;
        }

        private SeatState FindEnemyById(int id)
        {
            for (var i = 0; i < Enemies.Length; i++)
            {
                if (Enemies[i].Id == id)
                {
                    return Enemies[i];
                }
            }

            return null;
        }

        /// <summary>执行一次 AI 决策。开牌会立刻进入单挑亮牌；否则跟/加/全下/弃。第一轮禁止弃牌。</summary>
        private bool DecideAi(SeatState ai, bool scare)
        {
            var decision = BuildAiDecision(ai, scare, true, true);
            if (decision.Action == AiAction.Fold &&
                (_bettingRound <= 1 || ai.StreetUnits < CurrentRoundUnits()))
            {
                decision = new AiDecision(
                    AiAction.Call,
                    decision.WinRate,
                    _bettingRound <= 1 ? "第一轮不能弃牌" : "跟上加注");
            }

            switch (decision.Action)
            {
                case AiAction.Fold:
                    FoldSeat(ai, "弃牌");
                    Log($"{ai.Name} 弃牌（{decision.Reason}）");
                    return FinishIfOneLeft();

                case AiAction.Open:
                    if (CanAffordOpen(ai))
                    {
                        return ForceOpen(ai, Player);
                    }

                    break;
            }

            var roundUnits = CurrentRoundUnits();
            if (ai.Courage < CallCost(ai))
            {
                if (ai.Courage <= 0)
                {
                    FoldSeat(ai, "勇气值不足，弃牌");
                    Log($"{ai.Name} 勇气值不足，弃牌");
                    return FinishIfOneLeft();
                }

                if (!TryCommitUnits(ai, roundUnits, out var shortPaid))
                {
                    if (_bettingRound <= 1)
                    {
                        return false;
                    }

                    FoldSeat(ai, "勇气值不足，弃牌");
                    return FinishIfOneLeft();
                }

                ai.Status = $"全下 {shortPaid}";
                Log($"{ai.Name} 全下 {shortPaid}（{decision.Reason}）");
                return false;
            }

            var target = roundUnits;
            var allIn = decision.Action == AiAction.AllIn;
            if (allIn)
            {
                target = roundUnits;
            }
            else if (decision.Action == AiAction.Raise)
            {
                TryAiLookForRaise(ai);
                target = SizeAiRaise(ai, decision.WinRate);
            }

            if (target < roundUnits)
            {
                if (_bettingRound <= 1)
                {
                    target = roundUnits;
                }
                else
                {
                    FoldSeat(ai, "无法跟注，弃牌");
                    Log($"{ai.Name} 无法跟注，弃牌");
                    return FinishIfOneLeft();
                }
            }

            if (TryCommitUnits(ai, target, out var paid))
            {
                var raised = !allIn && target > _maxStreetUnits && ai.StreetUnits >= target;
                if (raised)
                {
                    ApplyRaisedCall(target);
                    ai.Status = paid > 0 ? $"加注 {paid}" : "加注";
                }
                else
                {
                    ai.Status = paid > 0 ? $"跟注 {paid}" : "跟注";
                }

                if (allIn && ai.Courage > 0)
                {
                    var rest = ai.Courage;
                    SpendCourage(ai, rest);
                    ai.TotalBet += rest;
                    ai.StreetPaid += rest;
                    Pot += rest;
                    ai.Status = $"全下 {paid + rest}";
                }

                Log($"{ai.Name} {ai.Status}（{decision.Reason}）");
                return false;
            }

            FoldSeat(ai, "弃牌");
            Log($"{ai.Name} 弃牌");
            return FinishIfOneLeft();
        }

        /// <summary>加注时尽量看牌（看牌价是闷注的两倍），加不起则保持闷加。</summary>
        private void TryAiLookForRaise(SeatState ai)
        {
            if (ai == null || ai.Looked)
            {
                return;
            }

            ai.Looked = true;
            if (UnitsAffordable(ai) >= RaiseUnits())
            {
                Log($"{ai.Name} 看牌加注");
                return;
            }

            ai.Looked = false;
        }

        /// <summary>加注尺寸：基础注×2 或 ×3；牌力高走 ×3。</summary>
        private int SizeAiRaise(SeatState ai, float winRate)
        {
            var mult = winRate >= 0.62f ? GameBalance.RaiseHighMult : GameBalance.RaiseLowMult;
            var target = RaiseUnits(mult);
            var affordable = UnitsAffordable(ai);
            if (affordable < target)
            {
                return CurrentRoundUnits();
            }

            return target;
        }

        /// <summary>有效筹码 = min(自己, 最短仍在手对手)，一手最多能赢这么多。</summary>
        private int ComputeEffectiveStack(SeatState ai)
        {
            var minOpp = int.MaxValue;
            foreach (var seat in AllSeats())
            {
                if (seat == ai || !Participates(seat) || seat.Folded)
                {
                    continue;
                }

                if (seat.Courage < minOpp)
                {
                    minOpp = seat.Courage;
                }
            }

            if (minOpp == int.MaxValue)
            {
                return Math.Max(0, ai.Courage);
            }

            return Math.Max(0, Math.Min(ai.Courage, minOpp));
        }

        /// <summary>组装 <see cref="AiContext"/>：蒙特卡洛胜率、跟注成本、读玩家线，再交给 AiBrain。</summary>
        private AiDecision BuildAiDecision(SeatState ai, bool scare, bool canRaise, bool canAllIn)
        {
            var score = EvaluateSeat(ai);
            var visible = CollectVisibleDeadCards(ai);
            var opponents = Math.Max(1, CountInHand() - 1);
            var winRate = ZhaJinHuaOdds.EstimateWinRate(ai.Hand, visible, opponents, _rng);
            var callCost = CallCost(ai);
            var shortest = int.MaxValue;
            var remainingAi = 0;
            var position = 0;
            for (var i = 0; i < Enemies.Length; i++)
            {
                var seat = Enemies[i];
                if (!seat.Alive || seat.Folded)
                {
                    continue;
                }

                if (seat == ai)
                {
                    position = remainingAi;
                }

                remainingAi++;
            }

            foreach (var seat in AllSeats())
            {
                if (seat == ai || !Participates(seat) || seat.Folded)
                {
                    continue;
                }

                if (seat.Courage < shortest)
                {
                    shortest = seat.Courage;
                }
            }

            if (shortest == int.MaxValue)
            {
                shortest = ai.Courage;
            }

            var effective = Math.Min(ai.Courage, shortest);
            var playerBb = Player.Courage / (float)Math.Max(1, GameBalance.MinBet);
            var playerStrength = Player.Folded ? 0.30f : History.EstimateStrength(Player.Courage, GameBalance.MinBet);
            return AiBrain.Decide(new AiContext
            {
                Ai = ai,
                Score = score,
                WinRate = winRate,
                StraightFlushDraw = ZhaJinHuaOdds.IsStraightFlushDraw(ai.Hand),
                Pot = Pot,
                CallCost = callCost,
                AiChips = ai.Courage,
                EffectiveStack = Math.Max(0, effective),
                ShortestOpponent = Math.Max(0, shortest),
                PlayerChips = Player.Courage,
                BigBlind = GameBalance.MinBet,
                BettingRound = _bettingRound,
                RemainingPlayers = opponents + 1,
                PositionAmongAi = position,
                RemainingAiCount = remainingAi,
                Scare = scare,
                CanOpen = CanAffordOpen(ai),
                CanRaise = canRaise && UnitsAffordable(ai) >= RaiseUnits(),
                CanAllIn = canAllIn,
                History = History,
                Rng = _rng,
                PlayerFolded = Player.Folded,
                PlayerLooked = Player.Looked,
                PlayerStreetCalls = History.StreetCalls,
                PlayerLookedCalls = History.LookedCalls,
                PlayerBlindCalls = History.BlindCalls,
                PlayerConsecutiveCalls = History.ConsecutiveCalls,
                PlayerConsecutiveBlindCalls = History.ConsecutiveBlindCalls,
                PlayerHandRaises = History.HandRaises,
                PlayerCalledFacingRaise = History.CalledFacingRaise,
                PlayerOnlyCalled = History.OnlyCalledThisHand,
                PlayerDeep = playerBb > 40f,
                PlayerShort = playerBb < 10f,
                PlayerMaxCallStackFrac = History.MaxCallStackFrac,
                PlayerStrength = playerStrength
            });
        }

        /// <summary>已亮出的手牌当作死牌，从 AI 胜率抽样里剔除。</summary>
        private List<Card> CollectVisibleDeadCards(SeatState hero)
        {
            var list = new List<Card>();
            foreach (var seat in AllSeats())
            {
                if (seat == hero || !seat.ShowCards || seat.Hand == null)
                {
                    continue;
                }

                for (var i = 0; i < seat.Hand.Length; i++)
                {
                    list.Add(seat.Hand[i]);
                }
            }

            return list;
        }

        /// <summary>
        /// 一街结束：未跟满的 AI 弃牌；连续两街同注且无人加注则亮牌；
        /// 否则进入下一街。玩家已全下时，未全下的对手可继续边池。
        /// </summary>
        private void FinishStreetOrShowdown()
        {
            var alive = CountInHand();
            if (alive <= 1)
            {
                AwardUncontestedAndSettle();
                return;
            }

            if (!IsAllIn(Player) && !Player.Folded && Player.StreetUnits < CurrentRoundUnits())
            {
                Hint = "有人加注，请跟注、加注、开牌或弃牌";
                Notify();
                return;
            }

            var roundUnits = CurrentRoundUnits();
            for (var i = 0; i < Enemies.Length; i++)
            {
                var ai = Enemies[i];
                if (IsAllIn(ai) || !ai.Alive || ai.Folded)
                {
                    continue;
                }

                if (ai.StreetUnits < roundUnits)
                {
                    if (TryCommitUnits(ai, roundUnits, out var paid))
                    {
                        ai.Status = paid > 0 ? $"跟注 {paid}" : "跟注";
                        Log($"{ai.Name} {ai.Status}（跟上加注）");
                    }
                    else if (ai.Hp <= 0)
                    {
                        FoldSeat(ai, "未跟注，弃牌");
                        Log($"{ai.Name} 未跟上当轮注额，弃牌");
                    }
                }
            }

            alive = CountInHand();
            if (alive <= 1)
            {
                AwardUncontestedAndSettle();
                return;
            }

            if (!AllNonPlayerMatched())
            {
                if (!IsAllIn(Player) && !Player.Folded)
                {
                    Hint = "等待跟注";
                    Notify();
                    return;
                }
            }

            if (!StreetBettingComplete())
            {
                Notify();
                return;
            }

            if (ShouldImmediateShowdown())
            {
                Hint = "投注已全部完成，全员亮牌摊牌";
                Showdown();
                return;
            }

            if (!_streetHadRaise && AllActiveBetsEqual())
            {
                _streetsWithoutRaise++;
            }
            else
            {
                _streetsWithoutRaise = 0;
            }

            if (_streetsWithoutRaise >= 2)
            {
                Hint = "连续两轮下注相同且无人加注，进入亮牌";
                Showdown();
                return;
            }

            _streetHadRaise = false;
            _playerActedThisStreet = false;
            var lastUnits = Math.Max(CurrentRoundUnits(), _roundBaseBet);
            foreach (var seat in AllSeats())
            {
                seat.StreetUnits = 0;
                seat.StreetPaid = 0;
            }

            _roundBaseBet = lastUnits + GameBalance.BaseBetStep;
            _betStep = _roundBaseBet;
            _maxStreetUnits = _roundBaseBet;
            BetUnits = _roundBaseBet;
            _bettingRound++;
            if (IsAllIn(Player) || Player.Folded)
            {
                Hint = $"第 {_bettingRound} 轮边池下注，基础注 {_roundBaseBet}。你已{(Player.Folded ? "弃牌" : "全下")}，对手继续。";
                ResolveAiStreet();
                return;
            }

            OfferLookOrBlindForStreet();
        }

        private void AdvanceStageBetRound()
        {
            _stageBetRound++;
            _roundBaseBet = GameBalance.BaseBetForRound(_stageBetRound);
            _betStep = _roundBaseBet;
            _maxStreetUnits = _roundBaseBet;
            BetUnits = _roundBaseBet;
        }

        private int NextRoundBaseBet()
        {
            return GameBalance.BaseBetForRound(_stageBetRound + 1);
        }

        /// <summary>加注成功后，本街基础跟注抬到加注额，后续跟注都按这个档。</summary>
        private void ApplyRaisedCall(int units)
        {
            _maxStreetUnits = Math.Max(_maxStreetUnits, units);
            _betStep = Math.Max(_betStep, units);
            BetUnits = _betStep;
            _streetHadRaise = true;
            _needAiRescan = true;
        }

        /// <summary>加注目标 = 当前跟注档 + 本回合基础注 ×2 或 ×3。</summary>
        private int RaiseUnits(int multiplier = GameBalance.RaiseLowMult)
        {
            var mult = multiplier < GameBalance.RaiseLowMult ? GameBalance.RaiseLowMult : multiplier;
            return CurrentRoundUnits() + _roundBaseBet * mult;
        }

        /// <summary>开牌要付当前注额的双倍。</summary>
        private int OpenUnits() => Math.Max(CurrentRoundUnits(), _betStep) * 2;

        /// <summary>本街需要跟上的档位 = 加注后的基础跟注，以及各未弃牌座位的 StreetUnits。</summary>
        private int CurrentRoundUnits()
        {
            var max = Math.Max(_betStep, _maxStreetUnits);
            foreach (var seat in AllSeats())
            {
                if (!Participates(seat) || seat.Folded)
                {
                    continue;
                }

                if (seat.StreetUnits > max)
                {
                    max = seat.StreetUnits;
                }
            }

            return max;
        }

        private int ShowdownStake() => Math.Max(GameBalance.MinBet, CurrentRoundUnits());

        private bool CanAffordOpen(SeatState seat)
        {
            return CallCost(seat, OpenUnits()) <= seat.Courage;
        }

        private int CallCost(SeatState seat, int units = -1)
        {
            if (units < 0)
            {
                units = CurrentRoundUnits();
            }

            units = Math.Max(units, seat.StreetUnits);
            var cost = CostFor(seat, units) - CostFor(seat, seat.StreetUnits);
            if (Run.Affix == BossAffix.AntiRaise && seat.IsPlayer)
            {
                cost = (int)Math.Ceiling(cost * 1.5f);
            }

            return cost;
        }

        /// <summary>开牌单挑：扣开牌费后立刻亮双方牌，输家出局。</summary>
        private bool ForceOpen(SeatState opener, SeatState target)
        {
            if (opener == null || target == null || opener.Folded || target.Folded)
            {
                return false;
            }

            if (!TryCommitUnits(opener, OpenUnits(), out var paid))
            {
                Hint = $"{opener.Name} 开牌失败：勇气值不足";
                Notify();
                return false;
            }

            LockBestOpenCardsIfEnemy(opener);
            LockBestOpenCardsIfEnemy(target);
            var openScore = EvaluateSeat(opener);
            var targetScore = EvaluateSeat(target);
            _pendingOpenerWins = openScore.CompareTo(targetScore) > 0;
            _pendingOpener = opener;
            _pendingOpenTarget = target;
            _pendingWinner = _pendingOpenerWins ? opener : target;
            _pendingBest = _pendingOpenerWins ? openScore : targetScore;
            opener.Status = $"开牌 {paid}";
            Log($"{opener.Name} 开牌单挑 {target.Name}（{paid}）");

            BeginRevealPlay(RevealKind.OpenDuel, BuildDuelRevealOrder(opener, target), _pendingWinner);
            Hint = $"{opener.Name} 开牌单挑 {target.Name}！";
            LastResult = $"{opener.Name} vs {target.Name}";
            Notify();
            return true;
        }

        /// <summary>玩家弃牌或全下后，若桌上还剩多人则 AI 继续打边池。</summary>
        private void SettleAfterPlayerOut()
        {
            Run.ConsecutiveLosses++;
            if (Run.ConsecutiveLosses >= 2)
            {
                Run.Tilted = true;
                Log("心态崩了：下一局最大下注限制为当前勇气值 50%");
            }

            if (CountInHand() <= 1)
            {
                AwardUncontestedAndSettle();
                return;
            }

            if (ShouldImmediateShowdown())
            {
                Showdown();
                return;
            }

            Hint = "未全下的对手继续下注，形成边池";
            ResolveAiStreet();
        }

        private void RevealHand(SeatState seat)
        {
            if (seat == null || seat.Hand == null)
            {
                return;
            }

            seat.ShowCards = true;
            var score = EvaluateSeat(seat);
            if (!string.IsNullOrEmpty(score.Label))
            {
                seat.Banner = seat.Folded ? $"弃牌 {score.Label}" : score.Label;
            }
        }

        private void RevealAllHands()
        {
            CardsRevealed = true;
            foreach (var seat in AllSeats())
            {
                if (!Participates(seat) || seat.Hand == null)
                {
                    continue;
                }

                RevealHand(seat);
            }
        }

        private void FoldSeat(SeatState seat, string status)
        {
            seat.Folded = true;
            seat.Status = status;
            RevealHand(seat);
            if (!seat.IsPlayer)
            {
                var score = EvaluateSeat(seat);
                Hint = CountOpponentsInHand() > 0
                    ? $"{seat.Name} 弃牌亮牌（{score.Label}），其余对手继续"
                    : $"{seat.Name} 弃牌，亮出 {score.Label}";
                Notify();
            }
        }

        private bool FinishIfOneLeft()
        {
            if (CountInHand() <= 1)
            {
                AwardUncontestedAndSettle();
                return true;
            }

            Notify();
            return false;
        }

        private bool AllNonPlayerMatched()
        {
            for (var i = 0; i < Enemies.Length; i++)
            {
                var ai = Enemies[i];
                if (ai.Alive && !ai.Folded && !IsAllIn(ai) && ai.StreetUnits < CurrentRoundUnits())
                {
                    return false;
                }
            }

            return true;
        }

        private void ResolveAfterPlayerFold()
        {
            foreach (var seat in AllSeats())
            {
                if (!seat.IsPlayer && Participates(seat) && HasHand(seat))
                {
                    RevealHand(seat);
                }
            }

            Hint = "你已弃牌，对手亮牌，由牌型最高者发动攻击";
            Showdown();
        }

        /// <summary>比牌：按牌型决胜负，赢家收池，玩家赢则进入点选攻击。</summary>
        private void Showdown()
        {
            if (_revealKind != RevealKind.None)
            {
                return;
            }

            Phase = GamePhase.Showdown;
            SelectingOpenTarget = false;

            if (Run.Affix == BossAffix.XRay)
            {
                var peek = EvaluateSeat(Player);
                Log($"透视眼：BOSS 偷看了你的牌型「{peek.Label}」");
            }

            SeatState winner = null;
            HandScore best = default;
            var first = true;
            foreach (var seat in AllSeats())
            {
                if (!Participates(seat) || seat.Folded)
                {
                    continue;
                }

                LockBestOpenCardsIfEnemy(seat);
                var score = EvaluateSeat(seat);
                if (first || score.CompareTo(best) > 0)
                {
                    best = score;
                    winner = seat;
                    first = false;
                }
            }

            if (winner == null)
            {
                LastResult = "无人亮牌";
                AfterRound();
                return;
            }

            _pendingWinner = winner;
            _pendingBest = best;
            BeginRevealPlay(RevealKind.Showdown, BuildShowdownRevealOrder(), winner);
            Hint = CountPlayersWhoCanBet() <= 1
                ? "全下后投注结束，立刻亮牌摊牌！"
                : "全员亮牌，按炸金花规则比大小！";
            Notify();
        }

        private void ApplyShowdownSettlement()
        {
            var winner = _pendingWinner;
            var best = _pendingBest;
            RevealAllHands();
            if (winner == null)
            {
                AfterRound();
                return;
            }

            var potSnap = Pot;
            AwardPots();
            var playerScore = EvaluateSeat(Player);
            var playerWin = winner.IsPlayer;
            LastResult = FormatShowdownLine(winner, best, playerScore, playerWin, potSnap);
            Log(LastResult);

            if (playerWin)
            {
                AwardPlayerRoundScore(potSnap);
                Run.ConsecutiveLosses = 0;
                Run.Tilted = false;
                var relicMult = RelicMultiplier(best);
                PendingAttackDamage = HandEvaluator.ComputeDamage(best, ShowdownStake(), relicMult);
                AttackLevel = MapAttackLevel(best.Type);
                ApplyBankruptcy(true, winner);
                Pot = 0;
                EnterPlayerAttack($"{HandDrama(best.Type)}！你赢了，造成 {PendingAttackDamage} 伤害");
                return;
            }

            Run.ConsecutiveLosses++;
            if (Run.ConsecutiveLosses >= 2)
            {
                Run.Tilted = true;
                Log("心态崩了：下一局最大下注限制为当前勇气值 50%");
            }

            DealPlayerLossDamage(winner);
            ApplyBankruptcy(false, winner);
            Pot = 0;
            Hint = LastResult;
            AfterRound();
        }

        private void ApplyOpenDuelSettlement()
        {
            if (_sequentialCompare)
            {
                ResolveSequentialDuel();
                return;
            }
            var opener = _pendingOpener;
            var target = _pendingOpenTarget;
            if (opener != null)
            {
                RevealHand(opener);
            }

            if (target != null)
            {
                RevealHand(target);
            }

            var openScore = opener != null ? EvaluateSeat(opener) : default;
            var targetScore = target != null ? EvaluateSeat(target) : default;
            if (_pendingOpenerWins)
            {
                if (target != null)
                {
                    target.Folded = true;
                    target.Status = "开牌失败";
                    target.Banner = targetScore.Label;
                }

                LastResult = $"{opener?.Name} 开牌胜出！{openScore.Label} 压过 {targetScore.Label}";
                Log(LastResult);
                if (target != null && target.IsPlayer)
                {
                    DealPlayerLossDamage(opener);
                    SettleAfterPlayerOut();
                    return;
                }
            }
            else
            {
                if (opener != null)
                {
                    opener.Folded = true;
                    opener.Status = "开牌失败";
                    opener.Banner = openScore.Label;
                }

                LastResult = $"{target?.Name} 扛住开牌！{targetScore.Label} 压过 {openScore.Label}";
                Log(LastResult);
                if (opener != null && opener.IsPlayer)
                {
                    DealPlayerLossDamage(target);
                    SettleAfterPlayerOut();
                    return;
                }
            }

            if (CountInHand() <= 1)
            {
                Showdown();
                return;
            }

            Phase = GamePhase.Betting;
            Hint = LastResult + "。继续下注";
            FinishStreetOrShowdown();
        }

        private void BeginRevealPlay(RevealKind kind, List<int> order, SeatState winner)
        {
            _revealKind = kind;
            RevealSeatIds.Clear();
            if (order != null)
            {
                for (var i = 0; i < order.Count; i++)
                {
                    RevealSeatIds.Add(order[i]);
                }
            }

            RevealWinnerId = winner != null ? winner.Id : -1;
            RevealPlaySerial++;
            Phase = GamePhase.Showdown;
        }

        private List<int> BuildShowdownRevealOrder()
        {
            var ids = new List<int>();
            if (HasHand(Player))
            {
                ids.Add(Player.Id);
            }

            for (var i = 0; i < Enemies.Length; i++)
            {
                var ai = Enemies[i];
                if (Participates(ai) && ai.Folded && HasHand(ai))
                {
                    ids.Add(ai.Id);
                }
            }

            for (var i = 0; i < Enemies.Length; i++)
            {
                var ai = Enemies[i];
                if (Participates(ai) && !ai.Folded && HasHand(ai))
                {
                    ids.Add(ai.Id);
                }
            }

            return ids;
        }

        private static bool HasHand(SeatState seat)
        {
            return seat != null && seat.Hand != null && seat.Hand.Length > 0;
        }

        private static Card[] CollectEvalCards(SeatState seat, Suit? banned, bool banFaces)
        {
            if (seat == null || seat.Hand == null)
            {
                return Array.Empty<Card>();
            }

            if (seat.CountSelectedCards() == GameBalance.OpenHandSize)
            {
                return HandEvaluator.CopySelectedCards(seat.Hand, seat.CardSelected);
            }

            if (!seat.IsPlayer)
            {
                return HandEvaluator.CopyBestOpenCards(
                    seat.Hand,
                    GameBalance.CardsDealt(false),
                    banned,
                    banFaces);
            }

            return HandEvaluator.CopySelectedCards(seat.Hand, seat.CardSelected);
        }

        private string FormatPlayerHand()
        {
            var parts = new string[GameBalance.PlayerCardsDealt];
            for (var i = 0; i < parts.Length; i++)
            {
                parts[i] = Player.Hand[i].DisplayName;
            }

            return string.Join(" / ", parts);
        }

        private static int SpyKey(int seatId, int cardIndex)
        {
            if (seatId < 0 || cardIndex < 0 || cardIndex >= GameBalance.MaxCardsPerSeat)
            {
                return -1;
            }

            return seatId * GameBalance.MaxCardsPerSeat + cardIndex;
        }

        private void SetSpyReveal(int seatId, int cardIndex, bool value)
        {
            var key = SpyKey(seatId, cardIndex);
            if (key >= 0 && key < Run.SpyReveal.Length)
            {
                Run.SpyReveal[key] = value;
            }
        }

        private void ClearSpyReveal()
        {
            for (var i = 0; i < Run.SpyReveal.Length; i++)
            {
                Run.SpyReveal[i] = false;
            }
        }

        private List<int> BuildDuelRevealOrder(SeatState opener, SeatState target)
        {
            var ids = new List<int>();
            if (opener != null && !opener.IsPlayer)
            {
                ids.Add(opener.Id);
            }

            if (target != null && !target.IsPlayer && (opener == null || target.Id != opener.Id))
            {
                ids.Add(target.Id);
            }

            if (opener != null && opener.IsPlayer)
            {
                ids.Add(opener.Id);
            }
            else if (target != null && target.IsPlayer)
            {
                ids.Add(target.Id);
            }

            return ids;
        }

        private SeatState SeatById(int id)
        {
            if (Player.Id == id)
            {
                return Player;
            }

            for (var i = 0; i < Enemies.Length; i++)
            {
                if (Enemies[i].Id == id)
                {
                    return Enemies[i];
                }
            }

            return null;
        }

        private int CountRemainingAi()
        {
            var n = 0;
            for (var i = 0; i < Enemies.Length; i++)
            {
                if (Enemies[i].Alive && !Enemies[i].Folded)
                {
                    n++;
                }
            }

            return n;
        }

        private string FormatShowdownLine(SeatState winner, HandScore best, HandScore playerScore, bool playerWin, int potAmount)
        {
            if (playerWin)
            {
                return $"{HandDrama(best.Type)}！你的{best.Label}赢下奖池 {potAmount}";
            }

            if (Player.Folded)
            {
                return $"{winner.Name} 的{best.Label}收走奖池 {potAmount}";
            }

            return $"{winner.Name} 的{best.Label}压过你的{playerScore.Label}";
        }

        private static string HandDrama(HandType type)
        {
            switch (type)
            {
                case HandType.ThreeOfAKind: return "豹子炸场";
                case HandType.StraightFlush: return "顺金通杀";
                case HandType.Flush: return "金花亮出";
                case HandType.Straight: return "顺子连上";
                case HandType.Pair: return "对子对撞";
                default: return "散牌拼点";
            }
        }

        /// <summary>散牌/对子为低，顺子/金花为中，顺金/豹子为高。</summary>
        private static int MapAttackLevel(HandType type)
        {
            switch (type)
            {
                case HandType.StraightFlush:
                case HandType.ThreeOfAKind:
                    return 3;
                case HandType.Straight:
                case HandType.Flush:
                    return 2;
                default:
                    return 1;
            }
        }

        public void Continue()
        {
            if (Phase == GamePhase.WaitingAttack)
            {
                if (AttackPlaying)
                {
                    CompletePlayerAttack();
                    return;
                }

                if (_sequentialCompare)
                {
                    RunNextCompare();
                    return;
                }

                var target = FirstAliveEnemy();
                if (target != null)
                {
                    BeginPlayerAttack(target);
                }
                else
                {
                    AfterRound();
                }

                return;
            }

            if (Phase != GamePhase.RoundSettle)
            {
                return;
            }

            if (!AnyEnemyAlive())
            {
                EnterShop();
                return;
            }

            Run.MagnifierThisRound = false;
            StartRound();
        }

        private int ApplyDamage(SeatState target, int damage, bool main)
        {
            if (target == null)
            {
                return 0;
            }

            var dealt = Math.Min(target.Hp, Math.Max(1, damage));
            target.Hp -= dealt;
            HpSvc()?.Damage(target.Id, dealt);
            target.Banner = main ? $"-{dealt}" : $"溅射 -{dealt}";
            Log($"攻击 {target.Name} {dealt}，剩余 HP {target.Hp}");
            if (target.Hp <= 0)
            {
                target.Hp = 0;
                target.Status = "阵亡";
                Log($"击杀 {target.Name}");
            }

            return dealt;
        }

        private void ApplyBankruptcy(bool playerWon, SeatState winner)
        {
            var ratio = Run.Affix == BossAffix.Stingy ? GameBalance.StingyRescueRatio : GameBalance.RescueRatio;
            for (var i = 0; i < Enemies.Length; i++)
            {
                var ai = Enemies[i];
                if (!ai.ActiveInStage || ai.Hp <= 0)
                {
                    continue;
                }

                if (ai.Hp >= GameBalance.MinBet)
                {
                    continue;
                }

                if (playerWon)
                {
                    var remainHp = ai.Hp;
                    ai.Hp = 0;
                    ai.Courage = 0;
                    ai.CourageStake = 0;
                    var hp = HpSvc();
                    if (hp != null)
                    {
                        hp.Damage(ai.Id, remainHp);
                    }

                    CourageSvc()?.Lose(ai.Id);
                    ai.Status = "斩杀";
                    ai.Banner = "濒死斩杀";
                    Log($"互助斩杀：{ai.Name} 血量不足继续，被你斩杀");
                }
                else if (winner != null && !winner.IsPlayer && winner != ai)
                {
                    var help = Math.Max(GameBalance.MinBet, (int)Math.Floor(Pot * ratio));
                    help = Math.Min(help, Math.Max(0, winner.Courage));
                    SpendCourage(winner, help);
                    AddCourage(ai, help);
                    ai.Banner = "获得援助";
                    ai.Status = "获救";
                    Log($"{winner.Name} 抽出 {help} 勇气值救助 {ai.Name}");
                }
            }
        }

        private void AfterRound()
        {
            PendingAttackDamage = 0;
            IncomingAttack = false;
            AttackLevel = 1;
            if (Player.Hp <= 0)
            {
                Phase = GamePhase.StageFail;
                Hint = LastResult + "\n生命耗尽。可看广告复活，或重开本关。";
                Notify();
                return;
            }

            Phase = GamePhase.RoundSettle;
            if (!AnyEnemyAlive())
            {
                Hint = LastResult + "\n已击杀全部敌人，点击进入商店";
            }
            else
            {
                Hint = LastResult + "\n点击「下一局」继续";
            }

            Notify();
        }

        private void EnterShop()
        {
            var score = ScoreSvc();
            var gold = score != null ? score.CollectGoldDelta() : 0;
            if (Run.DoubleGoldThisStage)
            {
                gold *= 2;
            }

            _shopGoldGranted = gold;
            Run.Gold += gold;
            var total = score != null ? score.Current.Total : 0;
            Log($"通关结算：总积分 {total} → {gold} 金币（总金币 {Run.Gold}）");
            Phase = GamePhase.Shop;
            Run.ShopRefreshCount = 0;
            RollShopOffers();
            Hint = $"关卡胜利！{total} 积分兑换 {gold} 金币。购买道具后进入下一关。";
            LastResult = Hint;
            Notify();
        }

        private void TryAutoLoanOrFail()
        {
            var magnifierDisabled = Run.DisabledConsumable == ConsumableId.LoanTicket;
            if (Run.LoanTicket && !magnifierDisabled)
            {
                Run.LoanTicket = false;
                _loanCourageBonus = GameBalance.MinBet;
                Log("借贷券生效，获得最低下注勇气值");
                StartRound();
                return;
            }

            Phase = GamePhase.StageFail;
            Hint = "勇气值不足。可看广告借贷继续，或重开本关。";
            Notify();
        }

        private void ContinueAfterLoan()
        {
            if (Player.Hp <= 0)
            {
                Phase = GamePhase.StageFail;
                Hint = "生命仍未恢复，请先复活";
                Notify();
                return;
            }

            StartRound();
        }

        /// <summary>从勇气值扣下注。看过牌的座位付双倍；反加注词缀再让玩家 ×1.5。</summary>
        private bool TryCommitUnits(SeatState seat, int units, out int paid)
        {
            units = Math.Max(units, seat.StreetUnits);
            var cost = CostFor(seat, units) - CostFor(seat, seat.StreetUnits);
            if (Run.Affix == BossAffix.AntiRaise && seat.IsPlayer)
            {
                cost = (int)Math.Ceiling(cost * 1.5f);
            }

            paid = cost;
            if (cost <= 0)
            {
                seat.StreetUnits = Math.Max(seat.StreetUnits, units);
                return true;
            }

            if (cost > seat.Courage)
            {
                if (seat.Courage <= 0)
                {
                    paid = 0;
                    return false;
                }

                paid = seat.Courage;
                SpendCourage(seat, paid);
                seat.TotalBet += paid;
                seat.StreetPaid += paid;
                Pot += paid;
                return true;
            }

            SpendCourage(seat, cost);
            seat.TotalBet += cost;
            seat.StreetPaid += cost;
            seat.StreetUnits = units;
            Pot += cost;
            return true;
        }

        private int CostFor(SeatState seat, int units)
        {
            return PaysDouble(seat) ? units * 2 : units;
        }

        /// <summary>
        /// 未看牌跟同一档（1×）。玩家闷注时，敌人始终按看牌价付双倍。
        /// </summary>
        private bool PaysDouble(SeatState seat)
        {
            if (seat == null)
            {
                return false;
            }

            return !seat.IsPlayer || seat.Looked;
        }

        public int CostToReach(int units)
        {
            return Math.Max(0, CostFor(Player, units) - CostFor(Player, Player.StreetUnits));
        }

        private int UnitsAffordable(SeatState seat)
        {
            var denom = PaysDouble(seat) ? 2 : 1;
            if (Run.Affix == BossAffix.AntiRaise && seat.IsPlayer)
            {
                denom = Math.Max(1, (int)Math.Ceiling(denom * 1.5f));
            }

            return AlignBet(seat.Courage / denom);
        }

        /// <summary>连输触发心态崩了时，把玩家最大下注压到当前勇气值一半。</summary>
        private int MaxBetUnits()
        {
            var cap = UnitsAffordable(Player);
            if (Run.Tilted)
            {
                cap = Math.Min(cap, AlignBet(Player.Courage / 2 / (Player.Looked ? 2 : 1)));
            }

            return Math.Max(_betStep, cap);
        }

        private SeatState FirstAliveEnemy()
        {
            for (var i = 0; i < Enemies.Length; i++)
            {
                if (Enemies[i].Alive)
                {
                    return Enemies[i];
                }
            }

            return null;
        }

        private int FindVisualSlot(SeatState target)
        {
            if (target == null)
            {
                return -1;
            }

            var activeCount = 0;
            for (var i = 0; i < Enemies.Length; i++)
            {
                if (Enemies[i].ActiveInStage)
                {
                    activeCount++;
                }
            }

            var placed = 0;
            for (var i = 0; i < Enemies.Length; i++)
            {
                if (!Enemies[i].ActiveInStage)
                {
                    continue;
                }

                var slot = TableVisualSlot(placed, activeCount);
                if (Enemies[i] == target)
                {
                    return slot;
                }

                placed++;
            }

            return -1;
        }

        private int EnemyTurnSlot(SeatState enemy)
        {
            if (enemy == null || enemy.IsPlayer)
            {
                return TurnArrowNone;
            }

            var slot = FindVisualSlot(enemy);
            return slot >= 0 ? slot : TurnArrowNone;
        }

        private SeatState BestRemainingAi()
        {
            SeatState best = null;
            HandScore score = default;
            var first = true;
            for (var i = 0; i < Enemies.Length; i++)
            {
                var e = Enemies[i];
                if (!e.Alive || e.Folded)
                {
                    continue;
                }

                var s = EvaluateSeat(e);
                if (first || s.CompareTo(score) > 0)
                {
                    best = e;
                    score = s;
                    first = false;
                }
            }

            return best;
        }

        private int CountInHand()
        {
            var n = 0;
            foreach (var seat in AllSeats())
            {
                if (Participates(seat) && !seat.Folded)
                {
                    n++;
                }
            }

            return n;
        }

        private int CountOpponentsInHand()
        {
            var n = 0;
            for (var i = 0; i < Enemies.Length; i++)
            {
                var enemy = Enemies[i];
                if (Participates(enemy) && !enemy.Folded)
                {
                    n++;
                }
            }

            return n;
        }

        private bool Participates(SeatState seat)
        {
            if (seat.IsPlayer)
            {
                return true;
            }

            return seat.ActiveInStage && (seat.Hp > 0 || (!seat.Folded && seat.TotalBet > 0));
        }

        private bool AnyEnemyAlive()
        {
            for (var i = 0; i < Enemies.Length; i++)
            {
                if (Enemies[i].Alive)
                {
                    return true;
                }
            }

            return false;
        }

        private int CountAliveEnemies()
        {
            var n = 0;
            for (var i = 0; i < Enemies.Length; i++)
            {
                if (Enemies[i].Alive)
                {
                    n++;
                }
            }

            return n;
        }

        /// <summary>玩家胜：只剩 1 名敌人则直接攻击，否则点选目标。</summary>
        private void EnterPlayerAttack(string hint)
        {
            if (!AnyEnemyAlive())
            {
                AfterRound();
                return;
            }

            Phase = GamePhase.WaitingAttack;
            if (CountAliveEnemies() == 1)
            {
                var only = FirstAliveEnemy();
                if (only != null)
                {
                    Hint = string.IsNullOrEmpty(hint)
                        ? $"场上仅剩 {only.Name}，直接攻击"
                        : $"{hint}。场上仅剩一名敌人，直接攻击";
                    BeginPlayerAttack(only);
                    return;
                }
            }

            Hint = string.IsNullOrEmpty(hint) ? "点选一名敌人攻击" : $"{hint}，点选敌人攻击";
            Notify();
        }

        private bool AllActiveBetsEqual()
        {
            int? units = null;
            foreach (var seat in AllSeats())
            {
                if (!Participates(seat) || seat.Folded || IsAllIn(seat))
                {
                    continue;
                }

                if (units == null)
                {
                    units = seat.StreetUnits;
                    continue;
                }

                if (seat.StreetUnits != units.Value)
                {
                    return false;
                }
            }

            return true;
        }

        private void ClearRound(SeatState seat)
        {
            seat.StreetUnits = 0;
            seat.StreetPaid = 0;
            seat.TotalBet = 0;
            seat.Folded = false;
            seat.Looked = !seat.IsPlayer;
            seat.ShowCards = false;
            seat.PeekedType = string.Empty;
            seat.ClearCardSelected();
            seat.Status = seat.Alive || seat.IsPlayer ? string.Empty : "未上场";
            if (!seat.IsPlayer && !seat.Alive)
            {
                seat.Status = seat.ActiveInStage ? "阵亡" : "未上场";
            }
        }

        private void ApplySeatHp(SeatState seat, int hp, int maxHp)
        {
            if (seat == null)
            {
                return;
            }

            seat.MaxHp = Math.Max(0, maxHp);
            seat.Hp = Math.Max(0, hp);
            HpSvc()?.BeginStage(seat.Id, seat.MaxHp, seat.Hp);
        }

        /// <summary>每回合筹码按人物当前血量换算，怪物与玩家同一套；不改血量。</summary>
        private void BeginRoundCourage()
        {
            var sourceHp = Math.Max(0, Player.Hp);
            foreach (var seat in AllSeats())
            {
                var hpForChips = seat.IsPlayer || seat.Alive ? sourceHp : 0;
                seat.Courage = ScoreBalance.HpToCourage(hpForChips);
                seat.CourageStake = 0;
                seat.RoundStartChips = seat.Courage;
                CourageSvc()?.BeginStage(seat.Id, hpForChips);
            }

            if (_loanCourageBonus > 0)
            {
                AddCourage(Player, _loanCourageBonus);
                Player.RoundStartChips = Player.Courage;
                _loanCourageBonus = 0;
            }

            if (AppServices.IsReady)
            {
                AppServices.Resolve<IScoreService>().BeginRound();
            }
        }

        private void ApplyHeroToPlayer(bool inheritHp)
        {
            var hero = ResolveHero();
            Run.HeroId = hero != null ? hero.Id : 0;
            Player.ActiveInStage = true;
            Player.Name = hero != null && !string.IsNullOrEmpty(hero.Name) ? hero.Name : "你";
            var maxHp = hero != null && hero.Hp > 0 ? hero.Hp : GameBalance.PlayerStartHp;
            var hp = inheritHp ? Math.Min(Math.Max(0, Player.Hp), maxHp) : maxHp;
            ApplySeatHp(Player, hp, maxHp);
            Player.Attack = hero != null ? Math.Max(0, hero.HeroDamage) : 0;
        }

        private int ApplyLevelEnemies()
        {
            var snapshot = LevelSvc()?.Current;
            var names = new[] { "敌人A", "敌人B", "敌人C" };
            if (snapshot != null)
            {
                Run.LevelId = snapshot.Id;
                Run.Stage = snapshot.Level;
                Run.HasBoss = snapshot.HasBoss;
                if (snapshot.HasBoss)
                {
                    Run.Affix = RandomAffix();
                    ApplyEdgeAffix();
                }

                var count = Math.Min(snapshot.Monsters.Count, Enemies.Length);
                for (var i = 0; i < Enemies.Length; i++)
                {
                    var seat = Enemies[i];
                    if (i < count)
                    {
                        var monster = snapshot.Monsters[i];
                        seat.ActiveInStage = true;
                        seat.IsBoss = monster.IsBoss;
                        seat.Profile = monster.IsBoss ? AiProfile.Expert : DefaultEnemyProfile(seat.Id);
                        seat.Name = monster.IsBoss ? "BOSS" : names[i];
                        ApplySeatHp(seat, monster.Hp, monster.Hp);
                        seat.Attack = Math.Max(0, monster.Damage);
                    }
                    else
                    {
                        DeactivateEnemy(seat, i);
                    }

                    seat.Banner = string.Empty;
                    ClearRound(seat);
                }

                return count;
            }

            var fallbackCount = GameBalance.EnemyCountForStage(Run.Stage);
            Run.HasBoss = GameBalance.IsBossStage(Run.Stage);
            if (Run.HasBoss)
            {
                names[0] = "BOSS";
                Run.Affix = RandomAffix();
                ApplyEdgeAffix();
            }

            for (var i = 0; i < Enemies.Length; i++)
            {
                var seat = Enemies[i];
                seat.ActiveInStage = i < fallbackCount;
                seat.IsBoss = Run.HasBoss && i == 0;
                seat.Profile = seat.IsBoss ? AiProfile.Expert : DefaultEnemyProfile(seat.Id);
                seat.Name = i < fallbackCount ? names[i] : $"敌人{i + 1}";
                var maxHp = GameBalance.EnemyHp(Run.Stage, seat.IsBoss);
                ApplySeatHp(seat, seat.ActiveInStage ? maxHp : 0, maxHp);
                seat.Attack = seat.ActiveInStage ? 10 : 0;
                seat.Banner = string.Empty;
                ClearRound(seat);
            }

            return fallbackCount;
        }

        private void DeactivateEnemy(SeatState seat, int index)
        {
            seat.ActiveInStage = false;
            seat.IsBoss = false;
            seat.Profile = DefaultEnemyProfile(seat.Id);
            seat.Name = $"敌人{index + 1}";
            ApplySeatHp(seat, 0, 0);
            seat.Attack = 0;
        }

        private bool TryAdvanceLevel()
        {
            var levels = LevelSvc();
            if (levels == null)
            {
                Run.Stage++;
                return Run.Stage <= 30;
            }

            var current = levels.Current;
            if (current == null)
            {
                return false;
            }

            var progress = ProgressSvc();
            progress?.MarkCleared(current.Id);
            progress?.SetLastLevel(current.Id);
            progress?.SetLastDifficulty(current.Difficulty);

            if (levels.TryGetNext(current.Difficulty, current.Level, out var next) && next != null)
            {
                levels.TrySelect(next.Id);
                progress?.SetLastLevel(next.Id);
                progress?.Save();
                return true;
            }

            progress?.Save();
            if (progress != null && progress.IsCleared(current.Difficulty))
            {
                Hint = levels.TryGetNextDifficulty(current.Difficulty, out var nextDiff)
                    ? $"已完成难度{current.Difficulty}，解锁难度{nextDiff}"
                    : $"已完成难度{current.Difficulty}，全部难度通关";
            }
            else
            {
                Hint = "你已打完该难度全部关卡！";
            }

            return false;
        }

        private static HeroConfig ResolveHero()
        {
            var heroId = ProgressSvc()?.LastHeroId ?? 0;
            var hero = HeroConfig.Get(heroId);
            if (hero != null)
            {
                return hero;
            }

            if (GameConst.IsLoaded)
            {
                hero = HeroConfig.Get(GameConst.Instance.DefaultHeroId);
                if (hero != null)
                {
                    return hero;
                }
            }

            foreach (var pair in HeroConfig.All)
            {
                return pair.Value;
            }

            return null;
        }

        private void AddCourage(SeatState seat, int amount)
        {
            if (seat == null || amount <= 0)
            {
                return;
            }

            seat.Courage += amount;
            CourageSvc()?.Add(seat.Id, amount);
        }

        private void SpendCourage(SeatState seat, int amount)
        {
            if (seat == null || amount <= 0)
            {
                return;
            }

            if (amount > seat.Courage)
            {
                amount = seat.Courage;
            }

            seat.Courage -= amount;
            seat.CourageStake += amount;
            CourageSvc()?.TryBet(seat.Id, amount);
        }

        private static IHpService HpSvc()
        {
            return AppServices.IsReady ? AppServices.Resolve<IHpService>() : null;
        }

        private static ICourageService CourageSvc()
        {
            return AppServices.IsReady ? AppServices.Resolve<ICourageService>() : null;
        }

        private static ILevelService LevelSvc()
        {
            return AppServices.IsReady ? AppServices.Resolve<ILevelService>() : null;
        }

        private static ILevelProgressService ProgressSvc()
        {
            return AppServices.IsReady ? AppServices.Resolve<ILevelProgressService>() : null;
        }

        private static IScoreService ScoreSvc()
        {
            return AppServices.IsReady ? AppServices.Resolve<IScoreService>() : null;
        }

        /// <summary>亮牌获胜：本手对怪造成的伤害记入本轮/关卡/总积分。</summary>
        private void AwardPlayerRoundScore(int potWon)
        {
            if (potWon <= 0)
            {
                return;
            }

            ScoreSvc()?.AwardRoundScore(potWon);
        }

        private void ApplyEdgeAffix()
        {
            if (Run.Affix != BossAffix.Edge)
            {
                return;
            }

            if (Run.Relics.Count > 0 && _rng.Next(2) == 0)
            {
                Run.DisabledRelic = Run.Relics[_rng.Next(Run.Relics.Count)];
                Log($"锋芒：本局禁用遗物 {Run.DisabledRelic}");
                return;
            }

            var options = new List<ConsumableId>();
            if (Run.SplashThisRound)
            {
                options.Add(ConsumableId.SplashSlash);
            }

            if (Run.MagnifierThisRound)
            {
                options.Add(ConsumableId.Magnifier);
            }

            if (Run.LoanTicket)
            {
                options.Add(ConsumableId.LoanTicket);
            }

            if (options.Count > 0)
            {
                Run.DisabledConsumable = options[_rng.Next(options.Count)];
                Log($"锋芒：本局禁用道具 {Run.DisabledConsumable}");
                if (Run.DisabledConsumable == ConsumableId.SplashSlash)
                {
                    Run.SplashThisRound = false;
                }

                if (Run.DisabledConsumable == ConsumableId.Magnifier)
                {
                    Run.MagnifierThisRound = false;
                }
            }
        }

        private BossAffix RandomAffix()
        {
            var values = (BossAffix[])Enum.GetValues(typeof(BossAffix));
            return values[_rng.Next(1, values.Length)];
        }

        /// <summary>
        /// 创建座位并绑定 AI 人格：id1 保守、id2 平衡偏激进、id3 激进。
        /// BOSS 关会在 <see cref="StartStage"/> 把对应座位改成 Expert。
        /// </summary>
        private SeatState CreateSeat(int id, string name, bool player)
        {
            return new SeatState
            {
                Id = id,
                Name = name,
                IsPlayer = player,
                ActiveInStage = player,
                MaxHp = 0,
                Hp = 0,
                Profile = player ? null : DefaultEnemyProfile(id)
            };
        }

        private static AiProfile DefaultEnemyProfile(int seatId)
        {
            switch (seatId)
            {
                case 1:
                    return AiProfile.Conservative;
                case 3:
                    return AiProfile.Aggressive;
                default:
                    return AiProfile.BalancedAggressive;
            }
        }

        private void Log(string line)
        {
            Run.Log.Add(line);
            if (Run.Log.Count > 40)
            {
                Run.Log.RemoveAt(0);
            }
        }

        private void RollShopOffers()
        {
            Run.ShopOfferIds.Clear();
            var pool = new List<RelicConfig>();
            foreach (var relic in RelicConfig.All.Values)
            {
                if (relic == null || OwnsRelicConfig(relic.Id) || relic.RefreshProbability <= 0f)
                {
                    continue;
                }

                pool.Add(relic);
            }

            var slots = Math.Min(GameBalance.ShopOfferCount, pool.Count);
            for (var n = 0; n < slots; n++)
            {
                var pick = PickWeightedRelic(pool);
                if (pick == null)
                {
                    break;
                }

                Run.ShopOfferIds.Add(pick.Id);
                pool.Remove(pick);
            }
        }

        private RelicConfig PickWeightedRelic(List<RelicConfig> pool)
        {
            if (pool == null || pool.Count == 0)
            {
                return null;
            }

            var total = 0f;
            for (var i = 0; i < pool.Count; i++)
            {
                total += Math.Max(0f, pool[i].RefreshProbability);
            }

            if (total <= 0f)
            {
                return pool[_rng.Next(pool.Count)];
            }

            var roll = _rng.NextDouble() * total;
            var acc = 0.0;
            for (var i = 0; i < pool.Count; i++)
            {
                acc += Math.Max(0f, pool[i].RefreshProbability);
                if (roll < acc)
                {
                    return pool[i];
                }
            }

            return pool[pool.Count - 1];
        }

        private bool HasUnownedRelicConfig()
        {
            foreach (var relic in RelicConfig.All.Values)
            {
                if (relic != null && !OwnsRelicConfig(relic.Id) && relic.RefreshProbability > 0f)
                {
                    return true;
                }
            }

            return false;
        }

        private void Notify() => Changed?.Invoke();

        private static int AlignBet(int value)
        {
            if (value < GameBalance.MinBet)
            {
                return GameBalance.MinBet;
            }

            return value / GameBalance.MinBet * GameBalance.MinBet;
        }

        private static int Clamp(int value, int min, int max)
        {
            if (value < min)
            {
                return min;
            }

            return value > max ? max : value;
        }

        private static bool ContainsSuit(HandScore score, Suit suit)
        {
            for (var i = 0; i < score.UsedCards.Length; i++)
            {
                if (score.UsedCards[i].Suit == suit)
                {
                    return true;
                }
            }

            return false;
        }

        private bool IsAllIn(SeatState seat)
        {
            return seat != null && !seat.Folded && seat.Courage <= 0 && seat.TotalBet > 0;
        }

        private int CountPlayersWhoCanBet()
        {
            var n = 0;
            foreach (var seat in AllSeats())
            {
                if (Participates(seat) && !seat.Folded && seat.Courage > 0)
                {
                    n++;
                }
            }

            return n;
        }

        private SeatState LastInHand()
        {
            SeatState last = null;
            var n = 0;
            foreach (var seat in AllSeats())
            {
                if (Participates(seat) && !seat.Folded)
                {
                    last = seat;
                    n++;
                }
            }

            return n == 1 ? last : null;
        }

        private bool StreetBettingComplete()
        {
            var round = CurrentRoundUnits();
            foreach (var seat in AllSeats())
            {
                if (!Participates(seat) || seat.Folded || IsAllIn(seat))
                {
                    continue;
                }

                if (seat.StreetUnits < round)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>连续两街无人加注，或只剩一人还能下注时，立刻摊牌。</summary>
        private bool ShouldImmediateShowdown()
        {
            if (CountInHand() <= 1)
            {
                return true;
            }

            if (!StreetBettingComplete())
            {
                return false;
            }

            return CountPlayersWhoCanBet() <= 1;
        }

        private void AwardUncontestedAndSettle()
        {
            var last = LastInHand();
            if (last == null)
            {
                Pot = 0;
                AfterRound();
                return;
            }

            var amount = Pot;
            AddCourage(last, amount);
            LastResult = $"{last.Name} 无人争夺，收走奖池 {amount}";
            Log(LastResult);
            if (last.IsPlayer)
            {
                AwardPlayerRoundScore(amount);
            }
            ApplyBankruptcy(last.IsPlayer, last);
            Pot = 0;
            if (last.IsPlayer)
            {
                Run.ConsecutiveLosses = 0;
                Run.Tilted = false;
                PendingAttackDamage = Math.Max(1, ShowdownStake());
                AttackLevel = 1;
                EnterPlayerAttack($"对手弃牌，你收走奖池 {amount}");
                return;
            }

            Hint = LastResult;
            AfterRound();
        }

        /// <summary>主池 + 边池：按投入分层，每层只在该层有份的人里比牌。</summary>
        private void AwardPots()
        {
            var layers = BuildPotLayers();
            for (var i = 0; i < layers.Count; i++)
            {
                var layer = layers[i];
                if (layer.Amount <= 0)
                {
                    continue;
                }

                var winners = BestEligible(layer.Eligible);
                if (winners.Count == 0)
                {
                    var leftover = LastInHand();
                    if (leftover != null)
                    {
                        winners.Add(leftover);
                    }
                }

                if (winners.Count == 0)
                {
                    continue;
                }

                var share = layer.Amount / winners.Count;
                var remain = layer.Amount - share * winners.Count;
                for (var w = 0; w < winners.Count; w++)
                {
                    var gain = share + (w == 0 ? remain : 0);
                    AddCourage(winners[w], gain);
                    Log($"{layer.Name} {layer.Amount} → {winners[w].Name} +{gain}");
                }
            }

            Pot = 0;
        }

        private List<PotLayer> BuildPotLayers()
        {
            var caps = new List<int>();
            foreach (var seat in AllSeats())
            {
                if (seat.TotalBet <= 0)
                {
                    continue;
                }

                if (!caps.Contains(seat.TotalBet))
                {
                    caps.Add(seat.TotalBet);
                }
            }

            caps.Sort();
            var layers = new List<PotLayer>();
            var prev = 0;
            for (var i = 0; i < caps.Count; i++)
            {
                var cap = caps[i];
                var slice = cap - prev;
                if (slice <= 0)
                {
                    continue;
                }

                var layer = new PotLayer
                {
                    Name = i == 0 ? "主池" : $"边池{i}",
                    Amount = 0
                };
                foreach (var seat in AllSeats())
                {
                    if (seat.TotalBet < cap)
                    {
                        continue;
                    }

                    layer.Amount += slice;
                    if (!seat.Folded)
                    {
                        layer.Eligible.Add(seat);
                    }
                }

                if (layer.Amount > 0)
                {
                    layers.Add(layer);
                }

                prev = cap;
            }

            return layers;
        }

        private List<SeatState> BestEligible(List<SeatState> eligible)
        {
            var winners = new List<SeatState>();
            HandScore best = default;
            var first = true;
            for (var i = 0; i < eligible.Count; i++)
            {
                var seat = eligible[i];
                if (seat.Folded)
                {
                    continue;
                }

                var score = EvaluateSeat(seat);
                if (first)
                {
                    best = score;
                    winners.Add(seat);
                    first = false;
                    continue;
                }

                var cmp = score.CompareTo(best);
                if (cmp > 0)
                {
                    best = score;
                    winners.Clear();
                    winners.Add(seat);
                }
                else if (cmp == 0)
                {
                    winners.Add(seat);
                }
            }

            return winners;
        }

        private enum RevealKind
        {
            None = 0,
            Showdown = 1,
            OpenDuel = 2
        }

        private sealed class PotLayer
        {
            public string Name;
            public int Amount;
            public readonly List<SeatState> Eligible = new List<SeatState>();
        }
    }
}
