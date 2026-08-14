using System;
using System.Collections.Generic;

namespace App.Game
{
    /// <summary>
    /// 炸金花闯关对局状态机：发牌 → 看牌/搓牌 → 下注街（玩家先手，AI 后手）→ 摊牌/开牌 → 攻击结算。
    /// 敌人座位固定 3 个，人格在 <see cref="CreateSeat"/> 绑定，BOSS 关覆盖成 Expert。
    /// </summary>
    public sealed class GameSession
    {
        public const int MaxEnemies = 3;

        private readonly Random _rng;
        private Deck _deck;
        /// <summary>本街已出现的最高下注档位。</summary>
        private int _maxStreetUnits;
        /// <summary>当轮基础单注。无人加注的街结束后会抬到上一街的最高档。</summary>
        private int _betStep = GameBalance.MinBet;
        /// <summary>连续无加注的街数，满 2 街强制摊牌。</summary>
        private int _streetsWithoutRaise;
        private int _bettingRound = 1;
        private bool _streetHadRaise;
        private bool _playerActedThisStreet;
        private int _pendingRubIndex = -1;
        private RevealKind _revealKind;
        private SeatState _pendingWinner;
        private HandScore _pendingBest;
        private SeatState _pendingOpener;
        private SeatState _pendingOpenTarget;
        private bool _pendingOpenerWins;
        private SeatState _pendingAttackTarget;
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
            // 座位写死 3 个：A 保守 / B 平衡偏激进 / C 激进。每关再按 EnemyCountForStage 决定谁上场。
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
        public int PendingAttackDamage { get; private set; }
        public int AttackPlaySerial { get; private set; }
        public int AttackVisualSlot { get; private set; } = -1;
        public int AttackDamage { get; private set; }
        public bool AttackPlaying => _pendingAttackTarget != null;
        public int RevealPlaySerial { get; private set; }
        public int RevealWinnerId { get; private set; } = -1;
        public readonly List<int> RevealSeatIds = new List<int>();
        public bool SelectingOpenTarget { get; private set; }
        public bool PlayerMayLookCards =>
            (Phase == GamePhase.WaitingLookChoice || Phase == GamePhase.Betting) &&
            !Player.Looked &&
            !Player.Folded;
        public bool PlayerMayCancelLookOrRub =>
            Phase == GamePhase.WaitingLookChoice || Phase == GamePhase.WaitingRub;
        public bool PlayerCanOpen => Phase == GamePhase.Betting && !Player.Folded && CanAffordOpen(Player);

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
            Run.LoanTicket = false;
            Run.SplashThisRound = false;
            Run.MagnifierThisRound = false;
            Run.AdsDoubleGoldToday = 0;
            Run.Log.Clear();
            StartStage();
        }

        public void RestartStage()
        {
            StartStage();
        }

        public void SelectRubCard(int index)
        {
            if (Phase != GamePhase.WaitingRub || index < 0 || index > 2)
            {
                return;
            }

            _pendingRubIndex = index;
            Hint = $"已选中第 {index + 1} 张牌，滑动搓开（必须搓 1 张）";
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
            if (Phase != GamePhase.WaitingRub || index < 0 || index > 2 || Run.RubsLeft <= 0)
            {
                return;
            }

            var old = Player.Hand[index];
            _deck.Remove(old);
            var next = DrawRubCard(old);
            Player.Hand[index] = next;
            Run.RubsLeft--;
            _pendingRubIndex = -1;
            Run.LastRubMessage = $"第 {index + 1} 张换成 {next.DisplayName}";
            Log(Run.LastRubMessage);

            if (Run.RubsLeft > 0)
            {
                Hint = $"{Run.LastRubMessage}。还可再搓 {Run.RubsLeft} 次，或点取消跳过";
                Notify();
                return;
            }

            EnterBetting();
        }

        public void CancelLookOrRub()
        {
            if (Phase == GamePhase.WaitingLookChoice)
            {
                BlindBet();
                return;
            }

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
            EnterBetting();
        }

        public void PeekMagnifier(int index)
        {
            if (Phase != GamePhase.Betting || !Run.MagnifierThisRound || Run.PeekSuitUsed)
            {
                return;
            }

            if (index < 0 || index > 2)
            {
                return;
            }

            Run.PeekSuitUsed = true;
            Run.PeekSuitIndex = index;
            Run.PeekedSuit = Player.Hand[index].Suit;
            Hint = $"放大镜：第 {index + 1} 张是{Card.SuitName(Player.Hand[index].Suit)}（未见点数）";
            Notify();
        }

        /// <summary>看牌后进入搓牌；搓完或跳过才下注。看牌后下注血量翻倍。</summary>
        public void LookCards()
        {
            if (!PlayerMayLookCards)
            {
                return;
            }

            Player.Looked = true;
            Player.Status = "已看牌";
            History.NoteLook();
            Run.RubsLeft = 1 + Run.ExtraRubCharges;
            Run.ExtraRubCharges = 0;
            _pendingRubIndex = -1;
            Phase = GamePhase.WaitingRub;
            Hint = Run.RubsLeft > 1
                ? $"看牌后可搓牌（可搓 {Run.RubsLeft} 次），或点取消跳过"
                : "看牌后可搓一张牌，或点取消跳过";
            Notify();
        }

        public void AdjustBetUnits(int delta)
        {
            BetUnits = Math.Max(_betStep, GameBalance.MinBet);
            Notify();
        }

        /// <summary>闷注：不看牌跟注。未看牌时对手跟注要付双倍。</summary>
        public void BlindBet()
        {
            if (Phase != GamePhase.Betting && Phase != GamePhase.WaitingLookChoice)
            {
                return;
            }

            if (Phase == GamePhase.WaitingLookChoice)
            {
                Phase = GamePhase.Betting;
            }

            PlacePlayerBet(false);
        }

        /// <summary>加注一档后轮到 AI 街。</summary>
        public void RaiseBet()
        {
            if (Phase != GamePhase.Betting)
            {
                return;
            }

            PlacePlayerBet(true);
        }

        public bool PlayerMayAllIn =>
            Phase == GamePhase.Betting && !Player.Folded && Player.Hp > 0;

        private bool PlayerMayUseItems =>
            (Phase == GamePhase.WaitingLookChoice ||
             Phase == GamePhase.Betting ||
             Phase == GamePhase.WaitingRub) &&
            !Player.Folded;

        public bool PlayerMayUsePeekGood =>
            PlayerMayUseItems &&
            Run.PeekGoodCharges > 0;

        public bool PlayerMayUseChaKanGood =>
            PlayerMayUseItems &&
            Run.ChaKanGoodCharges > 0 &&
            AnyLivingEnemyInHand();

        public bool PlayerMayUseTiHuanGood =>
            PlayerMayUseItems &&
            Run.TiHuanGoodCharges > 0 &&
            _deck != null;

        /// <summary>把剩余血量推进底池。全下后若还有人能下注，对手继续打边池。</summary>
        public void AllIn()
        {
            if (!PlayerMayAllIn)
            {
                return;
            }

            SelectingOpenTarget = false;
            var roundUnits = CurrentRoundUnits();
            TryCommitUnits(Player, roundUnits, out var paid);
            if (Player.Hp > 0)
            {
                var rest = Player.Hp;
                Player.Hp = 0;
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

            Run.PeekGoodCharges--;
            if (!Player.Looked)
            {
                Player.Looked = true;
                History.NoteLook();
            }

            Player.Status = "已看牌";
            Run.RubsLeft = Math.Max(1, Run.RubsLeft + 1);
            _pendingRubIndex = -1;
            Phase = GamePhase.WaitingRub;
            Hint = $"使用搓牌道具（剩余 {Run.PeekGoodCharges}）。点选一张手牌再搓";
            Log("使用道具：再次搓牌");
            Notify();
        }

        public void UseChaKanGood()
        {
            if (!PlayerMayUseChaKanGood)
            {
                return;
            }

            var targets = new List<SeatState>();
            for (var i = 0; i < Enemies.Length; i++)
            {
                var enemy = Enemies[i];
                if (enemy.Alive && !enemy.Folded && HasHand(enemy))
                {
                    targets.Add(enemy);
                }
            }

            if (targets.Count == 0)
            {
                Hint = "没有可透视的存活敌人";
                Notify();
                return;
            }

            var seat = targets[_rng.Next(targets.Count)];
            var index = _rng.Next(0, 3);
            SetSpyReveal(seat.Id, index, true);
            Run.ChaKanGoodCharges--;
            var card = seat.Hand[index];
            Hint = $"透视：{seat.Name} 第 {index + 1} 张是 {card.DisplayName}（剩余 {Run.ChaKanGoodCharges}）";
            Log($"透视 {seat.Name} 第 {index + 1} 张 {card.DisplayName}");
            Notify();
        }

        public void UseTiHuanGood()
        {
            if (!PlayerMayUseTiHuanGood)
            {
                return;
            }

            for (var i = 0; i < 3; i++)
            {
                Player.Hand[i] = _deck.Draw();
            }

            if (!Player.Looked)
            {
                History.NoteLook();
            }

            Player.Looked = true;
            Run.TiHuanGoodCharges--;
            Hint =
                $"替换：{Player.Hand[0].DisplayName} / {Player.Hand[1].DisplayName} / {Player.Hand[2].DisplayName}（剩余 {Run.TiHuanGoodCharges}）";
            Log($"替换手牌为 {Player.Hand[0].DisplayName} {Player.Hand[1].DisplayName} {Player.Hand[2].DisplayName}");
            if (Phase == GamePhase.WaitingLookChoice)
            {
                EnterBetting();
                return;
            }

            Notify();
        }

        public bool IsSpyRevealed(int seatId, int cardIndex)
        {
            var key = SpyKey(seatId, cardIndex);
            return key >= 0 && key < Run.SpyReveal.Length && Run.SpyReveal[key];
        }

        /// <summary>玩家弃牌。若桌上还剩多人，AI 继续打边池。</summary>
        public void Fold()
        {
            if (Phase != GamePhase.Betting || Player.Folded)
            {
                return;
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
            if (Phase != GamePhase.Betting || Player.Folded)
            {
                return;
            }

            SelectingOpenTarget = false;
            var roundUnits = CurrentRoundUnits();
            if (Player.StreetUnits < roundUnits)
            {
                if (!TryCommitUnits(Player, roundUnits, out var paid))
                {
                    Hint = "血量不足，无法比牌";
                    Notify();
                    return;
                }

                _playerActedThisStreet = true;
                Player.Status = Player.Looked ? $"看牌比牌 {paid}" : $"比牌 {paid}";
                Log($"{Player.Status}，当轮 {roundUnits}，奖池 {Pot}");
            }

            Showdown();
        }

        /// <summary>赢牌后点选敌人造成伤害。溅射斩会额外打其他存活敌人 30%。</summary>
        public void AttackEnemyAtSlot(int visualSlot)
        {
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

            if (Phase != GamePhase.WaitingAttack)
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

            if (Phase == GamePhase.WaitingAttack)
            {
                return !AttackPlaying;
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
            var damage = Math.Max(1, PendingAttackDamage);
            PendingAttackDamage = 0;
            ApplyDamage(target, damage, true);
            if (Run.SplashThisRound)
            {
                for (var i = 0; i < Enemies.Length; i++)
                {
                    if (Enemies[i] != target && Enemies[i].Alive)
                    {
                        ApplyDamage(Enemies[i], (int)Math.Round(damage * GameBalance.SplashRatio), false);
                    }
                }

                Run.SplashThisRound = false;
            }

            AfterRound();
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
                }

                Log($"购入道具 {item.Name}");
            }

            Hint = $"已购买 {item.Name}";
            Notify();
        }

        public void LeaveShop()
        {
            if (Phase != GamePhase.Shop)
            {
                return;
            }

            Run.Stage++;
            if (Run.Stage > 30)
            {
                Phase = GamePhase.RunComplete;
                Hint = "你已通关 3 个章节！";
                Notify();
                return;
            }

            StartStage();
        }

        public void WatchAdLoan()
        {
            if (Phase != GamePhase.StageFail || Run.AdsLoanThisStage >= 1)
            {
                return;
            }

            Run.AdsLoanThisStage++;
            Heal(Player, GameBalance.MinBet);
            Log("观看广告，借贷获得最低下注血量");
            ContinueAfterLoan();
        }

        public void WatchAdRevive()
        {
            if (Phase != GamePhase.StageFail || Run.AdsReviveThisStage >= 1)
            {
                return;
            }

            Run.AdsReviveThisStage++;
            Player.MaxHp = Math.Max(Player.MaxHp, GameBalance.PlayerStartHp);
            Player.Hp = Player.MaxHp;
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
            Run.ExtraRubCharges++;
            Log("观看广告，本关额外获得 1 次搓牌");
            Hint = $"额外搓牌 +1（本关还可广告 {2 - Run.AdsExtraRubThisStage} 次）";
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
            var extra = GameBalance.ConvertHpToGold(Player.Hp);
            Run.Gold += extra;
            Log($"双倍金币结算 +{extra}");
            Hint = $"金币翻倍，额外获得 {extra}";
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

        /// <summary>评估座位牌型。BOSS 禁用花色/人头会先过滤，燧石减半筹码和倍率。</summary>
        public HandScore EvaluateSeat(SeatState seat)
        {
            Suit? banned = null;
            var banFaces = Run.Affix == BossAffix.BanScoreFace;
            switch (Run.Affix)
            {
                case BossAffix.BanScoreHeart: banned = Suit.Heart; break;
                case BossAffix.BanScoreSpade: banned = Suit.Spade; break;
                case BossAffix.BanScoreDiamond: banned = Suit.Diamond; break;
                case BossAffix.BanScoreClub: banned = Suit.Club; break;
            }

            var score = HandEvaluator.Evaluate(seat.Hand, banned, banFaces);
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

        /// <summary>开新关：按关卡决定 3 敌或 1 BOSS，BOSS 人格改为 Expert 并随机词缀。</summary>
        private void StartStage()
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
            Run.PeekGoodCharges = 1;
            Run.ChaKanGoodCharges = 1;
            Run.TiHuanGoodCharges = 1;
            Run.Affix = BossAffix.None;
            Player.ActiveInStage = true;
            Player.MaxHp = GameBalance.PlayerStartHp;
            Player.Hp = GameBalance.PlayerStartHp;

            var count = GameBalance.EnemyCountForStage(Run.Stage);
            var boss = GameBalance.IsBossStage(Run.Stage);
            var names = new[] { "敌人A", "敌人B", "敌人C" };
            if (boss)
            {
                names[0] = "BOSS";
                Run.Affix = RandomAffix();
                ApplyEdgeAffix();
            }

            for (var i = 0; i < Enemies.Length; i++)
            {
                var seat = Enemies[i];
                seat.ActiveInStage = i < count;
                seat.IsBoss = boss && i == 0;
                if (seat.IsBoss)
                {
                    // BOSS 关只留 Enemies[0]，人格从 CreateSeat 的保守型覆盖成高手。
                    seat.Profile = AiProfile.Expert;
                }
                seat.Name = i < count ? names[i] : $"敌人{i + 1}";
                seat.MaxHp = GameBalance.EnemyHp(Run.Stage, seat.IsBoss);
                seat.Hp = seat.ActiveInStage ? seat.MaxHp : 0;
                seat.Banner = string.Empty;
                ClearRound(seat);
            }

            var title = boss
                ? $"第 {Run.Stage} 关 BOSS · {GameBalance.AffixName(Run.Affix)}"
                : $"第 {Run.Stage} 关 · {count} 名敌人";
            Log(title);
            if (boss)
            {
                Log(GameBalance.AffixDesc(Run.Affix));
            }

            StartRound();
        }

        /// <summary>重置本手下注状态并发牌，然后进入看牌/闷注选择。</summary>
        private void StartRound()
        {
            CardsRevealed = false;
            Pot = 0;
            _maxStreetUnits = GameBalance.MinBet;
            _betStep = GameBalance.MinBet;
            _streetsWithoutRaise = 0;
            _bettingRound = 1;
            _streetHadRaise = false;
            _playerActedThisStreet = false;
            _pendingRubIndex = -1;
            BetUnits = _betStep;
            LastResult = string.Empty;
            Run.RubsLeft = 0;
            SelectingOpenTarget = false;
            RevealWinnerId = -1;
            RevealSeatIds.Clear();
            _revealKind = RevealKind.None;
            _pendingAttackTarget = null;
            AttackVisualSlot = -1;
            Run.PeekSuitUsed = false;
            Run.PeekSuitIndex = -1;
            Run.PeekedSuit = null;
            Run.LastRubMessage = string.Empty;
            PendingAttackDamage = 0;
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

            if (Player.Hp < GameBalance.MinBet)
            {
                TryAutoLoanOrFail();
                return;
            }

            _deck = new Deck(_rng);
            DealAll();
            EnterLookChoice();
        }

        /// <summary>每人发 3 张。未上场的敌人不发。</summary>
        private void DealAll()
        {
            DealSerial++;
            foreach (var seat in AllSeats())
            {
                if (!seat.Alive && !seat.IsPlayer)
                {
                    continue;
                }

                if (seat.IsPlayer || seat.Alive)
                {
                    for (var i = 0; i < 3; i++)
                    {
                        seat.Hand[i] = _deck.Draw();
                    }
                }
            }
        }

        /// <summary>搓牌换一张。禁搓词缀过滤花色/人头；磁力手套有概率保留原花色。</summary>
        private Card DrawRubCard(Card original)
        {
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
            return _deck.DrawMatching(card =>
            {
                if (bannedSuit.HasValue && card.Suit == bannedSuit.Value)
                {
                    return false;
                }

                if (banFaces && card.IsFace)
                {
                    return false;
                }

                if (keepSuit && card.Suit != original.Suit)
                {
                    return false;
                }

                return !card.Equals(original);
            });
        }

        /// <summary>发牌后先让玩家选看牌或闷注，并开始记录本手 History。</summary>
        private void EnterLookChoice()
        {
            Phase = GamePhase.WaitingLookChoice;
            History.BeginHand(Player.Hp);
            Player.Status = "待选择";
            Hint = "请选择看牌，或取消（闷注）";
            Notify();
        }

        private void EnterBetting()
        {
            Phase = GamePhase.Betting;
            Player.Status = "待下注";
            Hint = string.IsNullOrEmpty(Run.LastRubMessage)
                ? string.Empty
                : Run.LastRubMessage + "。";
            if (Player.Looked)
            {
                Hint += Run.Tilted
                    ? "心态崩了：本局最大下注为当前血量 50%。请跟注或加注"
                    : "看牌下注血量翻倍，请跟注、加注、弃牌或比牌";
            }
            else
            {
                Hint += Run.Tilted
                    ? "心态崩了：本局最大下注为当前血量 50%。请闷注或看牌"
                    : "请跟注或加注。你闷着时对手跟注双倍";
            }

            if (Run.MagnifierThisRound && !Run.PeekSuitUsed)
            {
                Hint += "。点击一张手牌可偷看花色";
            }

            Notify();
        }

        /// <summary>玩家跟注或加注。跟满后调用 <see cref="ResolveAiStreet"/>。</summary>
        private void PlacePlayerBet(bool raise)
        {
            if (Player.Folded)
            {
                return;
            }

            SelectingOpenTarget = false;

            var units = raise ? RaiseUnits() : Math.Max(CurrentRoundUnits(), _betStep);
            units = Clamp(units, _betStep, MaxBetUnits());
            if (raise && units <= _maxStreetUnits)
            {
                units = Math.Min(MaxBetUnits(), RaiseUnits());
            }

            var facingRaise = !raise && Player.StreetUnits < CurrentRoundUnits();
            var hpBefore = Player.Hp;
            if (!TryCommitUnits(Player, units, out var paid))
            {
                Hint = "血量不足";
                Notify();
                return;
            }

            if (units > _maxStreetUnits)
            {
                _maxStreetUnits = units;
                _streetHadRaise = true;
            }

            BetUnits = _betStep;

            _playerActedThisStreet = true;
            History.NotePlayerBet(raise, Player.Looked, paid, hpBefore, facingRaise);
            if (Player.Hp == 0 && paid > 0)
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
        /// AI 行动街。已跟满的座位仍可能主动开牌；未跟满则 <see cref="DecideAi"/>。
        /// 最多扫 6 轮，避免加注来回打转。
        /// </summary>
        private void ResolveAiStreet()
        {
            var scare = HasRelic(RelicId.ScareMask);
            for (var pass = 0; pass < 6; pass++)
            {
                if (CountInHand() <= 1)
                {
                    AwardUncontestedAndSettle();
                    return;
                }

                var acted = false;
                for (var i = 0; i < Enemies.Length; i++)
                {
                    var ai = Enemies[i];
                    if (!ai.Alive || ai.Folded)
                    {
                        continue;
                    }

                    if (ai.StreetUnits >= CurrentRoundUnits())
                    {
                        var hold = BuildAiDecision(ai, scare, false, false);
                        if (hold.Action == AiAction.Open && CanAffordOpen(ai))
                        {
                            acted = true;
                            if (ForceOpen(ai, Player))
                            {
                                return;
                            }
                        }

                        continue;
                    }

                    acted = true;
                    if (DecideAi(ai, scare))
                    {
                        return;
                    }
                }

                if (!acted || AllNonPlayerMatched())
                {
                    break;
                }
            }

            FinishStreetOrShowdown();
        }

        /// <summary>执行一次 AI 决策。开牌会立刻进入单挑亮牌；否则跟/加/全下/弃。</summary>
        private bool DecideAi(SeatState ai, bool scare)
        {
            var decision = BuildAiDecision(ai, scare, true, true);
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
            if (ai.Hp < CallCost(ai))
            {
                if (ai.Hp <= 0)
                {
                    FoldSeat(ai, "血量不足，弃牌");
                    Log($"{ai.Name} 血量不足，弃牌");
                    return FinishIfOneLeft();
                }

                if (!TryCommitUnits(ai, roundUnits, out var shortPaid))
                {
                    FoldSeat(ai, "血量不足，弃牌");
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
                target = SizeAiRaise(ai, decision.WinRate);
            }

            if (target < roundUnits)
            {
                FoldSeat(ai, "无法跟注，弃牌");
                Log($"{ai.Name} 无法跟注，弃牌");
                return FinishIfOneLeft();
            }

            if (TryCommitUnits(ai, target, out var paid))
            {
                if (!allIn && target > _maxStreetUnits && ai.Hp > 0)
                {
                    _maxStreetUnits = target;
                    _streetHadRaise = true;
                    ai.Status = paid > 0 ? $"加注 {paid}" : "加注";
                }
                else
                {
                    ai.Status = paid > 0 ? $"跟注 {paid}" : "跟注";
                }

                if (allIn && ai.Hp > 0)
                {
                    var rest = ai.Hp;
                    ai.Hp = 0;
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

        /// <summary>加注尺寸很粗：能加就加固定一档（当前最高档 + 单注），加不起则跟。</summary>
        private int SizeAiRaise(SeatState ai, float winRate)
        {
            var target = RaiseUnits();
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

                if (seat.Hp < minOpp)
                {
                    minOpp = seat.Hp;
                }
            }

            if (minOpp == int.MaxValue)
            {
                return Math.Max(0, ai.Hp);
            }

            return Math.Max(0, Math.Min(ai.Hp, minOpp));
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

                if (seat.Hp < shortest)
                {
                    shortest = seat.Hp;
                }
            }

            if (shortest == int.MaxValue)
            {
                shortest = ai.Hp;
            }

            var effective = Math.Min(ai.Hp, shortest);
            var playerBb = Player.Hp / (float)Math.Max(1, GameBalance.MinBet);
            var playerStrength = Player.Folded ? 0.30f : History.EstimateStrength(Player.Hp, GameBalance.MinBet);
            return AiBrain.Decide(new AiContext
            {
                Ai = ai,
                Score = score,
                WinRate = winRate,
                StraightFlushDraw = ZhaJinHuaOdds.IsStraightFlushDraw(ai.Hand),
                Pot = Pot,
                CallCost = callCost,
                AiChips = ai.Hp,
                EffectiveStack = Math.Max(0, effective),
                ShortestOpponent = Math.Max(0, shortest),
                PlayerChips = Player.Hp,
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
        /// 一街结束：未跟满的 AI 弃牌；连续两街无人加注则摊牌；否则抬单注进入下一街。
        /// 玩家已全下/弃牌时，未全下的对手继续打边池。
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
                    FoldSeat(ai, "未跟注，弃牌");
                    Log($"{ai.Name} 未跟上当轮注额，弃牌");
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

            if (!_streetHadRaise)
            {
                _streetsWithoutRaise++;
            }
            else
            {
                _streetsWithoutRaise = 0;
            }

            if (_streetsWithoutRaise >= 2)
            {
                Showdown();
                return;
            }

            _streetHadRaise = false;
            _playerActedThisStreet = false;
            foreach (var seat in AllSeats())
            {
                seat.StreetUnits = 0;
                seat.StreetPaid = 0;
            }

            _betStep = Math.Max(_betStep, _maxStreetUnits);
            _maxStreetUnits = _betStep;
            BetUnits = _betStep;
            _bettingRound++;
            if (IsAllIn(Player) || Player.Folded)
            {
                Hint = $"第 {_bettingRound} 轮边池下注，当轮单注 {_betStep}。你已{(Player.Folded ? "弃牌" : "全下")}，对手继续。";
                ResolveAiStreet();
                return;
            }

            Hint = $"第 {_bettingRound} 轮下注，当轮单注 {_betStep}。仍有玩家可下注，形成边池。";
            Notify();
        }

        /// <summary>加注目标档 = 本街最高档 + 当轮单注。</summary>
        private int RaiseUnits() => _maxStreetUnits + Math.Max(_betStep, GameBalance.MinBet);

        /// <summary>开牌要付当前注额的双倍。</summary>
        private int OpenUnits() => Math.Max(CurrentRoundUnits(), _betStep) * 2;

        /// <summary>本街需要跟上的档位 = max(单注, 各未弃牌座位的 StreetUnits)。</summary>
        private int CurrentRoundUnits()
        {
            var max = Math.Max(_betStep, GameBalance.MinBet);
            foreach (var seat in AllSeats())
            {
                if (!Participates(seat) || seat.Folded || seat.Hp <= 0)
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
            return CallCost(seat, OpenUnits()) <= seat.Hp;
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
                Hint = $"{opener.Name} 开牌失败：血量不足";
                Notify();
                return false;
            }

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
                Log("心态崩了：下一局最大下注限制为当前血量 50%");
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
                Hint = $"{seat.Name} 弃牌，亮出 {score.Label}";
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
            Run.ConsecutiveLosses++;
            if (Run.ConsecutiveLosses >= 2)
            {
                Run.Tilted = true;
                Log("心态崩了：下一局最大下注限制为当前血量 50%");
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

            Hint = "你已弃牌，未全下的对手继续下注，形成边池";
            ResolveAiStreet();
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
                : "全员亮牌，比大小！";
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
                Run.ConsecutiveLosses = 0;
                Run.Tilted = false;
                var relicMult = RelicMultiplier(best);
                PendingAttackDamage = HandEvaluator.ComputeDamage(best, ShowdownStake(), relicMult);
                ApplyBankruptcy(true, winner);
                Pot = 0;
                if (!AnyEnemyAlive())
                {
                    AfterRound();
                    return;
                }

                Phase = GamePhase.WaitingAttack;
                Hint = $"{HandDrama(best.Type)}！你赢了，造成 {PendingAttackDamage} 伤害，点选敌人攻击";
                Notify();
                return;
            }

            Run.ConsecutiveLosses++;
            if (Run.ConsecutiveLosses >= 2)
            {
                Run.Tilted = true;
                Log("心态崩了：下一局最大下注限制为当前血量 50%");
            }

            DealPlayerLossDamage(winner);
            ApplyBankruptcy(false, winner);
            Pot = 0;
            Hint = LastResult;
            AfterRound();
        }

        private void ApplyOpenDuelSettlement()
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

        private bool AnyLivingEnemyInHand()
        {
            for (var i = 0; i < Enemies.Length; i++)
            {
                var enemy = Enemies[i];
                if (enemy.Alive && !enemy.Folded && HasHand(enemy))
                {
                    return true;
                }
            }

            return false;
        }

        private static int SpyKey(int seatId, int cardIndex)
        {
            if (seatId < 0 || cardIndex < 0 || cardIndex > 2)
            {
                return -1;
            }

            return seatId * 3 + cardIndex;
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

        public void Continue()
        {
            if (Phase == GamePhase.WaitingAttack)
            {
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

            if (Player.Hp < GameBalance.MinBet)
            {
                TryAutoLoanOrFail();
                return;
            }

            Run.MagnifierThisRound = false;
            StartRound();
        }

        private void ApplyDamage(SeatState target, int damage, bool main)
        {
            var dealt = Math.Min(target.Hp, Math.Max(1, damage));
            target.Hp -= dealt;
            target.Banner = main ? $"-{dealt}" : $"溅射 -{dealt}";
            Log($"攻击 {target.Name} {dealt}，剩余 HP {target.Hp}");
            if (target.Hp <= 0)
            {
                target.Hp = 0;
                target.Status = "阵亡";
                Log($"击杀 {target.Name}");
            }
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
                    ai.Hp = 0;
                    ai.Status = "斩杀";
                    ai.Banner = "濒死斩杀";
                    Log($"互助斩杀：{ai.Name} 血量耗尽，被你斩杀");
                }
                else if (winner != null && !winner.IsPlayer && winner != ai)
                {
                    var help = Math.Max(GameBalance.MinBet, (int)Math.Floor(Pot * ratio));
                    help = Math.Min(help, Math.Max(0, winner.Hp));
                    winner.Hp -= help;
                    Heal(ai, help);
                    ai.Banner = "获得援助";
                    ai.Status = "获救";
                    Log($"{winner.Name} 抽出 {help} 血量救助 {ai.Name}");
                }
            }
        }

        private void AfterRound()
        {
            PendingAttackDamage = 0;
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
            else if (Player.Hp < GameBalance.MinBet)
            {
                TryAutoLoanOrFail();
                return;
            }
            else
            {
                Hint = LastResult + "\n点击「下一局」继续";
            }

            Notify();
        }

        private void EnterShop()
        {
            var remain = Player.Hp;
            var gold = GameBalance.ConvertHpToGold(remain);
            if (Run.DoubleGoldThisStage)
            {
                gold *= 2;
            }

            Run.Gold += gold;
            Log($"通关结算：剩余血量 {remain} → {gold} 金币（总金币 {Run.Gold}）");
            Phase = GamePhase.Shop;
            Hint = $"关卡胜利！兑换 {gold} 金币。购买道具后进入下一关。";
            LastResult = Hint;
            Notify();
        }

        private void TryAutoLoanOrFail()
        {
            var magnifierDisabled = Run.DisabledConsumable == ConsumableId.LoanTicket;
            if (Run.LoanTicket && !magnifierDisabled)
            {
                Run.LoanTicket = false;
                Heal(Player, GameBalance.MinBet);
                Log("借贷券生效，获得最低下注血量");
                StartRound();
                return;
            }

            Phase = GamePhase.StageFail;
            Hint = "血量不足。可看广告借贷继续，或重开本关。";
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

            if (Player.Hp < GameBalance.MinBet)
            {
                Phase = GamePhase.StageFail;
                Hint = "仍不足以继续";
                Notify();
                return;
            }

            StartRound();
        }

        /// <summary>从血量扣下注。看牌后玩家付双倍；反加注词缀再让玩家 ×1.5。</summary>
        private bool TryCommitUnits(SeatState seat, int units, out int paid)
        {
            units = Math.Max(units, seat.StreetUnits);
            var cost = CostFor(seat, units) - CostFor(seat, seat.StreetUnits);
            if (Run.Affix == BossAffix.AntiRaise && seat.IsPlayer)
            {
                cost = (int)Math.Ceiling(cost * 1.5f);
            }

            paid = cost;
            if (cost > seat.Hp)
            {
                if (seat.Hp <= 0)
                {
                    paid = 0;
                    return false;
                }

                cost = seat.Hp;
                paid = cost;
            }

            seat.Hp -= cost;
            seat.TotalBet += cost;
            seat.StreetPaid += cost;
            if (seat.Hp > 0)
            {
                seat.StreetUnits = units;
            }

            Pot += cost;
            return true;
        }

        private int CostFor(SeatState seat, int units)
        {
            return PaysDouble(seat) ? units * 2 : units;
        }

        /// <summary>看牌的一方跟注付双倍：玩家看了自己付双倍；玩家闷着则 AI 付双倍。</summary>
        private bool PaysDouble(SeatState seat)
        {
            if (seat.IsPlayer)
            {
                return seat.Looked;
            }

            return !Player.Looked;
        }

        private int UnitsAffordable(SeatState seat)
        {
            var denom = PaysDouble(seat) ? 2 : 1;
            if (Run.Affix == BossAffix.AntiRaise && seat.IsPlayer)
            {
                denom = Math.Max(1, (int)Math.Ceiling(denom * 1.5f));
            }

            return AlignBet(seat.Hp / denom);
        }

        /// <summary>连输触发心态崩了时，把玩家最大下注压到当前血量一半。</summary>
        private int MaxBetUnits()
        {
            var cap = UnitsAffordable(Player);
            if (Run.Tilted)
            {
                cap = Math.Min(cap, AlignBet(Player.Hp / 2 / (Player.Looked ? 2 : 1)));
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

        private void ClearRound(SeatState seat)
        {
            seat.StreetUnits = 0;
            seat.StreetPaid = 0;
            seat.TotalBet = 0;
            seat.RoundStartChips = seat.Hp;
            seat.Folded = false;
            seat.Looked = false;
            seat.ShowCards = false;
            seat.Status = seat.Alive || seat.IsPlayer ? string.Empty : "未上场";
            if (!seat.IsPlayer && !seat.Alive)
            {
                seat.Status = seat.ActiveInStage ? "阵亡" : "未上场";
            }
        }

        private void Heal(SeatState seat, int amount)
        {
            if (seat == null || amount <= 0)
            {
                return;
            }

            seat.Hp += amount;
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
        /// BOSS 关会在 <see cref="StartStage"/> 把 id1 改成 Expert。
        /// </summary>
        private SeatState CreateSeat(int id, string name, bool player)
        {
            AiProfile profile = null;
            if (!player)
            {
                switch (id)
                {
                    case 1:
                        profile = AiProfile.Conservative; // 敌人A
                        break;
                    case 3:
                        profile = AiProfile.Aggressive; // 敌人C
                        break;
                    default:
                        profile = AiProfile.BalancedAggressive; // 敌人B
                        break;
                }
            }

            return new SeatState
            {
                Id = id,
                Name = name,
                IsPlayer = player,
                ActiveInStage = player,
                MaxHp = player ? GameBalance.PlayerStartHp : 0,
                Hp = player ? GameBalance.PlayerStartHp : 0,
                Profile = profile
            };
        }

        private void Log(string line)
        {
            Run.Log.Add(line);
            if (Run.Log.Count > 40)
            {
                Run.Log.RemoveAt(0);
            }
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
            return seat != null && !seat.Folded && seat.Hp <= 0 && seat.TotalBet > 0;
        }

        private int CountPlayersWhoCanBet()
        {
            var n = 0;
            foreach (var seat in AllSeats())
            {
                if (Participates(seat) && !seat.Folded && seat.Hp > 0)
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
            Heal(last, amount);
            LastResult = $"{last.Name} 无人争夺，收走奖池 {amount}";
            Log(LastResult);
            ApplyBankruptcy(last.IsPlayer, last);
            Pot = 0;
            if (last.IsPlayer)
            {
                Run.ConsecutiveLosses = 0;
                Run.Tilted = false;
                if (!AnyEnemyAlive())
                {
                    AfterRound();
                    return;
                }

                Phase = GamePhase.WaitingAttack;
                PendingAttackDamage = Math.Max(1, ShowdownStake());
                Hint = $"对手弃牌，你收走奖池 {amount}。点选敌人攻击";
                Notify();
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
                    Heal(winners[w], gain);
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
