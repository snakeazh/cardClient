using System;
using System.Collections.Generic;
using App.Bootstrap;
using App.Config;
using App.Level;
using App.Score;
using App.Talent;
using App.Unlock;
using Framework.Log;

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
        private bool _attackHitsApplied;
        private readonly List<SeatState> _compareQueue = new List<SeatState>();
        private int _compareCursor;
        private int _roundDamageDealt;
        private bool _sequentialCompare;
        /// <summary>本回合在血量换算之外额外获得的勇气值（借贷券 / 广告借贷）。</summary>
        private int _loanCourageBonus;
        /// <summary>本关进商店发放的金币（GetGold + 击杀加成，含双倍），供广告再发一份。不含本手伤害换金。</summary>
        private int _shopGoldGranted;
        private int _rubsUsedThisHand;
        private bool _rubbedThisHand;
        private bool _playerCardsShownThisRound;
        private bool _playerHandSettledThisRound;
        private bool _amuletUsedThisRound;
        private bool _ironRiceBowlGranted;
        private int _enemyDowngradeSteps;
        private int _roundCompareWins;
        private int _roundCompareLosses;
        private int _roundKills;
        private bool _lastPlayerAttackCrit;
        private SeatState _pendingDamageSource;
        private HandScore _pendingPlayerScore;
        private HandScore _pendingEnemyScore;
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
        /// <summary>玩家本手发牌张数。手牌压缩可降到 4。</summary>
        public int PlayerDealCount => BossMechanics.PlayerCardsDealt(Run);
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
        /// <summary>本关每回合击杀数，与 <see cref="StageRoundScores"/> 一一对应。</summary>
        public IReadOnlyList<int> StageRoundKills =>
            ScoreSvc()?.StageRoundKills ?? Array.Empty<int>();
        public int PendingAttackDamage { get; private set; }
        /// <summary>最近一次玩家伤害结算的遗物上下文，HUD 装备加成动画复用同一份掷骰。</summary>
        public RelicCombatContext LastRelicContext { get; private set; }
        public int AttackPlaySerial { get; private set; }
        public int AttackVisualSlot { get; private set; } = -1;

        /// <summary>
        /// 牌桌 otherNode 当前展示的敌人：比牌/攻击跟当前对手，透视跟已透视座位，否则第一个存活敌人。
        /// </summary>
        public SeatState DisplayedEnemy
        {
            get
            {
                if (_pendingOpenTarget != null && _pendingOpenTarget.ActiveInStage)
                {
                    return _pendingOpenTarget;
                }

                if (AttackVisualSlot >= 0)
                {
                    var attacking = EnemyAtVisualSlot(AttackVisualSlot);
                    if (attacking != null && attacking.ActiveInStage)
                    {
                        return attacking;
                    }
                }

                SeatState peeked = null;
                for (var i = 0; i < Enemies.Length; i++)
                {
                    var enemy = Enemies[i];
                    if (enemy != null &&
                        enemy.ActiveInStage &&
                        enemy.Alive &&
                        !string.IsNullOrEmpty(enemy.PeekedType))
                    {
                        peeked = enemy;
                    }
                }

                if (peeked != null)
                {
                    return peeked;
                }

                for (var i = 0; i < Enemies.Length; i++)
                {
                    var enemy = Enemies[i];
                    if (enemy != null && enemy.ActiveInStage && enemy.Alive)
                    {
                        return enemy;
                    }
                }

                return null;
            }
        }

        public int DisplayedEnemyVisualSlot => FindVisualSlot(DisplayedEnemy);

        /// <summary>
        /// 站在 player1 中心的敌人：只剩 1 个活人就站中间；2 个时第一个开牌的站中间；
        /// 3 个拼牌时当前对手站中间。攻击演出未结束时，当前目标即使刚死也占住位置。
        /// </summary>
        public SeatState CenterStandEnemy
        {
            get
            {
                if (AttackPlaying && !IncomingAttack)
                {
                    var attacking = EnemyAtVisualSlot(AttackVisualSlot);
                    if (attacking != null && attacking.ActiveInStage)
                    {
                        return attacking;
                    }
                }

                var alive = CountAliveEnemies();
                if (alive <= 1)
                {
                    return FirstAliveEnemy();
                }

                if (alive == 2)
                {
                    if (_sequentialCompare ||
                        Phase == GamePhase.Showdown ||
                        Phase == GamePhase.WaitingAttack ||
                        Phase == GamePhase.RoundSettle)
                    {
                        var current = DisplayedEnemy;
                        return current != null && current.Alive ? current : FirstAliveEnemy();
                    }

                    return FirstAliveEnemy();
                }

                if (_sequentialCompare ||
                    Phase == GamePhase.Showdown ||
                    Phase == GamePhase.WaitingAttack ||
                    Phase == GamePhase.RoundSettle)
                {
                    var current = DisplayedEnemy;
                    return current != null && current.Alive ? current : null;
                }

                return null;
            }
        }

        public int CenterStandVisualSlot => FindVisualSlot(CenterStandEnemy);
        public int AttackDamage { get; private set; }
        /// <summary>命中飘字 / 实际扣血用的伤害。挨打时已含玩家减伤，打怪时等于 <see cref="AttackDamage"/>。</summary>
        public int TakenDamage { get; private set; }
        /// <summary>攻击演出强度：1 低 / 2 中 / 3 高。</summary>
        public int AttackLevel { get; private set; } = 1;
        public bool AttackPlaying => _pendingAttackTarget != null;
        /// <summary>当前攻击由敌人打向玩家。</summary>
        public bool IncomingAttack { get; private set; }
        /// <summary>本手正在逐个与敌人比牌。</summary>
        public bool SequentialCompare => _sequentialCompare;
        public int RevealPlaySerial { get; private set; }
        public int RevealWinnerId { get; private set; } = -1;
        public readonly List<int> RevealSeatIds = new List<int>();
        public bool SelectingOpenTarget { get; private set; }
        public bool SelectingXRayTarget { get; private set; }
        public bool SelectingRubTarget { get; private set; }
        public bool AiActing { get; private set; }
        public int ActingAiId { get; private set; } = -1;
        public const float AiActionDelay = 1f;
        public bool PlayerMayLookCards =>
            !AiActing &&
            Phase == GamePhase.WaitingLookChoice &&
            !Player.Looked &&
            !Player.Folded;
        public bool PlayerMayCancelLookOrRub => false;
        /// <summary>开牌阶段且还有搓牌次数时，可点选手牌搓牌。</summary>
        public bool PlayerMayHoldRub =>
            !AiActing &&
            !Player.Folded &&
            Player.Looked &&
            Phase == GamePhase.WaitingOpen &&
            Run.PeekGoodCharges > 0 &&
            !BossMechanics.SkillsDisabled(Run) &&
            BossMechanics.CanAffordRub(Run);
        /// <summary>该难度已无下一关。</summary>
        public bool IsLastLevel
        {
            get
            {
                var levels = LevelSvc();
                var current = levels?.Current;
                return levels != null &&
                       current != null &&
                       !levels.TryGetNext(current.Difficulty, current.Level, out _);
            }
        }
        /// <summary>搓牌点选中的手牌下标；未选为 -1。</summary>
        public int PendingRubIndex => _pendingRubIndex;
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
            Run.ClearRunProgress();
            Run.Log.Clear();
            var hero = ResolveHero();
            Run.HeroId = hero != null ? hero.Id : 0;
            var baseGold = GameConst.IsLoaded ? Math.Max(0, GameConst.Instance.PlayerInitialGoldNum) : 0;
            var talentGold = (int)Math.Round(TalentMechanics.SumValue(TalentSvc(), MechanismType.InitialFunds));
            var heroGold = (int)Math.Round(HeroMechanics.SumValue(hero, MechanismType.InitialFunds));
            var extraGold = Math.Max(0, talentGold) + Math.Max(0, heroGold);
            Run.Gold = baseGold + extraGold;
            if (extraGold > 0)
            {
                Log($"富裕：初始金币 {Run.Gold}（基础 {baseGold} + {extraGold}）");
            }

            if (AppServices.IsReady)
            {
                AppServices.Resolve<IScoreService>().BeginChapter();
            }

            UnlockSvc()?.BeginRun();
            StartStage(inheritPlayerHp: false);
        }

        /// <summary>失败后再战：回到当前难度第 1 关并开新章节。</summary>
        public void RestartChallenge()
        {
            var levels = LevelSvc();
            var current = levels?.Current;
            if (current != null)
            {
                levels.TrySelect(current.Difficulty, 1);
            }

            StartNewRun();
        }

        public void RestartStage()
        {
            StartStage(inheritPlayerHp: false);
        }

        public void SelectRubCard(int index)
        {
            if (Phase != GamePhase.WaitingRub || index < 0 || index >= PlayerDealCount)
            {
                return;
            }

            _pendingRubIndex = index;
            Hint = $"已选中第 {index + 1} 张：拖开并持续搓够时间后松手";
            Notify();
        }

        /// <summary>长按手牌进入搓牌：翻到背面后拖拽，松手不够则取消。</summary>
        public bool TryBeginHoldRub(int index)
        {
            if (!PlayerMayHoldRub || index < 0 || index >= PlayerDealCount)
            {
                return false;
            }

            Player.Status = "已看牌";
            SelectingXRayTarget = false;
            SelectingRubTarget = false;
            Run.RubsLeft = 1;
            _pendingRubIndex = index;
            Phase = GamePhase.WaitingRub;
            Hint = $"搓牌（剩余 {Run.PeekGoodCharges}）。拖开并搓够时间后松手替换";
            Notify();
            return true;
        }

        public void NotifyRubTooWeak()
        {
            if (Phase != GamePhase.WaitingRub)
            {
                return;
            }

            Hint = "搓牌幅度不够，请再拖一次";
            Notify();
        }

        public void NotifyRubTooShort()
        {
            if (Phase != GamePhase.WaitingRub)
            {
                return;
            }

            Hint = "搓牌时间不够，请再搓久一点";
            Notify();
        }

        /// <summary>长按搓牌未达标或松手取消，不消耗次数，回到开牌。</summary>
        public void CancelHoldRub(string hint)
        {
            if (Phase != GamePhase.WaitingRub)
            {
                return;
            }

            _pendingRubIndex = -1;
            Run.RubsLeft = 0;
            ReturnToOpenReady(string.IsNullOrEmpty(hint)
                ? "点选 3 张牌后开牌。可使用技能"
                : hint);
        }

        public void ClearRubSelection()
        {
            if (Phase != GamePhase.WaitingRub)
            {
                return;
            }

            _pendingRubIndex = -1;
            Hint = $"搓牌（剩余 {Run.PeekGoodCharges}）。长按手牌拖开并搓够时间后松手替换";
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
            if (Phase != GamePhase.WaitingRub || index < 0 || index >= PlayerDealCount || Run.RubsLeft <= 0)
            {
                return;
            }

            if (!ApplyRubReplace(index, out var message))
            {
                return;
            }

            ReturnToOpenReady(Run.PeekGoodCharges > 0
                ? $"{message}。还可点选手牌搓牌 {Run.PeekGoodCharges} 次"
                : message);
        }

        /// <summary>点选手牌立刻搓牌替换，不进入拖拽阶段。</summary>
        public bool TryRubPlayerCard(int index)
        {
            if (!SelectingRubTarget ||
                AiActing ||
                Player.Folded ||
                !Player.Looked ||
                Phase != GamePhase.WaitingOpen ||
                Run.PeekGoodCharges <= 0 ||
                BossMechanics.SkillsDisabled(Run) ||
                !BossMechanics.CanAffordRub(Run) ||
                index < 0 ||
                index >= PlayerDealCount)
            {
                return false;
            }

            if (!ApplyRubReplace(index, out var message))
            {
                return false;
            }

            SelectingRubTarget = false;
            Hint = Run.PeekGoodCharges > 0
                ? $"{message}。再点搓牌可继续替换（剩余 {Run.PeekGoodCharges}）"
                : message;

            Notify();
            return true;
        }

        private bool ApplyRubReplace(int index, out string message)
        {
            message = null;
            var fee = BossMechanics.RubFeeGold(Run);
            if (fee > 0 && (Run == null || Run.Gold < fee))
            {
                Hint = "金币不足，无法搓牌";
                Notify();
                return false;
            }

            var old = Player.Hand[index];
            var next = DrawRubCard(old);
            if (!next.IsValid || next.Equals(old))
            {
                Hint = "没有可换的新牌";
                Notify();
                return false;
            }

            if (fee > 0)
            {
                SpendGold(fee);
                Log($"有偿服务：搓牌消耗 {fee} 金币（剩余 {Run.Gold}）");
            }

            Player.Hand[index] = next;
            if (Run.RubsLeft > 0)
            {
                Run.RubsLeft--;
            }

            if (Run.PeekGoodCharges > 0)
            {
                Run.PeekGoodCharges--;
            }

            _rubsUsedThisHand++;
            _rubbedThisHand = true;
            UnlockSvc()?.Report(ContidionType.ShuffleCard);
            if (RelicMechanics.HasMechanism(Run, MechanismType.RubbingCardRelic))
            {
                Run.RubRelicMagForever += RelicMechanics.SumValue(Run, MechanismType.RubbingCardRelic);
            }

            _pendingRubIndex = -1;
            Run.LastRubMessage = $"第 {index + 1} 张换成 {next.DisplayName}";
            Log(Run.LastRubMessage);
            message = Run.LastRubMessage;
            return true;
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

            if (index < 0 || index >= PlayerDealCount)
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

            if (Phase != GamePhase.WaitingOpen || SelectingRubTarget)
            {
                return;
            }

            if (index < 0 || index >= PlayerDealCount)
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

        private bool PlayerMayUseConsumable(bool thisHand)
        {
            if (Player == null || Player.Folded || AiActing)
            {
                return false;
            }

            if (Phase == GamePhase.WaitingOpen || Phase == GamePhase.WaitingRub)
            {
                return true;
            }

            return !thisHand && Phase == GamePhase.Shop;
        }

        public bool PlayerMayUsePeekGood =>
            PlayerMayUseItems &&
            Player.Looked &&
            Run.PeekGoodCharges > 0 &&
            Phase != GamePhase.WaitingRub &&
            !BossMechanics.SkillsDisabled(Run) &&
            BossMechanics.CanAffordRub(Run);

        public bool PlayerMayUseChaKanGood =>
            PlayerMayUseItems &&
            Run.ChaKanGoodCharges > 0 &&
            !BossMechanics.SkillsDisabled(Run);

        public bool PlayerMayUseTiHuanGood =>
            PlayerMayUseItems &&
            Run.TiHuanGoodCharges > 0 &&
            _deck != null &&
            !BossMechanics.SkillsDisabled(Run) &&
            !BossMechanics.SwapLocked(Run);

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

        /// <summary>点击搓牌按钮：进入或取消点选手牌替换。</summary>
        public void UsePeekGood()
        {
            if (!PlayerMayUsePeekGood)
            {
                return;
            }

            SelectingXRayTarget = false;
            SelectingRubTarget = !SelectingRubTarget;
            Hint = SelectingRubTarget
                ? $"搓牌（剩余 {Run.PeekGoodCharges}）。点选一张手牌替换"
                : "已取消搓牌";
            Notify();
        }

        public void UseChaKanGood()
        {
            if (!PlayerMayUseChaKanGood)
            {
                return;
            }

            SelectingRubTarget = false;
            SelectingXRayTarget = !SelectingXRayTarget;
            Hint = SelectingXRayTarget
                ? $"透视（剩余 {Run.ChaKanGoodCharges}）。点选一名敌人透视其手牌"
                : "已取消透视";
            Notify();
        }

        public void TryXRayPlayer()
        {
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

            SelectingRubTarget = false;
            SelectingXRayTarget = false;
            SyncDeckWithTable();
            var nextCards = new Card[PlayerDealCount];
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

            for (var i = 0; i < Player.Hand.Length; i++)
            {
                Player.Hand[i] = i < nextCards.Length ? nextCards[i] : default;
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
                Hint = $"已替换 {PlayerDealCount} 张手牌（未看牌，剩余 {Run.TiHuanGoodCharges}）";
                Log("替换手牌（未看牌）");
            }

            Notify();
        }

        private void TryXRaySeat(SeatState seat)
        {
            if (!PlayerMayUseChaKanGood || seat == null || !HasHand(seat) || seat.IsPlayer)
            {
                return;
            }

            if (!seat.Alive || seat.Folded)
            {
                return;
            }

            if (!string.IsNullOrEmpty(seat.PeekedType))
            {
                Hint = $"{seat.Name} 已经透视过";
                Notify();
                return;
            }

            var count = CardsDealtFor(seat);
            for (var i = 0; i < count; i++)
            {
                SetSpyReveal(seat.Id, i, true);
                seat.CardSelected[i] = true;
            }

            var score = EvaluateSeat(seat);
            seat.PeekedType = score.Label;
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
            SelectingXRayTarget = false;
            SelectingRubTarget = false;
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

        /// <summary>受击演出开始时扣血。没有演出时由 <see cref="CompletePlayerAttack"/> 兜底。</summary>
        public void ApplyPendingAttackHits()
        {
            if (_attackHitsApplied || Phase != GamePhase.WaitingAttack || _pendingAttackTarget == null)
            {
                return;
            }

            ResolvePendingAttackHits(_pendingAttackTarget);
            Notify();
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
            _pendingDamageSource = Player;
            AttackVisualSlot = FindVisualSlot(target);
            AttackDamage = Math.Max(0, PendingAttackDamage);
            TakenDamage = AttackDamage;
            _attackHitsApplied = false;
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
            if (!_attackHitsApplied)
            {
                ResolvePendingAttackHits(target);
            }

            _attackHitsApplied = false;
            PendingAttackDamage = 0;

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

        private void ResolvePendingAttackHits(SeatState target)
        {
            _attackHitsApplied = true;
            var damage = Math.Max(1, PendingAttackDamage);
            var dealt = ApplyPlayerAttackHits(target, damage, out var scoreDamage);
            if (target != null && !target.IsPlayer)
            {
                _roundDamageDealt += scoreDamage;
                ApplyBloodSucking(dealt);
            }
        }

        private int ApplyPlayerAttackHits(SeatState target, int damage, out int scoreDamage)
        {
            scoreDamage = 0;
            if (target == null)
            {
                return 0;
            }

            if (target.IsPlayer)
            {
                scoreDamage = damage;
                return ApplyDamage(target, damage, true, _pendingDamageSource);
            }

            var dealt = 0;
            var aoe = HeroMechanics.SumValue(Run, MechanismType.AoeDamage);
            if (aoe > 0f)
            {
                var aoeDmg = (int)Math.Round(damage * aoe);
                for (var i = 0; i < Enemies.Length; i++)
                {
                    if (!Enemies[i].Alive)
                    {
                        continue;
                    }

                    dealt += ApplyDamage(Enemies[i], aoeDmg, Enemies[i] == target, Player);
                    scoreDamage += aoeDmg;
                }

                Run.SplashThisRound = false;
            }
            else
            {
                dealt = ApplyDamage(target, damage, true, Player);
                scoreDamage = damage;
                var splashRatio = (Run.SplashThisRound ? GameBalance.SplashRatio : 0f)
                    + HeroMechanics.SumValue(Run, MechanismType.VersatilePerson);
                if (splashRatio > 0f)
                {
                    for (var i = 0; i < Enemies.Length; i++)
                    {
                        if (Enemies[i] == target || !Enemies[i].Alive)
                        {
                            continue;
                        }

                        var splash = (int)Math.Round(damage * splashRatio);
                        dealt += ApplyDamage(Enemies[i], splash, false, Player);
                        scoreDamage += splash;
                    }
                }

                Run.SplashThisRound = false;
            }

            dealt += ApplyCriticalAoe(target);
            if (HeroMechanics.Roll(Run, MechanismType.ExtraAttackOneTime, _rng))
            {
                dealt += ApplyDamage(target, damage, true, Player);
                scoreDamage += damage;
                Log("追击：额外攻击 1 次");
            }

            TryApplyStrawHeal(dealt);
            return dealt;
        }

        private void BeginIncomingAttack(SeatState attacker)
        {
            if (Phase != GamePhase.WaitingAttack || attacker == null || Player.Hp <= 0 || _pendingAttackTarget != null)
            {
                return;
            }

            IncomingAttack = true;
            _pendingAttackTarget = Player;
            _pendingDamageSource = attacker;
            AttackVisualSlot = FindVisualSlot(attacker);
            AttackDamage = Math.Max(0, PendingAttackDamage);
            TakenDamage = IncomingDamageAfterMitigation(PendingAttackDamage, attacker);
            _attackHitsApplied = false;
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
            Run.HandBrandIndex = -1;
            _sequentialCompare = true;
            _compareQueue.Clear();
            _compareCursor = 0;
            _roundDamageDealt = 0;
            IncomingAttack = false;
            SelectingXRayTarget = false;
            SelectingRubTarget = false;
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
            PickHandBrand();
            ResetEnemyOpenSelection();
            RunNextCompare();
        }

        /// <summary>结算前清掉透视时的 5 张全选，再由 LockBestOpenCardsIfEnemy 锁最大 3 张（翻面亮牌，不抬起）。</summary>
        private void ResetEnemyOpenSelection()
        {
            if (Enemies == null)
            {
                return;
            }

            for (var i = 0; i < Enemies.Length; i++)
            {
                Enemies[i]?.ClearCardSelected();
            }
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
            _pendingPlayerScore = openScore;
            _pendingEnemyScore = targetScore;
            _pendingOpenerWins = ResolveCompareWithReverse(Player, openScore, enemy, targetScore);
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
                GrantDamageGold(_roundDamageDealt);
            }

            ApplyRoundCompareRelics();

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
            _pendingPlayerScore = opener != null && opener.IsPlayer ? openScore : targetScore;
            _pendingEnemyScore = opener != null && opener.IsPlayer ? targetScore : openScore;
            NotifyPlayerShowdown(openScore, _pendingOpenerWins);
            var playerWonCompare = opener != null && opener.IsPlayer
                ? _pendingOpenerWins
                : !_pendingOpenerWins;
            var enemySeat = opener != null && opener.IsPlayer ? target : opener;
            RecordCompareOutcome(enemySeat, playerWonCompare);
            if (_pendingOpenerWins)
            {
                var damage = ComputeAttackDamage(opener, openScore, target);
                TryApplyPermanentCardBonuses(openScore);
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

            var loss = ComputeAttackDamage(target, targetScore, opener);
            TryApplyPermanentCardBonuses(openScore);
            PendingAttackDamage = loss;
            AttackLevel = MapAttackLevel(targetScore.Type);
            var taken = IncomingDamageAfterMitigation(loss, target);
            LastResult = $"{target?.Name} 的{targetScore.Label}压过你的{openScore.Label}，受到 {taken} 伤害";
            Log(LastResult);
            Phase = GamePhase.WaitingAttack;
            BeginIncomingAttack(target);
            if (!AttackPlaying)
            {
                ApplyDamage(Player, loss, true, target);
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

        private int ComputeAttackDamage(SeatState attacker, HandScore score, SeatState defender = null)
        {
            if (attacker == null)
            {
                return 1;
            }

            var mag = HandTypeMagnification(score.Type);
            var ctx = RelicCombatContext.Empty;
            var talent = attacker.IsPlayer ? TalentSvc() : null;
            var firstShow = attacker.IsPlayer && _stageBetRound == 1;
            var talentMag = 0f;
            var talentAttack = 0;
            if (attacker.IsPlayer)
            {
                ctx = RelicMechanics.BuildCombatContext(
                    Run,
                    score,
                    CollectUnshownCards(attacker),
                    _rubsUsedThisHand,
                    _rubbedThisHand,
                    _rng);
                LastRelicContext = ctx;
                talentMag = TalentMechanics.SumMultiplierExtra(talent, firstShow);
                talentAttack = (int)Math.Round(TalentMechanics.SumAttackExtra(talent, score, ctx));
            }

            var relicExtra = attacker.IsPlayer ? RelicMechanics.SumMultiplierExtra(Run, score, ctx) : 0f;
            var relicAttack = attacker.IsPlayer
                ? (int)Math.Round(RelicMechanics.SumAttackExtra(Run, score, ctx))
                : 0;
            var extra = relicExtra + talentMag;
            var attackExtra = relicAttack + talentAttack;
            var flint = BossMechanics.FlintMultiplier(Run);
            var totalMag = (mag + extra) * flint;
            var relicMag = (mag + relicExtra) * flint;
            var atk = attacker.Attack + attackExtra;
            // BaseChips：亮出三张牌 ChipValue 全加（A=11），加在配置攻击力上再乘（牌型+遗物）倍率。
            var damage = HandEvaluator.ComputeAttackDamage(atk, score.BaseChips, totalMag);
            var formulaDamage = damage;
            var withoutTalent = HandEvaluator.ComputeAttackDamage(
                attacker.Attack + relicAttack,
                score.BaseChips,
                relicMag);
            var dmgPercent = 0f;
            var crit = false;
            var critMul = 0f;
            var chaseAdd = 0;
            var execute = false;
            if (attacker.IsPlayer)
            {
                var hero = ResolveHero();
                dmgPercent = TalentMechanics.SumDamagePercent(
                    talent,
                    defender,
                    Player,
                    CountAliveEnemies());
                dmgPercent += HeroMechanics.SumValue(hero, MechanismType.Damage);
                dmgPercent += RelicOutgoingDamagePercent(defender);
                dmgPercent += BossMechanics.PlayerOutgoingDamagePercent(Run, score.Type);
                if (dmgPercent != 0f)
                {
                    damage = Math.Max(1, (int)Math.Round(damage * (1f + dmgPercent)));
                }

                var critRate = TalentMechanics.CriticalRate(talent, hero)
                    + HeroMechanics.SumValue(hero, MechanismType.HeroCritical)
                    + RelicMechanics.SumValue(Run, MechanismType.HeroCritical)
                    + RelicMechanics.StackedValue(Run, MechanismType.EveryRoundEndingGetCritical, Run.CritStacks);
                critMul = TalentMechanics.CriticalDamageMultiplier(hero);
                if (critRate > 0f && _rng.NextDouble() < critRate)
                {
                    crit = true;
                    damage = Math.Max(1, (int)Math.Round(damage * critMul));
                    withoutTalent = Math.Max(1, (int)Math.Round(withoutTalent * critMul));
                }

                var original = damage;
                if (TalentMechanics.Roll(talent, MechanismType.ProOfExtraAttack, _rng))
                {
                    chaseAdd = (int)Math.Round(original * TalentBalance.ExtraAttackDamageRatio);
                    damage += chaseAdd;
                }

                if (defender != null &&
                    !defender.IsPlayer &&
                    !defender.IsBoss &&
                    TalentMechanics.IsBelowHpRatio(defender, TalentBalance.ExecuteHpRatio) &&
                    TalentMechanics.Roll(talent, MechanismType.KillingProbabilityTen, _rng))
                {
                    execute = true;
                    damage = Math.Max(damage, defender.Hp);
                }
            }

            var talentDamage = attacker.IsPlayer ? damage - withoutTalent : 0;
            _lastPlayerAttackCrit = attacker.IsPlayer && crit;
            if (!attacker.IsPlayer)
            {
                var monsterPer = BossMechanics.MonsterOutgoingDamagePercent(Run);
                if (monsterPer != 0f)
                {
                    damage = Math.Max(1, (int)Math.Round(damage * (1f + monsterPer)));
                }

                damage += BossMechanics.GoldThornExtra(Run);
            }

            if (attacker.IsPlayer && Run.UseDamageFixed > 0)
            {
                damage = Run.UseDamageFixed;
            }

            if (attacker.IsPlayer && RelicMechanics.Roll(Run, MechanismType.AllPeacePer, _rng))
            {
                Log("和平鸽：本次造成伤害变为 0");
                damage = 0;
            }

            LogAttackDamage(
                attacker,
                defender,
                score,
                relicExtra,
                relicAttack,
                attackExtra,
                mag,
                flint,
                totalMag,
                formulaDamage,
                damage,
                talentDamage,
                ctx,
                firstShow,
                talentMag,
                talentAttack,
                dmgPercent,
                crit,
                critMul,
                chaseAdd,
                execute);
            if (damage <= 0)
            {
                return 0;
            }

            return Math.Max(1, damage);
        }

        /// <summary>打玩家前的减伤：先加遗物/天赋 HeroTakeDamage，再乘百分比（英雄、条约、陷阱、差距胶囊）。闪避仍在 ApplyDamage。</summary>
        private int IncomingDamageAfterMitigation(int damage, SeatState attacker = null)
        {
            if (Run.UseNullifyIncoming)
            {
                return 0;
            }

            var take = (int)Math.Round(
                RelicMechanics.SumValue(Run, MechanismType.HeroTakeDamage) +
                TalentMechanics.SumValue(TalentSvc(), MechanismType.HeroTakeDamage));
            if (take != 0)
            {
                damage = Math.Max(1, damage + take);
            }

            var takePer = HeroMechanics.SumValue(Run, MechanismType.HeroTakeDamagePer);
            takePer += RelicMechanics.SumValue(Run, MechanismType.MonsterDamage);
            if (attacker != null && Run.LostToMonster(attacker.MonsterId))
            {
                takePer += RelicMechanics.SumValue(Run, MechanismType.Trap);
            }

            takePer += RelicMechanics.GapDamagePercent(
                Run,
                _pendingPlayerScore.Type,
                _pendingEnemyScore.Type,
                outgoing: false);
            takePer += BossMechanics.IncomingDamagePercent(Run);
            if (takePer != 0f)
            {
                damage = Math.Max(0, (int)Math.Round(damage * (1f + takePer)));
            }

            if (damage <= 0)
            {
                return 0;
            }

            return Math.Max(1, damage);
        }

        private float RelicOutgoingDamagePercent(SeatState defender)
        {
            var percent = 0f;
            if (CountAliveEnemies() <= 1)
            {
                percent += RelicMechanics.SumValue(Run, MechanismType.OneMonsterGetDamage);
            }

            if (defender != null && Run.LostToMonster(defender.MonsterId))
            {
                percent += RelicMechanics.SumValue(Run, MechanismType.Revenge);
            }

            percent += RelicMechanics.StackedValue(
                Run,
                MechanismType.DefeatGetDamage,
                Run.DefeatDmgStacks);
            percent += RelicMechanics.GapDamagePercent(
                Run,
                _pendingPlayerScore.Type,
                _pendingEnemyScore.Type,
                outgoing: true);
            percent += Run.UseDamageMulAdd;
            percent += Run.LevelWinDamageUp;
            return percent;
        }

        private void LogAttackDamage(
            SeatState attacker,
            SeatState defender,
            HandScore score,
            float relicExtra,
            int relicAttack,
            int attackExtra,
            float mag,
            float flint,
            float totalMag,
            int formulaDamage,
            int damage,
            int talentDamage,
            RelicCombatContext ctx,
            bool firstShow,
            float talentMag,
            int talentAttack,
            float dmgPercent,
            bool crit,
            float critMul,
            int chaseAdd,
            bool execute)
        {
            var atk = Math.Max(0, attacker.Attack);
            var chips = Math.Max(0, score.BaseChips);
            var effective = atk + attackExtra + chips;
            var vs = defender != null ? $"→{defender.Name}" : string.Empty;
            var beats = score.BeatsAll ? " 通杀" : string.Empty;
            var cards = FormatUsedCards(score);
            var parts = attacker.IsPlayer ? RelicMechanics.CollectMultiplierParts(Run, score, ctx) : string.Empty;
            var attackParts = attacker.IsPlayer ? RelicMechanics.CollectAttackParts(Run, score, ctx) : string.Empty;
            var talent = attacker.IsPlayer ? TalentSvc() : null;
            var talentMagParts = attacker.IsPlayer
                ? TalentMechanics.CollectMultiplierParts(talent, firstShow)
                : string.Empty;
            var talentAtkParts = attacker.IsPlayer
                ? TalentMechanics.CollectAttackParts(talent, score, ctx)
                : string.Empty;
            var talentPctParts = attacker.IsPlayer
                ? TalentMechanics.CollectDamagePercentParts(
                    talent,
                    defender,
                    Player,
                    CountAliveEnemies())
                : string.Empty;
            var talentParts = JoinLogParts(talentAtkParts, talentMagParts, talentPctParts);
            var relicText = relicExtra == 0f
                ? "遗物+0"
                : string.IsNullOrEmpty(parts)
                    ? $"遗物+{relicExtra}"
                    : $"遗物+{relicExtra} ({parts})";
            if (talentMag != 0f)
            {
                relicText += string.IsNullOrEmpty(talentMagParts)
                    ? $" 天赋+{talentMag}"
                    : $" 天赋+{talentMag} ({talentMagParts})";
            }

            var attackRelic = relicAttack == 0
                ? string.Empty
                : string.IsNullOrEmpty(attackParts)
                    ? $" + 遗物攻{relicAttack}"
                    : $" + 遗物攻{relicAttack} ({attackParts})";
            if (talentAttack != 0 && !string.IsNullOrEmpty(talentAtkParts))
            {
                attackRelic += $" + 天赋攻{talentAttack} ({talentAtkParts})";
            }
            else if (talentAttack != 0)
            {
                attackRelic += $" + 天赋攻{talentAttack}";
            }

            var contextLine = string.Empty;
            if (attacker.IsPlayer)
            {
                var unshown = FormatCardList(ctx.Unshown);
                contextLine =
                    $"  未亮出 {unshown} | 搓牌已用{ctx.RubsUsedThisHand} 剩余{ctx.PeekLeft}" +
                    $" 透视{ctx.XRayLeft} 替换{ctx.ReplaceLeft} | 幸运七x{ctx.LuckySevenHits}\n";
            }

            var talentLine = string.Empty;
            if (attacker.IsPlayer &&
                (talentDamage != 0 || talentParts.Length > 0 || dmgPercent != 0f || crit || chaseAdd != 0 || execute))
            {
                talentLine = "  ";
                if (talentDamage != 0)
                {
                    var sign = talentDamage > 0 ? "+" : string.Empty;
                    talentLine += $"天赋伤害{sign}{talentDamage}";
                    if (talentParts.Length > 0)
                    {
                        talentLine += $" ({talentParts})";
                    }

                    talentLine += " ";
                }
                else if (talentParts.Length > 0)
                {
                    talentLine += $"天赋 ({talentParts}) ";
                }

                if (dmgPercent != 0f)
                {
                    talentLine += $"伤害x{1f + dmgPercent} ";
                }

                if (crit)
                {
                    talentLine += $"暴击x{critMul} ";
                }

                if (chaseAdd != 0)
                {
                    talentLine += $"追击+{chaseAdd} ";
                }

                if (execute)
                {
                    talentLine += "斩杀";
                }

                talentLine = talentLine.TrimEnd() + "\n";
            }

            var resultLine = formulaDamage == damage
                ? $"  {effective} x {totalMag} = {damage}"
                : $"  {effective} x {totalMag} = {formulaDamage} → {damage}";
            if (attacker.IsPlayer && talentDamage != 0)
            {
                var sign = talentDamage > 0 ? "+" : string.Empty;
                resultLine += $" | 天赋伤害{sign}{talentDamage}";
            }

            AppLog.Info(
                LogChannel.Game,
                $"伤害 {attacker.Name}{vs} | {HandEvaluator.TypeName(score.Type)}{beats} {cards}\n" +
                $"  攻击{atk}{attackRelic} + 点数{chips} = {effective} | 牌型x{mag} + {relicText} | 燧石x{flint} | 倍率x{totalMag}\n" +
                contextLine +
                talentLine +
                resultLine);
        }

        private static string JoinLogParts(params string[] parts)
        {
            string text = null;
            if (parts == null)
            {
                return string.Empty;
            }

            for (var i = 0; i < parts.Length; i++)
            {
                var part = parts[i];
                if (string.IsNullOrEmpty(part))
                {
                    continue;
                }

                text = text == null ? part : text + ", " + part;
            }

            return text ?? string.Empty;
        }

        private static string FormatUsedCards(HandScore score)
        {
            return FormatCardList(score.UsedCards);
        }

        private static string FormatCardList(Card[] cards)
        {
            if (cards == null || cards.Length == 0)
            {
                return "无";
            }

            var text = cards[0].IsValid ? cards[0].DisplayName : string.Empty;
            for (var i = 1; i < cards.Length; i++)
            {
                if (!cards[i].IsValid)
                {
                    continue;
                }

                text += cards[i].DisplayName;
            }

            return string.IsNullOrEmpty(text) ? "无" : text;
        }

        public static float HandTypeMagnification(HandType type)
        {
            return HandEvaluator.TypeMultiplier(type);
        }

        private void DealPlayerLossDamage(SeatState winner)
        {
            if (winner == null || winner.IsPlayer)
            {
                return;
            }

            var score = EvaluateSeat(winner);
            var damage = HandEvaluator.ComputeDamage(score, ShowdownStake(), 1f);
            ApplyDamage(Player, damage, true, winner);
        }

        public SeatState EnemyAtVisualSlot(int visualSlot)
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

        /// <summary>
        /// 上场敌人视觉槽：player1 是中心。1 人站 player1；2 人站 player2/player3；
        /// 3 人左中右为 player2 / player1 / player3。拼牌时当前对手再站到 player1。
        /// </summary>
        public static int TableVisualSlot(int enemyIndex, int activeCount)
        {
            if (activeCount <= 1)
            {
                return 0;
            }

            if (activeCount == 2)
            {
                return enemyIndex == 0 ? 1 : 2;
            }

            if (enemyIndex == 0)
            {
                return 1;
            }

            if (enemyIndex == 1)
            {
                return 0;
            }

            return 2;
        }

        /// <summary>
        /// 下次刷新商店所需金币：首次 <see cref="GameConst.ShopRefreshFirst"/>，
        /// 之后每次 + <see cref="GameConst.ShopRefreshAfter"/>，增长次数不超过 <see cref="GameConst.ShopRefreshGoldUpNumMax"/>。
        /// </summary>
        public int ShopRefreshCost
        {
            get
            {
                var first = GameConst.IsLoaded ? Math.Max(0, GameConst.Instance.ShopRefreshFirst) : 5;
                var after = GameConst.IsLoaded ? Math.Max(0, GameConst.Instance.ShopRefreshAfter) : 3;
                var maxUp = GameConst.IsLoaded ? Math.Max(0, GameConst.Instance.ShopRefreshGoldUpNumMax) : int.MaxValue;
                var ups = Math.Min(Math.Max(0, Run.ShopRefreshCount), maxUp);
                return first + ups * after;
            }
        }

        /// <summary>会员卡免费刷新时为 0，否则为下次付费刷新价。</summary>
        public int EffectiveShopRefreshCost => Run.FreeShopRefreshLeft > 0 ? 0 : ShopRefreshCost;

        /// <summary>
        /// 可携带圣物上限：<see cref="GameBalance.MaxRelics"/> + 天赋 RelicNumMax。
        /// </summary>
        public int RelicCarryMax =>
            GameBalance.MaxRelics + (int)Math.Round(TalentMechanics.SumValue(TalentSvc(), MechanismType.RelicNumMax));

        public int EffectiveSellPrice(int relicId) => RelicMechanics.SellPrice(Run, relicId);

        public int EffectiveBuyPrice(int relicId)
        {
            var price = HeroMechanics.BuyPrice(Run, RelicConfig.Get(relicId));
            if (Run.ShopBuyDiscount <= 0f)
            {
                return price;
            }

            if (Run.ShopBuyDiscount >= 1f)
            {
                return 0;
            }

            return Math.Max(0, (int)Math.Round(price * (1f - Run.ShopBuyDiscount)));
        }

        public bool OwnsRelicConfig(int relicId) => Run.RelicConfigIds.Contains(relicId);

        public bool CanRefreshShop =>
            Phase == GamePhase.Shop &&
            HasUnownedRelicConfig() &&
            (Run.FreeShopRefreshLeft > 0 || RelicMechanics.CanAfford(Run, ShopRefreshCost));

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

            var free = Run.FreeShopRefreshLeft > 0;
            var cost = ShopRefreshCost;
            if (!free && !RelicMechanics.CanAfford(Run, cost))
            {
                Hint = "金币不足";
                Notify();
                return;
            }

            if (free)
            {
                Run.FreeShopRefreshLeft--;
                RollShopOffers();
                UnlockSvc()?.Report(ContidionType.RefreshStore);
                Log($"免费刷新商店（会员卡，下次 {ShopRefreshCost} 金币）");
                Hint = Run.FreeShopRefreshLeft > 0
                    ? $"商店已刷新，剩余 {Run.FreeShopRefreshLeft} 次免费刷新"
                    : $"商店已刷新，下次刷新 {ShopRefreshCost} 金币";
                Notify();
                return;
            }

            SpendGold(cost);
            Run.ShopRefreshCount++;
            RollShopOffers();
            UnlockSvc()?.Report(ContidionType.RefreshStore);
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

            if (Run.RelicConfigIds.Count >= RelicCarryMax)
            {
                Hint = "遗物已满";
                Notify();
                return;
            }

            var price = EffectiveBuyPrice(relicId);
            if (!RelicMechanics.CanAfford(Run, price))
            {
                Hint = RelicMechanics.HasMechanism(Run, MechanismType.Liability)
                    ? "超出白条额度"
                    : "金币不足";
                Notify();
                return;
            }

            SpendGold(price);
            Run.RelicConfigIds.Add(relicId);
            Run.ShopOfferIds.Remove(relicId);
            ApplyRelicMaxHpDelta((int)Math.Round(RelicMechanics.SumValueForRelic(relicId, MechanismType.HeroHpMax)));
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
            var sell = RelicMechanics.SellPrice(Run, relicId);
            Run.Gold += sell;
            ApplyRelicMaxHpDelta(-(int)Math.Round(RelicMechanics.SumValueForRelic(relicId, MechanismType.HeroHpMax)));
            Log($"出售遗物 {relic.Name}，获得 {sell} 金币");
            Hint = $"已出售 {relic.Name}";
            Notify();
        }

        public void UseRelic(int relicId)
        {
            if (!OwnsRelicConfig(relicId) || RelicMechanics.IsDisabled(Run, relicId))
            {
                Hint = "未拥有该遗物";
                Notify();
                return;
            }

            var relic = RelicConfig.Get(relicId);
            if (relic == null || !RelicMechanics.IsConsumable(relic))
            {
                Hint = "该装备无法使用";
                Notify();
                return;
            }

            var thisHand = RelicMechanics.RequiresComparePhase(relic);
            if (!PlayerMayUseConsumable(thisHand))
            {
                Hint = thisHand ? "当前无法在比牌前使用" : "当前无法使用该装备";
                Notify();
                return;
            }

            if (!CanApplyConsumable(relic, out var failHint))
            {
                Hint = failHint;
                Notify();
                return;
            }

            RelicMechanics.ForEachRelicEntry(relic, ApplyConsumableEntry);
            Run.RelicConfigIds.RemoveAll(id => id == relicId);
            if (Phase == GamePhase.Shop)
            {
                FillShopOffers();
            }

            Log($"使用遗物 {relic.Name}");
            Hint = $"已使用 {relic.Name}";
            Notify();
        }

        private bool CanApplyConsumable(RelicConfig relic, out string failHint)
        {
            failHint = null;
            if (relic?.MechanismId == null)
            {
                return true;
            }

            for (var i = 0; i < relic.MechanismId.Length; i++)
            {
                var entry = RelicEntryConfig.Get(relic.MechanismId[i]);
                if (entry != null &&
                    entry.Type == MechanismType.RemoveBossEntry &&
                    Run.LevelEntryIds.Count <= 0)
                {
                    failHint = "本关没有可移除的词缀";
                    return false;
                }
            }

            return true;
        }

        private void ApplyConsumableEntry(RelicEntryConfig entry)
        {
            if (entry == null)
            {
                return;
            }

            var inShop = Phase == GamePhase.Shop;
            var value = RelicMechanics.ValueAt(entry);
            switch (entry.Type)
            {
                case MechanismType.UseDamageMul:
                    Run.UseDamageMulAdd += value;
                    Log($"消耗品：本次比牌伤害 +{value:P0}");
                    break;
                case MechanismType.UseDamageFixed:
                    Run.UseDamageFixed = Math.Max(Run.UseDamageFixed, (int)Math.Round(value));
                    Log($"消耗品：本次比牌伤害固定为 {Run.UseDamageFixed}");
                    break;
                case MechanismType.HandTypeMagUp:
                {
                    var handType = HandEvaluator.FromConfigHandType((int)Math.Round(RelicMechanics.ValueAt(entry, 0)));
                    var mag = RelicMechanics.ValueAt(entry, 1);
                    Run.AddHandTypeMagBonus(handType, mag);
                    Log($"消耗品：{HandEvaluator.TypeName(handType)} 倍率永久 +{mag}");
                    break;
                }
                case MechanismType.RandomHandTypeMagUp:
                    ApplyRandomHandTypeMagUp(
                        RelicMechanics.ValueAt(entry, 0),
                        (int)Math.Round(RelicMechanics.ValueAt(entry, 1)));
                    break;
                case MechanismType.HealHpPercent:
                {
                    var healed = HealPlayer((int)Math.Round(Player.MaxHp * value));
                    Log($"消耗品：回复 {healed} HP");
                    break;
                }
                case MechanismType.MaxHpUpAndHeal:
                {
                    var maxHp = (int)Math.Round(value);
                    Run.PermanentMaxHpBonus += maxHp;
                    ApplyRelicMaxHpDelta(maxHp);
                    Log($"消耗品：永久生命上限 +{maxHp}");
                    break;
                }
                case MechanismType.NextShopDiscount:
                    Run.NextShopDiscount = Math.Max(Run.NextShopDiscount, value);
                    Log($"消耗品：下次商店购买折扣 {value:P0}");
                    break;
                case MechanismType.RemoveBossEntry:
                    Run.LevelEntryIds.Clear();
                    Run.BossShieldHitsLeft = 0;
                    Log("消耗品：已移除本关词缀");
                    break;
                case MechanismType.FirstLeopardGold:
                    Run.FirstLeopardGoldPending += (int)Math.Round(value);
                    Log($"消耗品：本局首次豹子额外金币 {value}");
                    break;
                case MechanismType.LevelWinDamageUp:
                    if (inShop)
                    {
                        Run.PendingLevelWinDamageUp += value;
                    }
                    else
                    {
                        Run.LevelWinDamageUp += value;
                    }

                    Log($"消耗品：本关获胜伤害 +{value:P0}");
                    break;
                case MechanismType.LevelHpMaxUp:
                {
                    var hpBonus = (int)Math.Round(value);
                    ApplyRelicMaxHpDelta(hpBonus);
                    if (inShop)
                    {
                        Run.PendingLevelHpMaxBonus += hpBonus;
                    }
                    else
                    {
                        Run.LevelHpMaxBonus += hpBonus;
                    }

                    Log($"消耗品：本关生命上限 +{hpBonus}");
                    break;
                }
                case MechanismType.UseRoundNullify:
                {
                    Run.UseNullifyIncoming = RelicMechanics.ValueAt(entry, 0) != 0f;
                    var nullifyHeal = HealPlayer((int)Math.Round(Player.MaxHp * RelicMechanics.ValueAt(entry, 1)));
                    Log($"消耗品：本次比牌免疫伤害，回复 {nullifyHeal} HP");
                    break;
                }
                default:
                    Log($"消耗品：未处理的机制 {entry.Type}");
                    break;
            }
        }

        private void ApplyRandomHandTypeMagUp(float mag, int count)
        {
            var types = new[]
            {
                HandType.HighCard,
                HandType.Pair,
                HandType.Straight,
                HandType.Flush,
                HandType.StraightFlush,
                HandType.ThreeOfAKind
            };
            var remain = types.Length;
            var take = Math.Min(Math.Max(0, count), remain);
            for (var i = 0; i < take; i++)
            {
                var pick = _rng.Next(i, remain);
                var tmp = types[i];
                types[i] = types[pick];
                types[pick] = tmp;
                Run.AddHandTypeMagBonus(types[i], mag);
                Log($"消耗品：{HandEvaluator.TypeName(types[i])} 倍率永久 +{mag}");
            }
        }

        private void ClearThisHandConsumables()
        {
            Run.UseDamageMulAdd = 0f;
            Run.UseDamageFixed = 0;
            Run.UseNullifyIncoming = false;
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
            ResetSkillCharges();
            Log("观看广告复活，生命已回满，技能次数已重置");
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

#if UNITY_EDITOR
        public const int EditorDebugGoldAmount = 99999;

        /// <summary>编辑器外挂：玩家免伤。ApplyDamage 生效。</summary>
        public static bool DebugGodMode { get; set; }

        /// <summary>编辑器外挂：对敌伤害直接斩杀。ApplyDamage 生效。</summary>
        public static bool DebugOneHitKill { get; set; }

        public void DebugAddGold(int amount = EditorDebugGoldAmount)
        {
            if (amount <= 0)
            {
                return;
            }

            Run.Gold += amount;
            Log($"[编辑器] 金币 +{amount}（总金币 {Run.Gold}）");
            Notify();
        }

        public void DebugFullHp()
        {
            var healed = Player.MaxHp - Player.Hp;
            if (healed <= 0)
            {
                Log("[编辑器] 血量已满");
                Notify();
                return;
            }

            Player.Hp = Player.MaxHp;
            HpSvc()?.Heal(Player.Id, healed);
            Log($"[编辑器] 回满血 +{healed}");
            Notify();
        }

        public void DebugMaxSkillCharges()
        {
            Run.PeekGoodCharges += 99;
            Run.ChaKanGoodCharges += 99;
            Run.TiHuanGoodCharges += 99;
            Log("[编辑器] 搓牌/透视/替换次数 +99（每关 / 进商店后下一手 / 复活时重置）");
            Notify();
        }

        public void DebugSkipStage()
        {
            if (Phase != GamePhase.WaitingOpen && Phase != GamePhase.WaitingRub &&
                Phase != GamePhase.RoundSettle)
            {
                Hint = "[编辑器] 当前阶段不可跳关，请回到待开牌/结算阶段";
                Log(Hint);
                Notify();
                return;
            }

            for (var i = 0; i < Enemies.Length; i++)
            {
                var enemy = Enemies[i];
                if (enemy == null || enemy.Hp <= 0)
                {
                    continue;
                }

                Log($"[编辑器] 秒杀 {enemy.Name}");
                enemy.Hp = 0;
                enemy.Status = "阵亡";
            }

            EnterShop();
        }
#endif

        /// <summary>旧摊牌 <see cref="HandEvaluator.ComputeDamage"/> 用。主路径攻击见 ComputeAttackDamage：遗物/天赋加在牌型倍率上。</summary>
        public float RelicMultiplier(HandScore score)
        {
            var extra = RelicMechanics.SumMultiplierExtra(Run, score, LastRelicContext) +
                        TalentMechanics.SumMultiplierExtra(TalentSvc(), _stageBetRound == 1);
            var flint = BossMechanics.FlintMultiplier(Run);
            return (1f + extra) * flint;
        }

        /// <summary>玩家失效花色过滤。敌人不受禁红/禁黑/禁花色计分影响。</summary>
        private void GetScoreBan(
            SeatState seat,
            out Suit? banned,
            out Suit? banned2,
            out bool banFaces,
            out Suit? banned3)
        {
            banned = null;
            banned2 = null;
            banFaces = false;
            banned3 = null;
            if (seat != null && seat.IsPlayer)
            {
                BossMechanics.GetScoreBan(Run, out banned, out banned2, out banFaces, out banned3);
            }
        }

        /// <summary>开牌结算时为敌人锁定 5 选 3 的最大牌型（会先清掉透视时的全选）。</summary>
        private void LockBestOpenCardsIfEnemy(SeatState seat)
        {
            if (seat == null || seat.IsPlayer || seat.Hand == null)
            {
                return;
            }

            GetScoreBan(seat, out var banned, out var banned2, out var banFaces, out var banned3);
            HandEvaluator.SelectBestOpen(
                seat.Hand,
                seat.CardSelected,
                CardsDealtFor(seat),
                banned,
                banFaces,
                GetHandEvalRules(seat),
                banned2,
                banned3);
        }

        /// <summary>评估座位牌型。BOSS 失效花色会先过滤玩家手牌。</summary>
        public HandScore EvaluateSeat(SeatState seat)
        {
            GetScoreBan(seat, out var banned, out var banned2, out var banFaces, out var banned3);
            var rules = GetHandEvalRules(seat);
            var score = HandEvaluator.Evaluate(
                CollectEvalCards(seat, banned, banFaces, rules, banned2, banned3),
                banned,
                banFaces,
                rules,
                banned2,
                banned3);
            if (seat != null && seat.IsPlayer)
            {
                if (RelicMechanics.HasMechanism(Run, MechanismType.SpecialTwoThreeFive))
                {
                    score = RelicMechanics.ApplyTwoThreeFive(score);
                }

                score = RelicMechanics.ApplyPlayerTypeRewrite(Run, score);
            }
            else if (seat != null && !seat.IsPlayer)
            {
                score = RelicMechanics.ApplyEnemyTypeRewrite(Run, score, _enemyDowngradeSteps);
                score = RelicMechanics.ApplyCompareRankBonus(Run, score);
            }

            return BossMechanics.ApplyChipOverride(Run, score);
        }

        private HandEvalRules GetHandEvalRules(SeatState seat)
        {
            if (seat == null || !seat.IsPlayer)
            {
                return default;
            }

            return new HandEvalRules(
                RelicMechanics.HasMechanism(Run, MechanismType.SpecialFlush),
                RelicMechanics.HasMechanism(Run, MechanismType.SpecialStraight));
        }

        private bool OpenerWinsCompare(
            SeatState opener,
            HandScore openScore,
            SeatState target,
            HandScore targetScore)
        {
            if (BossMechanics.TieLoses(Run) && openScore.CompareLevel == targetScore.CompareLevel)
            {
                if (opener != null && opener.IsPlayer && (target == null || !target.IsPlayer))
                {
                    return false;
                }

                if (target != null && target.IsPlayer && (opener == null || !opener.IsPlayer))
                {
                    return true;
                }
            }

            var cmp = openScore.CompareTo(targetScore);
            if (opener != null && opener.IsPlayer && (target == null || !target.IsPlayer))
            {
                return cmp >= 0;
            }

            if (target != null && target.IsPlayer && (opener == null || !opener.IsPlayer))
            {
                return cmp > 0;
            }

            return cmp > 0;
        }

        private bool ResolveCompareWithReverse(
            SeatState opener,
            HandScore openScore,
            SeatState target,
            HandScore targetScore)
        {
            var playerWins = OpenerWinsCompare(opener, openScore, target, targetScore);
            if (opener == null || !opener.IsPlayer || playerWins)
            {
                return playerWins;
            }

            if (!RelicMechanics.Roll(Run, MechanismType.ReverseResult, _rng))
            {
                return false;
            }

            Log("逆转沙漏：比牌结果反转，伤害按自身牌型计算");
            return true;
        }

        private void NotifyPlayerShowdown(HandScore playerScore, bool playerWon)
        {
            if (playerScore.UsedCards == null || playerScore.UsedCards.Length == 0)
            {
                return;
            }

            OnPlayerCardsShown(playerScore);
            if (playerWon)
            {
                ApplyPlayerWinGold(playerScore);
                if (_rubbedThisHand)
                {
                    UnlockSvc()?.Report(ContidionType.ShuffleCardAndVictory);
                }
            }
        }

        private void OnPlayerCardsShown(HandScore score)
        {
            if (_playerCardsShownThisRound)
            {
                return;
            }

            _playerCardsShownThisRound = true;
            ApplyIronRiceBowl();
            Run.AddHandTypeShowCount(score.Type);
            if (score.Type == HandType.ThreeOfAKind && Run.FirstLeopardGoldPending > 0)
            {
                var gold = Run.FirstLeopardGoldPending;
                Run.FirstLeopardGoldPending = 0;
                Run.Gold += gold;
                Log($"豹子精髓 +{gold} 金币（总金币 {Run.Gold}）");
            }
            if (score.Type == HandType.Straight)
            {
                UnlockSvc()?.Report(ContidionType.Straight);
            }

            if (RelicMechanics.IsNaturalTwoThreeFive(score.UsedCards))
            {
                UnlockSvc()?.Report(ContidionType.TwoThreeFive);
            }

            if (HasShownSeven(score.UsedCards))
            {
                UnlockSvc()?.Report(ContidionType.Seven);
            }
        }

        private void TryApplyPermanentCardBonuses(HandScore score)
        {
            if (_playerHandSettledThisRound)
            {
                return;
            }

            _playerHandSettledThisRound = true;
            if (RelicMechanics.HasMechanism(Run, MechanismType.EveryCardAttackForever) && score.UsedCards != null)
            {
                var delta = (int)Math.Round(RelicMechanics.SumValue(Run, MechanismType.EveryCardAttackForever));
                if (delta != 0)
                {
                    for (var i = 0; i < score.UsedCards.Length; i++)
                    {
                        Run.AddRankAttackBonus(score.UsedCards[i].Rank, delta);
                    }
                }
            }

            RelicMechanics.ForEachEntry(Run, (_, entry) =>
            {
                if (entry.Type != MechanismType.ProOfUpCardType)
                {
                    return;
                }

                if (_rng.NextDouble() < RelicMechanics.ValueAt(entry))
                {
                    Run.AddHandTypeMagBonus(score.Type, 1f);
                    Log($"天使：{HandEvaluator.TypeName(score.Type)} 倍率永久 +1");
                }
            });
        }

        private void ApplyPlayerWinGold(HandScore score)
        {
            var cards = score.UsedCards;
            if (cards == null)
            {
                return;
            }

            var allFace = RelicMechanics.HasMechanism(Run, MechanismType.AllCardIsHeadCard);
            var headChance = RelicMechanics.SumValue(Run, MechanismType.ProOfHeadCardFunds);
            var nineGold = RelicMechanics.SumValue(Run, MechanismType.SpecialNineCard);
            var gained = 0;
            for (var i = 0; i < cards.Length; i++)
            {
                var card = cards[i];
                if ((allFace || card.IsFace) && headChance > 0f && _rng.NextDouble() < headChance)
                {
                    gained += 1;
                }

                if (card.Rank == Rank.Nine && nineGold != 0f)
                {
                    gained += (int)Math.Round(nineGold);
                }
            }

            if (gained == 0)
            {
                return;
            }

            Run.Gold += gained;
            Log($"亮牌结算 +{gained} 金币（总金币 {Run.Gold}）");
        }

        private void ApplyKillSellBonus()
        {
            RelicMechanics.ForEachEntry(Run, (relic, entry) =>
            {
                if (relic == null || entry.Type != MechanismType.KillAfterSellingPrice)
                {
                    return;
                }

                var delta = (int)Math.Round(RelicMechanics.ValueAt(entry));
                if (delta != 0)
                {
                    Run.AddRelicSellPriceBonus(relic.Id, delta);
                }
            });
        }

        private void SpendGold(int amount)
        {
            if (amount <= 0)
            {
                return;
            }

            Run.Gold -= amount;
            Run.GoldSpentThisRun += amount;
        }

        /// <summary>搓牌/透视/替换次数：开新关、进商店后下一手、复活时重置；本关内跨手保留。</summary>
        private void ResetSkillCharges()
        {
            var rubDelta = (int)Math.Round(
                RelicMechanics.SumValue(Run, MechanismType.RubbingCardsNum) +
                HeroMechanics.SumValue(Run, MechanismType.RubbingCardsNum));
            Run.PeekGoodCharges = Math.Max(
                0,
                GameBalance.SkillRubUses + Run.BonusRubCharges + rubDelta - BossMechanics.RubChargeDelta(Run));
            var xrayDelta = (int)Math.Round(RelicMechanics.SumValue(Run, MechanismType.PerspectiveNum));
            Run.ChaKanGoodCharges = GameBalance.SkillXRayUses + Run.BonusXRayCharges + xrayDelta;
            Run.TiHuanGoodCharges = GameBalance.SkillReplaceUses + Run.BonusReplaceCharges;
        }

        /// <summary>开新关：玩家满血读英雄表，通关进下一关时继承残血。怪物血量读关卡配置。按 <c>LevelEntryNum</c> 随机机制。</summary>
        private void StartStage(bool inheritPlayerHp)
        {
            Run.AdsLoanThisStage = 0;
            Run.AdsReviveThisStage = 0;
            Run.AdsExtraRubThisStage = 0;
            Run.StrawUsedThisStage = false;
            Run.StrawHealPending = 0f;
            Run.DoubleGoldThisStage = false;
            Run.PeekSuitUsed = false;
            Run.PeekSuitIndex = -1;
            Run.PeekedSuit = null;
            Run.DisabledRelicIds.Clear();
            Run.DisabledConsumable = null;
            Run.ExtraRubCharges = 0;
            Run.StolenAttack = 0;
            Run.HandBrandIndex = -1;
            Run.BossShieldHitsLeft = 0;
            Run.ShopBuyDiscount = 0f;
            Run.LevelWinDamageUp = Run.PendingLevelWinDamageUp;
            Run.PendingLevelWinDamageUp = 0f;
            Run.LevelHpMaxBonus = Run.PendingLevelHpMaxBonus;
            Run.PendingLevelHpMaxBonus = 0;
            ClearThisHandConsumables();
            _stageBetRound = 0;
            _loanCourageBonus = 0;
            _shopGoldGranted = 0;
            PickLevelEntries();
            if (AppServices.IsReady)
            {
                AppServices.Resolve<IScoreService>().BeginStage();
            }

            ApplyHeroToPlayer(inheritPlayerHp);
            var enemyCount = ApplyLevelEnemies();

            var entries = BossMechanics.ResolveAll(Run);
            var title = Run.HasBoss
                ? $"第 {Run.Stage} 关 BOSS"
                : $"第 {Run.Stage} 关 · {enemyCount} 名敌人";
            if (entries.Count > 0)
            {
                title += " · " + JoinEntryNames(entries);
            }

            Log(title);
            for (var i = 0; i < entries.Count; i++)
            {
                if (!string.IsNullOrEmpty(entries[i].Desc))
                {
                    Log(entries[i].Desc);
                }
            }

            ResetSkillCharges();
            StartRound();
        }

        private void PickLevelEntries()
        {
            var snapshot = LevelSvc()?.Current;
            Run.HasBoss = snapshot != null ? snapshot.HasBoss : GameBalance.IsBossStage(Run.Stage);
            var count = snapshot != null ? snapshot.LevelEntryNum : 0;
            BossMechanics.PickRandomIds(_rng, count, Run.LevelEntryIds);
            Run.BossShieldHitsLeft = BossMechanics.ShieldHits(Run);
        }

        private static string JoinEntryNames(List<BossEntryConfig> entries)
        {
            if (entries == null || entries.Count == 0)
            {
                return string.Empty;
            }

            var names = new string[entries.Count];
            for (var i = 0; i < entries.Count; i++)
            {
                names[i] = entries[i] != null ? entries[i].Name : string.Empty;
            }

            return string.Join("、", names);
        }

        /// <summary>重置本手状态并发牌，然后直接看牌进入开牌阶段。技能次数按关卡保留，不在这里重置。</summary>
        private void StartRound()
        {
            CardsRevealed = false;
            Pot = 0;
            AdvanceStageBetRound();
            if (TryFailRoundLimit())
            {
                return;
            }

            _rubsUsedThisHand = 0;
            _rubbedThisHand = false;
            _playerCardsShownThisRound = false;
            _playerHandSettledThisRound = false;
            _amuletUsedThisRound = false;
            _ironRiceBowlGranted = false;
            _enemyDowngradeSteps = 0;
            _roundCompareWins = 0;
            _roundCompareLosses = 0;
            _roundKills = 0;
            _lastPlayerAttackCrit = false;
            _pendingDamageSource = null;
            LastRelicContext = RelicCombatContext.Empty;
            Run.HandBrandIndex = -1;
            _streetsWithoutRaise = 0;
            _bettingRound = 1;
            _streetHadRaise = false;
            _playerActedThisStreet = false;
            _pendingRubIndex = -1;
            LastResult = string.Empty;
            Run.RubsLeft = 0;
            SelectingOpenTarget = false;
            SelectingXRayTarget = false;
            SelectingRubTarget = false;
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
            ApplyRelicDisable();
            ApplyAttackSteal();
            ApplyMonsterRegen();
            _deck = new Deck(_rng);
            DealAll();
            ApplyRoundStartRelics();
            EnterOpenReady();
        }

        /// <summary>每人发牌。玩家默认 5 张，手牌压缩可降到 4。未上场的敌人不发。一副牌不重复。</summary>
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

                var count = CardsDealtFor(seat);
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
            if (_deck.TryDrawMatching(card => RubCardAllowed(card, original, false), out var next))
            {
                return next;
            }

            return _deck.TryDraw(out next) ? next : original;
        }

        private bool RubCardAllowed(Card card, Card original, bool keepSuit)
        {
            if (!card.IsValid || card.Equals(original))
            {
                return false;
            }

            if (BossMechanics.IsRubBanned(Run, card))
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

                var count = CardsDealtFor(seat);
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
            SelectingRubTarget = false;
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
            SelectingRubTarget = false;

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

            var scare = false;
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
            _pendingPlayerScore = opener != null && opener.IsPlayer ? openScore : targetScore;
            _pendingEnemyScore = opener != null && opener.IsPlayer ? targetScore : openScore;
            _pendingOpenerWins = ResolveCompareWithReverse(opener, openScore, target, targetScore);
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
            NotifyPlayerShowdown(playerScore, playerWin);
            TryApplyPermanentCardBonuses(playerScore);
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
            var playerScore = opener != null && opener.IsPlayer
                ? openScore
                : target != null && target.IsPlayer
                    ? targetScore
                    : default;
            var playerWon = opener != null && opener.IsPlayer
                ? _pendingOpenerWins
                : target != null && target.IsPlayer && !_pendingOpenerWins;
            if ((opener != null && opener.IsPlayer) || (target != null && target.IsPlayer))
            {
                NotifyPlayerShowdown(playerScore, playerWon);
                TryApplyPermanentCardBonuses(playerScore);
            }

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

        private Card[] CollectEvalCards(
            SeatState seat,
            Suit? banned,
            bool banFaces,
            HandEvalRules rules,
            Suit? banned2,
            Suit? banned3 = null)
        {
            if (seat == null || seat.Hand == null)
            {
                return Array.Empty<Card>();
            }

            if (seat.CountSelectedCards() == GameBalance.OpenHandSize)
            {
                if (seat.IsPlayer && Run.HandBrandIndex >= 0)
                {
                    return CopySelectedExcept(seat, Run.HandBrandIndex);
                }

                return HandEvaluator.CopySelectedCards(seat.Hand, seat.CardSelected);
            }

            if (!seat.IsPlayer)
            {
                return HandEvaluator.CopyBestOpenCards(
                    seat.Hand,
                    CardsDealtFor(seat),
                    banned,
                    banFaces,
                    rules,
                    banned2,
                    banned3);
            }

            return HandEvaluator.CopySelectedCards(seat.Hand, seat.CardSelected);
        }

        private static Card[] CopySelectedExcept(SeatState seat, int skipIndex)
        {
            if (seat?.Hand == null || seat.CardSelected == null)
            {
                return Array.Empty<Card>();
            }

            var picked = new List<Card>(GameBalance.OpenHandSize);
            var limit = Math.Min(seat.Hand.Length, seat.CardSelected.Length);
            for (var i = 0; i < limit; i++)
            {
                if (i == skipIndex || !seat.CardSelected[i])
                {
                    continue;
                }

                picked.Add(seat.Hand[i]);
            }

            return picked.ToArray();
        }

        private Card[] CollectUnshownCards(SeatState seat)
        {
            if (seat?.Hand == null)
            {
                return Array.Empty<Card>();
            }

            var picked = new List<Card>(2);
            var dealt = Math.Min(CardsDealtFor(seat), seat.Hand.Length);
            var selected = seat.CardSelected;
            for (var i = 0; i < dealt; i++)
            {
                if (selected != null && i < selected.Length && selected[i])
                {
                    continue;
                }

                if (seat.Hand[i].IsValid)
                {
                    picked.Add(seat.Hand[i]);
                }
            }

            return picked.ToArray();
        }

        private string FormatPlayerHand()
        {
            var parts = new string[PlayerDealCount];
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
                if (IsLastLevel)
                {
                    CompleteLastLevel();
                    return;
                }

                EnterShop();
                return;
            }

            Run.MagnifierThisRound = false;
            StartRound();
        }

        private int ApplyDamage(SeatState target, int damage, bool main, SeatState attacker = null)
        {
            if (target == null)
            {
                return 0;
            }

#if UNITY_EDITOR
            if (DebugGodMode && ReferenceEquals(target, Player))
            {
                Log($"[编辑器] 无敌模式：{target.Name} 免疫 {damage} 伤害");
                return 0;
            }

            if (DebugOneHitKill && !ReferenceEquals(target, Player))
            {
                damage = Math.Max(damage, target.Hp);
            }
#endif

            if (!target.IsPlayer && attacker != null && attacker.IsPlayer)
            {
                if (TryBossTimidImmune(target))
                {
                    return 0;
                }

                if (TryMonsterEvade(target, attacker))
                {
                    return 0;
                }

                if (TryBossShieldImmune(target))
                {
                    return 0;
                }
            }

            if (ReferenceEquals(target, Player))
            {
                if (Run.UseNullifyIncoming)
                {
                    Log("消耗品：本次比牌免疫伤害");
                    target.Banner = "免疫";
                    return 0;
                }

                var miss = HeroMechanics.SumValue(Run, MechanismType.MissDamagePer)
                    + RelicMechanics.SumValue(Run, MechanismType.MissDamagePer)
                    + RelicMechanics.StackedValue(
                        Run,
                        MechanismType.EveryRoundEndingGetEvade,
                        Run.EvadeStacks);
                if (miss > 0f && _rng.NextDouble() < miss)
                {
                    Log($"闪避：{target.Name} 免疫 {damage} 伤害");
                    target.Banner = "闪避";
                    ApplyDodgeCounter(attacker);
                    return 0;
                }

                if (!_amuletUsedThisRound && RelicMechanics.HasMechanism(Run, MechanismType.MissFirstDamage))
                {
                    _amuletUsedThisRound = true;
                    Log("护身符：免疫本回合第一次伤害");
                    target.Banner = "护身符";
                    return 0;
                }

                if (RelicMechanics.Roll(Run, MechanismType.AllPeacePer, _rng))
                {
                    Log("和平鸽：本次受到伤害变为 0");
                    target.Banner = "和平鸽";
                    return 0;
                }

                damage = IncomingDamageAfterMitigation(damage, attacker);
                if (damage <= 0)
                {
                    return 0;
                }

                if (damage >= target.Hp &&
                    !Run.StrawUsedThisStage &&
                    RelicMechanics.HasMechanism(Run, MechanismType.AstrawToClutchAt))
                {
                    Run.StrawUsedThisStage = true;
                    Run.StrawHealPending = RelicMechanics.SumValue(Run, MechanismType.AstrawToClutchAt);
                    var survived = Math.Max(0, target.Hp - 1);
                    target.Hp = 1;
                    HpSvc()?.Damage(target.Id, survived);
                    target.Banner = main ? $"-{survived}" : $"溅射 -{survived}";
                    Log($"救命稻草：血量降至 1，下次造成伤害回复 {Run.StrawHealPending:P0}");
                    ApplyBounce(attacker, survived);
                    TryGrantTakeDamageGold(survived);
                    ApplyLifeSiphon(attacker, survived);
                    return survived;
                }
            }

            if (damage <= 0)
            {
                return 0;
            }

            var dealt = Math.Min(target.Hp, Math.Max(1, damage));
            if (dealt < damage)
            {
                AppLog.Info(LogChannel.Game, $"伤害截断 {target.Name} 计算{damage} → 实际{dealt}（当前HP {target.Hp}）");
            }

            if (!target.IsPlayer && dealt >= target.Hp && BossMechanics.CanSecondWind(Run, target))
            {
                var lost = target.Hp;
                target.SecondWindUsed = true;
                var reviveHp = BossMechanics.SecondWindHp(Run, target.MaxHp);
                ApplySeatHp(target, reviveHp, target.MaxHp);
                target.Banner = "复活";
                Log($"不灭传说：{target.Name} 以 {reviveHp} HP 复活");
                TryApplyPhaseRage(target);
                if (target.IsBoss)
                {
                    RefreshBossRageAttack(target);
                }

                if (main && attacker != null && attacker.IsPlayer)
                {
                    ApplyCurseBodySelfDamage();
                    ApplyThornShellSelfDamage(lost);
                }

                return lost;
            }

            target.Hp -= dealt;
            HpSvc()?.Damage(target.Id, dealt);
            target.Banner = main ? $"-{dealt}" : $"溅射 -{dealt}";
            Log($"攻击 {target.Name} {dealt}，剩余 HP {target.Hp}");
            if (ReferenceEquals(target, Player) && dealt > 0)
            {
                TryGrantTakeDamageGold(dealt);
                ApplyBounce(attacker, dealt);
                ApplyLifeSiphon(attacker, dealt);
            }

            if (target.Hp <= 0)
            {
                target.Hp = 0;
                target.Status = "阵亡";
                Log($"击杀 {target.Name}");
                if (!target.IsPlayer)
                {
                    _roundKills++;
                    ScoreSvc()?.TrackStageKill();
                    ApplyKillSellBonus();
                    ApplyTalentKillRewards();
                    UnlockSvc()?.Report(ContidionType.KillMonster);
                    ApplyVengefulSoulOnKill();
                }
            }
            else
            {
                TryApplyPhaseRage(target);
            }

            if (target.IsBoss)
            {
                RefreshBossRageAttack(target);
            }

            if (main && attacker != null && attacker.IsPlayer && !target.IsPlayer)
            {
                ApplyCurseBodySelfDamage();
                ApplyThornShellSelfDamage(dealt);
            }

            return dealt;
        }

        private void TryGrantTakeDamageGold(int dealt)
        {
            if (dealt <= 0)
            {
                return;
            }

            var gold = (int)Math.Round(RelicMechanics.SumValue(Run, MechanismType.TakeDamageGetFunds));
            if (gold != 0)
            {
                Run.Gold += gold;
                Log($"补偿金 +{gold} 金币（总金币 {Run.Gold}）");
            }
        }

        private void ApplyBounce(SeatState attacker, int dealt)
        {
            if (attacker == null || attacker.IsPlayer || !attacker.Alive || dealt <= 0)
            {
                return;
            }

            var bounce = (int)Math.Round(RelicMechanics.SumValue(Run, MechanismType.BounceDamage));
            if (bounce <= 0)
            {
                return;
            }

            Log($"反击拳套：反弹 {bounce} 伤害给 {attacker.Name}");
            ApplyDamage(attacker, bounce, false, Player);
        }

        private void ApplyDodgeCounter(SeatState attacker)
        {
            if (attacker == null || attacker.IsPlayer || !attacker.Alive)
            {
                return;
            }

            RelicMechanics.ForEachEntry(Run, (_, entry) =>
            {
                if (entry.Type != MechanismType.MissGetDamage)
                {
                    return;
                }

                var dmg = RelicMechanics.AttackPowerDamage(entry, Player.Attack);
                if (dmg <= 0)
                {
                    return;
                }

                Log($"武林秘籍：闪避反击 {dmg}");
                ApplyDamage(attacker, dmg, false, Player);
            });
        }

        private int ApplyCriticalAoe(SeatState mainTarget)
        {
            if (!_lastPlayerAttackCrit)
            {
                return 0;
            }

            var dealt = 0;
            RelicMechanics.ForEachEntry(Run, (_, entry) =>
            {
                if (entry.Type != MechanismType.CriticalAoe)
                {
                    return;
                }

                var dmg = RelicMechanics.AttackPowerDamage(entry, Player.Attack);
                if (dmg <= 0)
                {
                    return;
                }

                for (var i = 0; i < Enemies.Length; i++)
                {
                    if (Enemies[i] == mainTarget || !Enemies[i].Alive)
                    {
                        continue;
                    }

                    Log($"刺客秘籍：暴击溅射 {Enemies[i].Name} {dmg}");
                    dealt += ApplyDamage(Enemies[i], dmg, false, Player);
                }
            });
            return dealt;
        }

        private void TryApplyStrawHeal(int dealt)
        {
            if (dealt <= 0 || Run.StrawHealPending <= 0f)
            {
                return;
            }

            var heal = HealPlayer((int)Math.Round(dealt * Run.StrawHealPending));
            Run.StrawHealPending = 0f;
            if (heal > 0)
            {
                Log($"救命稻草回血 +{heal} HP（当前 {Player.Hp}/{Player.MaxHp}）");
            }
        }

        private void RecordCompareOutcome(SeatState enemy, bool playerWon)
        {
            if (enemy == null || enemy.IsPlayer)
            {
                return;
            }

            if (playerWon)
            {
                _roundCompareWins++;
                return;
            }

            _roundCompareLosses++;
            if (RelicMechanics.HasMechanism(Run, MechanismType.Revenge) ||
                RelicMechanics.HasMechanism(Run, MechanismType.Trap))
            {
                Run.RememberLostTo(enemy.MonsterId);
            }

            if (RelicMechanics.HasMechanism(Run, MechanismType.DefeatGetMagnification))
            {
                Run.DefeatMagStacks++;
            }

            if (RelicMechanics.HasMechanism(Run, MechanismType.DefeatGetDamage))
            {
                Run.DefeatDmgStacks++;
            }
            ApplyDefeatBadges();
        }

        private void ApplyDefeatBadges()
        {
            var atk = (int)Math.Round(RelicMechanics.SumValue(Run, MechanismType.DefeatGetAttack));
            if (atk != 0)
            {
                Run.PermanentAttackBonus += atk;
                Player.Attack = Math.Max(0, Player.Attack + atk);
                Log($"勇气徽章：永久攻击 +{atk}");
            }

            var hp = (int)Math.Round(RelicMechanics.SumValue(Run, MechanismType.DefeatGetHpMax));
            if (hp != 0)
            {
                Run.PermanentMaxHpBonus += hp;
                ApplyRelicMaxHpDelta(hp);
                Log($"激励徽章：血上限 +{hp}");
            }
        }

        private void ApplyRoundCompareRelics()
        {
            if (_roundCompareWins + _roundCompareLosses <= 0)
            {
                return;
            }

            if (_roundCompareLosses == 0)
            {
                var atk = (int)Math.Round(RelicMechanics.SumValue(Run, MechanismType.SteppingStone));
                if (atk != 0)
                {
                    Run.PermanentAttackBonus += atk;
                    Player.Attack = Math.Max(0, Player.Attack + atk);
                    Log($"垫脚石：永久攻击 +{atk}");
                }
            }

            if (_roundCompareWins == 0)
            {
                var gold = (int)Math.Round(RelicMechanics.SumValue(Run, MechanismType.DefeatAllGetGoldAndReplyHp));
                var heal = (int)Math.Round(RelicMechanics.SumValue(Run, MechanismType.DefeatAllGetGoldAndReplyHp, 1));
                if (gold != 0)
                {
                    Run.Gold += gold;
                    Log($"后备计划 +{gold} 金币（总金币 {Run.Gold}）");
                }

                if (heal != 0)
                {
                    HealPlayer(heal);
                    Log($"后备计划回复 {heal} HP");
                }
            }
        }

        private void ApplyIronRiceBowl()
        {
            if (_ironRiceBowlGranted)
            {
                return;
            }

            var gold = (int)Math.Round(RelicMechanics.SumValue(Run, MechanismType.IronRiceBowl));
            if (gold == 0)
            {
                return;
            }

            _ironRiceBowlGranted = true;
            Run.Gold += gold;
            Log($"铁饭碗 +{gold} 金币（总金币 {Run.Gold}）");
        }

        private void ApplyRoundStartRelics()
        {
            var gold = (int)Math.Round(RelicMechanics.SumValue(Run, MechanismType.EveryRoundGetGold));
            if (gold != 0)
            {
                Run.Gold += gold;
                Log($"小钱包 +{gold} 金币（总金币 {Run.Gold}）");
            }

            var nobleHp = RelicMechanics.SumValue(Run, MechanismType.NobleBadge);
            var nobleGold = (int)Math.Round(RelicMechanics.SumValue(Run, MechanismType.NobleBadge, 1));
            if (nobleGold != 0 && Player.MaxHp > 0 && Player.Hp > Player.MaxHp * nobleHp)
            {
                Run.Gold += nobleGold;
                Log($"高贵徽章 +{nobleGold} 金币（总金币 {Run.Gold}）");
            }

            var hpMax = (int)Math.Round(RelicMechanics.SumValue(Run, MechanismType.EveryRoundGetHpMax));
            if (hpMax != 0)
            {
                Run.PermanentMaxHpBonus += hpMax;
                ApplyRelicMaxHpDelta(hpMax);
                Log($"永恒之心：血上限 +{hpMax}");
            }

            _enemyDowngradeSteps = 0;
            RelicMechanics.ForEachEntry(Run, (_, entry) =>
            {
                if (entry.Type != MechanismType.DownGrade)
                {
                    return;
                }

                if (_rng.NextDouble() >= RelicMechanics.ValueAt(entry))
                {
                    return;
                }

                var steps = Math.Max(1, (int)Math.Round(RelicMechanics.ValueAt(entry, 1)));
                _enemyDowngradeSteps += steps;
                Log($"好运来：本回合敌人牌型 -{steps}");
            });

            RelicMechanics.ForEachEntry(Run, (_, entry) =>
            {
                if (entry.Type != MechanismType.AdmissionTicket)
                {
                    return;
                }

                var dmg = RelicMechanics.AttackPowerDamage(entry, Player.Attack);
                if (dmg <= 0)
                {
                    return;
                }

                for (var i = 0; i < Enemies.Length; i++)
                {
                    if (!Enemies[i].Alive)
                    {
                        continue;
                    }

                    Log($"入场券：对 {Enemies[i].Name} 造成 {dmg}");
                    ApplyDamage(Enemies[i], dmg, true, Player);
                }
            });
        }

        private void ApplyRoundEndRelics()
        {
            if (_roundDamageDealt <= 0)
            {
                var gold = (int)Math.Round(RelicMechanics.SumValue(Run, MechanismType.DefeatAllGetGold));
                if (gold != 0)
                {
                    Run.Gold += gold;
                    Log($"记账本 +{gold} 金币（总金币 {Run.Gold}）");
                }
            }

            RelicMechanics.ForEachEntry(Run, (_, entry) =>
            {
                if (entry.Type != MechanismType.EveryRoundEndingGetGoldPer)
                {
                    return;
                }

                if (_rng.NextDouble() >= RelicMechanics.ValueAt(entry))
                {
                    return;
                }

                var gold = (int)Math.Round(RelicMechanics.ValueAt(entry, 1));
                if (gold == 0)
                {
                    return;
                }

                Run.Gold += gold;
                Log($"幸运草 +{gold} 金币（总金币 {Run.Gold}）");
            });

            var interestUnit = RelicMechanics.SumValue(Run, MechanismType.Interest);
            var interestGold = (int)Math.Round(RelicMechanics.SumValue(Run, MechanismType.Interest, 1));
            if (interestUnit > 0f && interestGold != 0 && Run.Gold > 0)
            {
                var gain = (int)Math.Floor(Run.Gold / (double)interestUnit) * interestGold;
                if (gain != 0)
                {
                    Run.Gold += gain;
                    Log($"利息 +{gain} 金币（总金币 {Run.Gold}）");
                }
            }

            var peekUnit = RelicMechanics.SumValue(Run, MechanismType.Abacus);
            var peekGold = (int)Math.Round(RelicMechanics.SumValue(Run, MechanismType.Abacus, 1));
            if (peekUnit > 0f && peekGold != 0 && Run.PeekGoodCharges > 0)
            {
                var gain = (int)Math.Floor(Run.PeekGoodCharges / peekUnit) * peekGold;
                if (gain != 0)
                {
                    Run.Gold += gain;
                    Log($"小算盘 +{gain} 金币（总金币 {Run.Gold}）");
                }
            }

            var potRatio = RelicMechanics.SumValue(Run, MechanismType.DamageTurnToGold);
            if (potRatio > 0f && _roundDamageDealt > 0)
            {
                var gain = (int)Math.Floor(_roundDamageDealt * (double)potRatio);
                if (gain != 0)
                {
                    Run.Gold += gain;
                    Log($"聚宝盆 +{gain} 金币（总金币 {Run.Gold}）");
                }
            }

            var thermoHp = RelicMechanics.SumValue(Run, MechanismType.ThermosCup);
            var thermoHeal = (int)Math.Round(RelicMechanics.SumValue(Run, MechanismType.ThermosCup, 1));
            if (thermoHeal != 0 && Player.MaxHp > 0 && Player.Hp < Player.MaxHp * thermoHp)
            {
                HealPlayer(thermoHeal);
                Log($"保温杯回复 {thermoHeal} HP");
            }

            TryAddCappedStack(MechanismType.EveryRoundEndingGetCritical, ref Run.CritStacks, "暴击拳套");
            TryAddCappedStack(MechanismType.EveryRoundEndingGetEvade, ref Run.EvadeStacks, "运动鞋");

            if (_roundKills <= 0 && RelicMechanics.HasMechanism(Run, MechanismType.NoKillMonsterGetMagnification))
            {
                var mag = RelicMechanics.SumValue(Run, MechanismType.NoKillMonsterGetMagnification);
                if (mag != 0f)
                {
                    Run.PracticeMagForever += mag;
                    Log($"练习卷：永久倍率 +{mag}");
                }
            }
        }

        private void TryAddCappedStack(MechanismType type, ref int stacks, string name)
        {
            if (!RelicMechanics.HasMechanism(Run, type))
            {
                return;
            }

            var cap = (int)Math.Round(RelicMechanics.SumValue(Run, type, 1));
            if (cap <= 0)
            {
                cap = int.MaxValue;
            }

            if (stacks >= cap)
            {
                return;
            }

            stacks++;
            Log($"{name}叠层 {stacks}/{cap}");
        }

        private void ApplyBankruptcy(bool playerWon, SeatState winner)
        {
            var ratio = GameBalance.RescueRatio;
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
                    UnlockSvc()?.Report(ContidionType.KillMonster);
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
            ClearThisHandConsumables();
            if (Player.Hp <= 0)
            {
                Phase = GamePhase.StageFail;
                Hint = LastResult + "\n生命耗尽。可看广告复活，或重开本关。";
                Notify();
                return;
            }

            ApplyEveryRoundHpUp();
            ApplyRoundEndRelics();
            ApplyTalentRoundGold();
            ApplyMonsterGrow();
            Phase = GamePhase.RoundSettle;
            if (!AnyEnemyAlive())
            {
                Hint = LastResult + (IsLastLevel
                    ? "\n已击杀全部敌人，点击进入总结算"
                    : "\n已击杀全部敌人，点击进入商店");
            }
            else
            {
                Hint = LastResult + "\n点击「下一局」继续";
            }

            Notify();
        }

        private void EnterShop()
        {
            ApplyTalentStageEndHeal();
            var gold = GrantStageGold();
            Phase = GamePhase.Shop;
            Run.ShopRefreshCount = 0;
            Run.FreeShopRefreshLeft = (int)Math.Round(RelicMechanics.SumValue(Run, MechanismType.FreeShopRefresh));
            if (Run.NextShopDiscount > 0f)
            {
                Run.ShopBuyDiscount = Run.NextShopDiscount;
                Run.NextShopDiscount = 0f;
                Log($"商店折扣：购买价格 -{Run.ShopBuyDiscount:P0}");
            }
            else
            {
                Run.ShopBuyDiscount = 0f;
            }
            RollShopOffers();
            Hint = $"关卡胜利！通关获得 {gold} 金币。购买道具后进入下一关。";
            LastResult = Hint;
            Notify();
        }

        private void CompleteLastLevel()
        {
            ApplyTalentStageEndHeal();
            GrantStageGold();
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

        private int GrantStageGold()
        {
            var score = ScoreSvc();
            var current = LevelSvc()?.Current;
            var gold = current != null ? Math.Max(0, current.GetGold) : 0;
            var goldPer = HeroMechanics.SumValue(Run, MechanismType.GetGoldAfterLevel);
            if (goldPer != 0f)
            {
                gold = Math.Max(0, (int)Math.Round(gold * (1f + goldPer)));
            }

            gold += CountStageKills() * KillMonsterGoldBonus();
            if (Run.DoubleGoldThisStage)
            {
                gold *= 2;
            }

            _shopGoldGranted = gold;
            Run.Gold += gold;
            var stage = score != null ? score.Current.Stage : 0;
            var total = score != null ? score.Current.Total : 0;
            Log($"关卡结算：通关 +{gold} 金币（本关积分 {stage}，章节累计 {total}，总金币 {Run.Gold}）");
            return gold;
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

        /// <summary>从勇气值扣下注。看过牌的座位付双倍。</summary>
        private bool TryCommitUnits(SeatState seat, int units, out int paid)
        {
            units = Math.Max(units, seat.StreetUnits);
            var cost = CostFor(seat, units) - CostFor(seat, seat.StreetUnits);
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
            maxHp += (int)Math.Round(
                RelicMechanics.SumValue(Run, MechanismType.HeroHpMax) +
                TalentMechanics.SumValue(TalentSvc(), MechanismType.HeroHpMax) +
                Run.PermanentMaxHpBonus);
            maxHp += Run.LevelHpMaxBonus;
            var hp = inheritHp ? Math.Min(Math.Max(0, Player.Hp), maxHp) : maxHp;
            var fragile = BossMechanics.FragileBodyMaxHpPercent(Run);
            if (fragile != 0f)
            {
                maxHp = Math.Max(1, (int)Math.Round(maxHp * (1f + fragile)));
                hp = inheritHp ? Math.Min(Math.Max(0, Player.Hp), maxHp) : maxHp;
            }

            ApplySeatHp(Player, hp, maxHp);
            var attack = hero != null ? Math.Max(0, hero.HeroDamage) : 0;
            attack += (int)Math.Round(TalentMechanics.SumValue(TalentSvc(), MechanismType.HeroAttack));
            attack += Run.PermanentAttackBonus;
            Player.Attack = Math.Max(0, attack);
            Player.Icon = hero != null ? hero.Icon : null;
        }

        private void ApplyRelicMaxHpDelta(int delta)
        {
            if (delta == 0 || Player == null)
            {
                return;
            }

            var maxHp = Math.Max(1, Player.MaxHp + delta);
            var hp = delta > 0 ? Player.Hp + delta : Math.Min(Player.Hp, maxHp);
            if (hp < 1)
            {
                hp = 1;
            }

            ApplySeatHp(Player, hp, maxHp);
        }

        private void ApplyEveryRoundHpUp()
        {
            HealPlayer((int)Math.Round(RelicMechanics.SumValue(Run, MechanismType.HeroHpReplyEveryRoundEnding)));
        }

        private void ApplyBloodSucking(int dealt)
        {
            if (dealt <= 0)
            {
                return;
            }

            var ratio = RelicMechanics.SumValue(Run, MechanismType.BloodSucking);
            var heal = HealPlayer((int)Math.Round(dealt * ratio));
            if (heal > 0)
            {
                AppLog.Info(LogChannel.Game, $"吸血 {dealt} x {ratio} → +{heal} HP（当前 {Player.Hp}/{Player.MaxHp}）");
            }
        }

        private int HealPlayer(int amount)
        {
            if (amount <= 0 || Player == null || Player.Hp <= 0)
            {
                return 0;
            }

            if (BossMechanics.HealBlocked(Run))
            {
                return 0;
            }

            var mul = BossMechanics.HealMultiplier(Run);
            if (mul != 1f)
            {
                amount = (int)Math.Round(amount * mul);
            }

            if (amount <= 0)
            {
                return 0;
            }

            var before = Player.Hp;
            var hp = Math.Min(Player.MaxHp, Player.Hp + amount);
            ApplySeatHp(Player, hp, Player.MaxHp);
            return hp - before;
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
                        seat.Name = EnemyDisplayName(monster, i);
                        seat.MonsterId = monster.MonsterId;
                        ApplySeatHp(seat, monster.Hp, monster.Hp);
                        seat.Attack = Math.Max(0, monster.Damage);
                        seat.BaseAttack = seat.Attack;
                        seat.PhaseRageTriggered = false;
                        seat.SecondWindUsed = false;
                        seat.Icon = monster.Icon;
                        ApplyEnemySpawnRelics(seat);
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
                seat.BaseAttack = seat.Attack;
                seat.PhaseRageTriggered = false;
                seat.SecondWindUsed = false;
                seat.Icon = null;
                seat.MonsterId = 0;
                seat.Banner = string.Empty;
                ClearRound(seat);
                ApplyEnemySpawnRelics(seat);
            }

            return fallbackCount;
        }

        private static string EnemyDisplayName(LevelMonster monster, int index)
        {
            if (monster != null && !string.IsNullOrEmpty(monster.Name))
            {
                return monster.Name;
            }

            if (monster != null && monster.IsBoss)
            {
                return "BOSS";
            }

            var fallback = new[] { "敌人A", "敌人B", "敌人C" };
            return index >= 0 && index < fallback.Length ? fallback[index] : $"敌人{index + 1}";
        }

        private void DeactivateEnemy(SeatState seat, int index)
        {
            seat.ActiveInStage = false;
            seat.IsBoss = false;
            seat.Profile = DefaultEnemyProfile(seat.Id);
            seat.Name = $"敌人{index + 1}";
            ApplySeatHp(seat, 0, 0);
            seat.Attack = 0;
            seat.BaseAttack = 0;
            seat.PhaseRageTriggered = false;
            seat.SecondWindUsed = false;
            seat.Icon = null;
            seat.MonsterId = 0;
        }

        private void ApplyEnemySpawnRelics(SeatState seat)
        {
            if (seat == null || !seat.ActiveInStage || seat.IsBoss)
            {
                return;
            }

            var per = RelicMechanics.SumValue(Run, MechanismType.MonsterHpMax);
            if (per == 0f)
            {
                return;
            }

            var maxHp = Math.Max(1, (int)Math.Round(seat.MaxHp * (1f + per)));
            ApplySeatHp(seat, maxHp, maxHp);
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

        private static ITalentService TalentSvc()
        {
            return AppServices.IsReady ? AppServices.Resolve<ITalentService>() : null;
        }

        private static IUnlockConditionService UnlockSvc()
        {
            return AppServices.IsReady ? AppServices.Resolve<IUnlockConditionService>() : null;
        }

        private void ApplyTalentKillRewards()
        {
            var talent = TalentSvc();
            if (TalentMechanics.Roll(talent, MechanismType.KillingProOfObtainingFunds, _rng))
            {
                Run.Gold += 1;
                Log($"点金手 +1 金币（总金币 {Run.Gold}）");
            }

            if (!TalentMechanics.IsBelowHpRatio(Player, TalentBalance.LowHpRatio))
            {
                return;
            }

            var ratio = TalentMechanics.SumValue(talent, MechanismType.KillingBringsBackBlood);
            var heal = HealPlayer((int)Math.Round(Player.MaxHp * ratio));
            if (heal > 0)
            {
                Log($"逢凶化吉 +{heal} HP（当前 {Player.Hp}/{Player.MaxHp}）");
            }
        }

        private void ApplyTalentRoundGold()
        {
            if (!TalentMechanics.Roll(TalentSvc(), MechanismType.ProOfObtainingFundsEverySettlement, _rng))
            {
                return;
            }

            Run.Gold += 1;
            Log($"资本家 +1 金币（总金币 {Run.Gold}）");
        }

        private void ApplyTalentStageEndHeal()
        {
            var ratio = TalentMechanics.SumValue(TalentSvc(), MechanismType.HeroHpReplyEveryLevelEnding);
            var heal = HealPlayer((int)Math.Round(Player.MaxHp * ratio));
            if (heal > 0)
            {
                Log($"回复 +{heal} HP（当前 {Player.Hp}/{Player.MaxHp}）");
            }
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

        /// <summary>
        /// 本手攻击值按 <see cref="GameConst.DamageTurnToGold"/> 当场换成局内金币。
        /// 比例 [a, b]：floor(伤害 × b / a)。每手单独取整，不计入商店双倍。
        /// </summary>
        private void GrantDamageGold(int damage)
        {
            var gold = DamageToGold(damage);
            if (gold <= 0)
            {
                return;
            }

            Run.Gold += gold;
            Log($"伤害换金 +{gold}（本手 {damage} 伤害，总金币 {Run.Gold}）");
        }

        private static int DamageToGold(int damage)
        {
            if (damage <= 0 || !GameConst.IsLoaded)
            {
                return 0;
            }

            var rate = GameConst.Instance.DamageTurnToGold;
            if (rate == null || rate.Length < 2 || rate[0] <= 0)
            {
                return 0;
            }

            return damage * Math.Max(0, rate[1]) / rate[0];
        }

        private int CountStageKills()
        {
            var kills = 0;
            for (var i = 0; i < Enemies.Length; i++)
            {
                var enemy = Enemies[i];
                if (enemy != null && enemy.ActiveInStage && enemy.Hp <= 0)
                {
                    kills++;
                }
            }

            return kills;
        }

        private static int KillMonsterGoldBonus()
        {
            return GameConst.IsLoaded ? Math.Max(0, GameConst.Instance.KillMonsterGetGold) : 0;
        }

        private int CardsDealtFor(SeatState seat)
        {
            return GameBalance.CardsDealt(seat != null && seat.IsPlayer, Run);
        }

        private bool AnyBossAlive()
        {
            for (var i = 0; i < Enemies.Length; i++)
            {
                var enemy = Enemies[i];
                if (enemy != null && enemy.IsBoss && enemy.Alive)
                {
                    return true;
                }
            }

            return false;
        }

        private bool AnyNonBossAlive()
        {
            for (var i = 0; i < Enemies.Length; i++)
            {
                var enemy = Enemies[i];
                if (enemy != null && !enemy.IsBoss && enemy.Alive)
                {
                    return true;
                }
            }

            return false;
        }

        private bool TryFailRoundLimit()
        {
            if (!BossMechanics.RoundLimitExceeded(Run, _stageBetRound) || !AnyEnemyAlive())
            {
                return false;
            }

            if (Player != null && Player.Hp > 0)
            {
                var remain = Player.Hp;
                Player.Hp = 0;
                HpSvc()?.Damage(Player.Id, remain);
                Player.Status = "阵亡";
            }

            LastResult = "回合制约：未能在限定回合内击杀所有敌人";
            Log(LastResult);
            Phase = GamePhase.StageFail;
            Hint = LastResult + "\n生命耗尽。可看广告复活，或重开本关。";
            Notify();
            return true;
        }

        private void PickHandBrand()
        {
            Run.HandBrandIndex = -1;
            if (!BossMechanics.Has(Run, BossEntryType.HandBrand) || Player == null)
            {
                return;
            }

            var selected = new List<int>(GameBalance.OpenHandSize);
            var limit = Math.Min(Player.Hand.Length, Player.CardSelected.Length);
            for (var i = 0; i < limit; i++)
            {
                if (Player.CardSelected[i])
                {
                    selected.Add(i);
                }
            }

            if (selected.Count == 0)
            {
                return;
            }

            Run.HandBrandIndex = selected[_rng.Next(selected.Count)];
            Log($"手牌烙印：第 {Run.HandBrandIndex + 1} 张不参与牌型");
        }

        private void ApplyRelicDisable()
        {
            Run.DisabledRelicIds.Clear();
            var count = BossMechanics.RelicDisableCount(Run);
            if (count <= 0 || Run.RelicConfigIds.Count == 0)
            {
                return;
            }

            var pool = new List<int>(Run.RelicConfigIds.Count);
            for (var i = 0; i < Run.RelicConfigIds.Count; i++)
            {
                var id = Run.RelicConfigIds[i];
                if (id > 0)
                {
                    pool.Add(id);
                }
            }

            var n = Math.Min(count, pool.Count);
            for (var i = 0; i < n; i++)
            {
                var pick = _rng.Next(pool.Count);
                var id = pool[pick];
                pool.RemoveAt(pick);
                if (!Run.DisabledRelicIds.Add(id))
                {
                    continue;
                }

                var relic = RelicConfig.Get(id);
                var name = relic != null ? relic.Name : id.ToString();
                Log($"收藏禁用：本手失效 {name}");
            }
        }

        private void ApplyAttackSteal()
        {
            var ratio = BossMechanics.AttackStealRatio(Run);
            if (ratio <= 0f || Player == null)
            {
                return;
            }

            var steal = (int)Math.Round(Player.Attack * ratio);
            if (steal <= 0)
            {
                return;
            }

            Player.Attack = Math.Max(0, Player.Attack - steal);
            Run.StolenAttack += steal;
            for (var i = 0; i < Enemies.Length; i++)
            {
                RefreshBossRageAttack(Enemies[i]);
            }

            Log($"窃取指环：BOSS 偷取攻击 {steal}（玩家 {Player.Attack}）");
        }

        private void RefreshBossRageAttack(SeatState seat)
        {
            if (seat == null || seat.IsPlayer || !seat.ActiveInStage || seat.Hp <= 0)
            {
                return;
            }

            if (!BossMechanics.Has(Run, BossEntryType.MonsterRage) && !seat.IsBoss)
            {
                return;
            }

            var core = Math.Max(0, seat.BaseAttack + Run.StolenAttack);
            var rage = BossMechanics.RageAttackBonus(Run, seat, core);
            var next = core + rage;
            if (next != seat.Attack && rage > 0)
            {
                var lost = seat.MaxHp > 0 ? 1f - seat.Hp / (float)seat.MaxHp : 0f;
                Log($"狂暴增长：攻击 {seat.Attack} → {next}（已损失 {lost:P0}）");
            }

            seat.Attack = next;
        }

        private bool TryBossTimidImmune(SeatState target)
        {
            if (target == null || !target.IsBoss || !BossMechanics.Has(Run, BossEntryType.BossTimid))
            {
                return false;
            }

            if (!AnyNonBossAlive())
            {
                return false;
            }

            Log($"胆小首领：{target.Name} 在随从存活时免疫伤害");
            target.Banner = "免疫";
            return true;
        }

        private bool TryBossShieldImmune(SeatState target)
        {
            if (target == null || !target.IsBoss || Run.BossShieldHitsLeft <= 0)
            {
                return false;
            }

            Run.BossShieldHitsLeft--;
            Log($"黑暗护盾：免疫伤害（剩余 {Run.BossShieldHitsLeft}）");
            target.Banner = "护盾";
            return true;
        }

        private bool TryMonsterEvade(SeatState target, SeatState attacker)
        {
            var chance = BossMechanics.MonsterEvadeChance(Run);
            if (chance <= 0f || target == null || _rng.NextDouble() >= chance)
            {
                return false;
            }

            Log($"灵活身姿：{target.Name} 闪避攻击");
            target.Banner = "闪避";
            var factor = BossMechanics.MonsterEvadeCounterFactor(Run);
            var counter = (int)Math.Round(target.Attack * factor);
            if (counter > 0 && Player != null && Player.Alive)
            {
                Log($"灵活身姿：反击 {counter}");
                ApplyDamage(Player, counter, false, target);
            }

            return true;
        }

        private void ApplyCurseBodySelfDamage()
        {
            if (Player == null || Player.Hp <= 0)
            {
                return;
            }

            var dmg = BossMechanics.CurseBodySelfDamage(Run, Player.Hp);
            if (dmg <= 0)
            {
                return;
            }

            var dealt = Math.Min(Player.Hp, dmg);
            Player.Hp -= dealt;
            HpSvc()?.Damage(Player.Id, dealt);
            Log($"诅咒之躯：自损 {dealt} HP（当前 {Player.Hp}/{Player.MaxHp}）");
            if (Player.Hp <= 0)
            {
                Player.Hp = 0;
                Player.Status = "阵亡";
            }
        }

        private void ApplyThornShellSelfDamage(int dealt)
        {
            if (Player == null || Player.Hp <= 0)
            {
                return;
            }

            var dmg = BossMechanics.ThornShellSelfDamage(Run, dealt);
            if (dmg <= 0)
            {
                return;
            }

            var lost = Math.Min(Player.Hp, dmg);
            Player.Hp -= lost;
            HpSvc()?.Damage(Player.Id, lost);
            Log($"尖刺外壳：自损 {lost} HP（当前 {Player.Hp}/{Player.MaxHp}）");
            if (Player.Hp <= 0)
            {
                Player.Hp = 0;
                Player.Status = "阵亡";
            }
        }

        private void ApplyVengefulSoulOnKill()
        {
            if (Player == null)
            {
                return;
            }

            var delta = BossMechanics.VengefulSoulAttackDelta(Run);
            if (delta == 0)
            {
                return;
            }

            Run.PermanentAttackBonus += delta;
            Player.Attack = Math.Max(0, Player.Attack + delta);
            Log($"怨恨之灵：攻击 {delta}（当前 {Player.Attack}）");
        }

        private void ApplyLifeSiphon(SeatState attacker, int dealt)
        {
            if (attacker == null || attacker.IsPlayer || !attacker.Alive)
            {
                return;
            }

            var heal = BossMechanics.LifeSiphonHeal(Run, dealt);
            if (heal <= 0)
            {
                return;
            }

            var hp = Math.Min(attacker.MaxHp, attacker.Hp + heal);
            var gained = hp - attacker.Hp;
            if (gained <= 0)
            {
                return;
            }

            ApplySeatHp(attacker, hp, attacker.MaxHp);
            Log($"生命虹吸：{attacker.Name} 回复 {gained} HP（当前 {attacker.Hp}/{attacker.MaxHp}）");
        }

        private void TryApplyPhaseRage(SeatState seat)
        {
            if (!BossMechanics.ShouldTriggerPhaseRage(Run, seat))
            {
                return;
            }

            var bonus = BossMechanics.PhaseRageAttackBonus(Run, seat.BaseAttack);
            seat.PhaseRageTriggered = true;
            if (bonus <= 0)
            {
                return;
            }

            seat.BaseAttack += bonus;
            seat.Attack = Math.Max(0, seat.Attack + bonus);
            Log($"背水一战：{seat.Name} 攻击 +{bonus}（当前 {seat.Attack}）");
            if (seat.IsBoss)
            {
                RefreshBossRageAttack(seat);
            }
        }

        private void ApplyMonsterRegen()
        {
            var ratio = BossMechanics.MonsterRegenRatio(Run);
            if (ratio <= 0f)
            {
                return;
            }

            for (var i = 0; i < Enemies.Length; i++)
            {
                var seat = Enemies[i];
                if (seat == null || !seat.Alive || seat.MaxHp <= 0)
                {
                    continue;
                }

                var heal = (int)Math.Round(seat.MaxHp * ratio);
                if (heal <= 0)
                {
                    continue;
                }

                var hp = Math.Min(seat.MaxHp, seat.Hp + heal);
                var gained = hp - seat.Hp;
                if (gained <= 0)
                {
                    continue;
                }

                ApplySeatHp(seat, hp, seat.MaxHp);
                Log($"巫术灵体：{seat.Name} 回复 {gained} HP（当前 {seat.Hp}/{seat.MaxHp}）");
            }
        }

        private void ApplyMonsterGrow()
        {
            var add = BossMechanics.MonsterGrowAttack(Run);
            if (add == 0)
            {
                return;
            }

            for (var i = 0; i < Enemies.Length; i++)
            {
                var seat = Enemies[i];
                if (seat == null || !seat.Alive)
                {
                    continue;
                }

                seat.BaseAttack = Math.Max(0, seat.BaseAttack + add);
                seat.Attack = Math.Max(0, seat.Attack + add);
                Log($"磨刀霍霍：{seat.Name} 攻击 +{add}（当前 {seat.Attack}）");
                if (seat.IsBoss)
                {
                    RefreshBossRageAttack(seat);
                }
            }
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
            FillShopOffers();
        }

        /// <summary>把货架补到上限。只排除当前持有的，消耗品用掉后可以再进池。</summary>
        private void FillShopOffers()
        {
            while (Run.ShopOfferIds.Count > GameBalance.ShopOfferCount)
            {
                Run.ShopOfferIds.RemoveAt(Run.ShopOfferIds.Count - 1);
            }

            var pool = BuildShopOfferPool();
            while (Run.ShopOfferIds.Count < GameBalance.ShopOfferCount)
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

        private List<RelicConfig> BuildShopOfferPool()
        {
            var pool = new List<RelicConfig>();
            foreach (var relic in RelicConfig.All.Values)
            {
                if (!CanAppearInShop(relic) || Run.ShopOfferIds.Contains(relic.Id))
                {
                    continue;
                }

                pool.Add(relic);
            }

            return pool;
        }

        private bool CanAppearInShop(RelicConfig relic)
        {
            if (relic == null || relic.RefreshProbability <= 0f)
            {
                return false;
            }

            if (!IsRelicInShopPool(relic.Id))
            {
                return false;
            }

            return !OwnsRelicConfig(relic.Id);
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
                total += HeroMechanics.ShopWeight(Run, pool[i]);
            }

            if (total <= 0f)
            {
                return pool[_rng.Next(pool.Count)];
            }

            var roll = _rng.NextDouble() * total;
            var acc = 0.0;
            for (var i = 0; i < pool.Count; i++)
            {
                acc += HeroMechanics.ShopWeight(Run, pool[i]);
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
                if (CanAppearInShop(relic))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsRelicInShopPool(int relicId)
        {
            var unlock = UnlockSvc();
            return unlock == null || unlock.IsRelicInShopPool(relicId);
        }

        private static bool HasShownSeven(Card[] cards)
        {
            if (cards == null)
            {
                return false;
            }

            for (var i = 0; i < cards.Length; i++)
            {
                if (cards[i].Rank == Rank.Seven)
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
