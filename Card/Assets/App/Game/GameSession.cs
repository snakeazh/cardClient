using System;
using System.Collections.Generic;
using System.Text;

namespace App.Game
{
    public sealed class GameSession
    {
        public const int MaxEnemies = 3;

        private readonly Random _rng;
        private Deck _deck;
        private int _maxStreetUnits;
        private int _streetsWithoutRaise;
        private bool _streetHadRaise;
        private bool _playerActedThisStreet;
        private int _pendingRubIndex = -1;

        public GameSession() : this(new Random())
        {
        }

        public GameSession(Random rng)
        {
            _rng = rng ?? new Random();
            Run = new RunState();
            Player = CreateSeat(0, "你", true);
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
        public bool PlayerMayLookCards => Phase == GamePhase.Betting && !Player.Looked;

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
            Run.Chips = GameBalance.PlayerStartChips;
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
            Run.RubbedReveal[index] = true;
            Run.LastRubMessage = $"第 {index + 1} 张搓成 {next.DisplayName}";
            Log(Run.LastRubMessage);

            if (Run.RubsLeft > 0)
            {
                Hint = $"{Run.LastRubMessage}。还可再搓 {Run.RubsLeft} 次，点选一张牌滑动搓开";
                Notify();
                return;
            }

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

        public void LookCards()
        {
            if (Phase != GamePhase.Betting || Player.Looked)
            {
                return;
            }

            Player.Looked = true;
            Player.Status = "已看牌";
            Hint = "已看牌，后续下注筹码翻倍。牌面仍对他人隐藏，直到比牌。";
            Notify();
        }

        public void AdjustBetUnits(int delta)
        {
            var min = GameBalance.MinBet;
            var max = MaxBetUnits();
            BetUnits = Clamp(BetUnits + delta, min, Math.Max(min, max));
            Notify();
        }

        public void BlindBet()
        {
            if (Phase != GamePhase.Betting)
            {
                return;
            }

            PlacePlayerBet(false);
        }

        public void RaiseBet()
        {
            if (Phase != GamePhase.Betting)
            {
                return;
            }

            PlacePlayerBet(true);
        }

        public void Fold()
        {
            if (Phase != GamePhase.Betting || Player.Folded)
            {
                return;
            }

            Player.Folded = true;
            Player.Status = "弃牌";
            Log("你弃牌");
            ResolveAfterPlayerFold();
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
            GrantChips(GameBalance.MinBet);
            Log("观看广告，借贷获得最低下注额");
            ContinueAfterLoan();
        }

        public void WatchAdRevive()
        {
            if (Phase != GamePhase.StageFail || Run.AdsReviveThisStage >= 1)
            {
                return;
            }

            Run.AdsReviveThisStage++;
            GrantChips(Math.Max(GameBalance.MinBet, GameBalance.PlayerStartChips / 2));
            Log("观看广告复活，恢复 50% 初始筹码");
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
            var extra = GameBalance.ConvertChipsToGold(Player.Chips);
            Run.Gold += extra;
            Log($"双倍金币结算 +{extra}");
            Hint = $"金币翻倍，额外获得 {extra}";
            Notify();
        }

        public bool HasRelic(RelicId id) => Run.Relics.Contains(id) && Run.DisabledRelic != id;

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
            Run.Affix = BossAffix.None;
            GrantChips(GameBalance.PlayerStartChips, reset: true);

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
                seat.Name = i < count ? names[i] : $"敌人{i + 1}";
                seat.MaxHp = GameBalance.EnemyHp(Run.Stage, seat.IsBoss);
                seat.Hp = seat.ActiveInStage ? seat.MaxHp : 0;
                seat.Chips = seat.ActiveInStage ? GameBalance.EnemyChips(Run.Stage, seat.IsBoss) : 0;
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

        private void StartRound()
        {
            CardsRevealed = false;
            Pot = 0;
            _maxStreetUnits = GameBalance.MinBet;
            _streetsWithoutRaise = 0;
            _streetHadRaise = false;
            _playerActedThisStreet = false;
            _pendingRubIndex = -1;
            BetUnits = GameBalance.MinBet;
            LastResult = string.Empty;
            Run.RubsLeft = 1 + Run.ExtraRubCharges;
            Run.ExtraRubCharges = 0;
            Run.PeekSuitUsed = false;
            Run.PeekSuitIndex = -1;
            Run.PeekedSuit = null;
            Run.LastRubMessage = string.Empty;
            for (var i = 0; i < Run.RubbedReveal.Length; i++)
            {
                Run.RubbedReveal[i] = false;
            }

            Player.Chips = Run.Chips;
            ClearRound(Player);
            for (var i = 0; i < Enemies.Length; i++)
            {
                ClearRound(Enemies[i]);
                Enemies[i].Banner = string.Empty;
            }

            if (Player.Chips < GameBalance.MinBet)
            {
                TryAutoLoanOrFail();
                return;
            }

            _deck = new Deck(_rng);
            DealAll();
            Phase = GamePhase.WaitingRub;
            Hint = Run.RubsLeft > 1
                ? $"强制盲搓：点选一张牌并滑动搓开（本局可搓 {Run.RubsLeft} 次）"
                : "强制盲搓：点选一张牌，在牌面上滑动搓开";
            Notify();
        }

        private void DealAll()
        {
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

        private void EnterBetting()
        {
            Phase = GamePhase.Betting;
            Player.Status = "待下注";
            Hint = string.IsNullOrEmpty(Run.LastRubMessage)
                ? string.Empty
                : Run.LastRubMessage + "。";
            Hint += Run.Tilted
                ? "心态崩了：本局最大下注为当前筹码 50%。请选择不看牌下注或看牌加注"
                : "请选择：不看牌下注（低成本）或看牌加注（筹码翻倍）";
            if (Run.MagnifierThisRound && !Run.PeekSuitUsed)
            {
                Hint += "。点击一张手牌可偷看花色";
            }
            Notify();
        }

        private void PlacePlayerBet(bool raise)
        {
            if (Player.Folded)
            {
                return;
            }

            var units = BetUnits;
            if (!raise)
            {
                units = Math.Max(_maxStreetUnits, GameBalance.MinBet);
            }

            units = Clamp(units, GameBalance.MinBet, MaxBetUnits());
            if (raise && units <= _maxStreetUnits)
            {
                units = Math.Min(MaxBetUnits(), _maxStreetUnits + GameBalance.MinBet);
            }

            if (!TryCommitUnits(Player, units, out var paid))
            {
                Hint = "筹码不足";
                Notify();
                return;
            }

            if (units > _maxStreetUnits)
            {
                _maxStreetUnits = units;
                _streetHadRaise = true;
            }

            _playerActedThisStreet = true;
            Player.Status = Player.Looked ? $"看牌下注 {paid}" : $"闷注 {paid}";
            Log($"{Player.Status}，奖池 {Pot}");
            if (Player.StreetUnits < _maxStreetUnits)
            {
                Hint = "有人加注，请跟注、再加注或弃牌";
                Notify();
                return;
            }

            ResolveAiStreet();
        }

        private void ResolveAiStreet()
        {
            var scare = HasRelic(RelicId.ScareMask);
            for (var i = 0; i < Enemies.Length; i++)
            {
                var ai = Enemies[i];
                if (!ai.Alive || ai.Folded)
                {
                    continue;
                }

                DecideAi(ai, scare);
            }

            FinishStreetOrShowdown();
        }

        private void DecideAi(SeatState ai, bool scare)
        {
            var score = EvaluateSeat(ai);
            var strength = (int)score.Type;
            var callRate = 0.55f;
            var raiseRate = 0.12f;
            var foldRate = 0.20f;

            if (strength >= (int)HandType.Flush)
            {
                raiseRate = 0.62f;
                callRate = 0.30f;
                foldRate = 0.02f;
            }
            else if (strength == (int)HandType.HighCard)
            {
                foldRate = 0.62f;
                raiseRate = 0.08f;
                callRate = 0.18f;
            }
            else if (strength == (int)HandType.Pair)
            {
                raiseRate = 0.22f;
                callRate = 0.55f;
                foldRate = 0.12f;
            }

            if (scare)
            {
                callRate *= 0.9f;
                raiseRate *= 0.9f;
                foldRate = Math.Min(0.9f, foldRate + 0.08f);
            }

            if (ai.Chips < CostFor(ai, _maxStreetUnits))
            {
                ai.Folded = true;
                ai.Status = "弃牌";
                Log($"{ai.Name} 筹码不足，弃牌");
                return;
            }

            var roll = _rng.NextDouble();
            if (roll < foldRate && strength <= (int)HandType.Pair)
            {
                ai.Folded = true;
                ai.Status = "弃牌";
                Log($"{ai.Name} 弃牌");
                return;
            }

            var target = _maxStreetUnits;
            if (roll < foldRate + raiseRate)
            {
                var percent = strength >= (int)HandType.Flush
                    ? 0.20 + _rng.NextDouble() * 0.30
                    : 0.05 + _rng.NextDouble() * 0.05;
                var raiseUnits = AlignBet((int)(ai.Chips * percent) / (ai.Looked ? 2 : 1));
                target = Math.Max(_maxStreetUnits + GameBalance.MinBet, raiseUnits);
                target = Math.Min(target, UnitsAffordable(ai));
            }

            if (TryCommitUnits(ai, target, out var paid))
            {
                if (target > _maxStreetUnits)
                {
                    _maxStreetUnits = target;
                    _streetHadRaise = true;
                    ai.Status = $"加注 {paid}";
                }
                else
                {
                    ai.Status = $"跟注 {paid}";
                }

                Log($"{ai.Name} {ai.Status}");
            }
            else
            {
                ai.Folded = true;
                ai.Status = "弃牌";
                Log($"{ai.Name} 弃牌");
            }
        }

        private void FinishStreetOrShowdown()
        {
            var alive = CountInHand();
            if (alive <= 1)
            {
                Showdown();
                return;
            }

            if (!Player.Folded && Player.StreetUnits < _maxStreetUnits)
            {
                Hint = "有人加注，请跟注、再加注或弃牌";
                Notify();
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
            }

            _maxStreetUnits = GameBalance.MinBet;
            Hint = $"第 {_streetsWithoutRaise + 1} 轮下注。连续两轮无人加注将强制比牌。";
            Notify();
        }

        private void ResolveAfterPlayerFold()
        {
            var winner = BestRemainingAi();
            if (winner != null)
            {
                winner.Chips += Pot;
                Log($"{winner.Name} 吃下奖池 {Pot}");
                ApplyBankruptcy(false, winner);
            }

            Pot = 0;
            Run.ConsecutiveLosses++;
            if (Run.ConsecutiveLosses >= 2)
            {
                Run.Tilted = true;
                Log("心态崩了：下一局最大下注限制为当前筹码 50%");
            }

            AfterRound();
        }

        private void Showdown()
        {
            Phase = GamePhase.Showdown;
            CardsRevealed = true;

            if (Run.Affix == BossAffix.XRay)
            {
                var peek = EvaluateSeat(Player);
                Log($"透视眼：BOSS 偷看了你的牌型「{peek.Label}」");
            }

            SeatState winner = null;
            HandScore best = default;
            var first = true;
            var sb = new StringBuilder();

            foreach (var seat in AllSeats())
            {
                if (!Participates(seat) || seat.Folded)
                {
                    continue;
                }

                var score = EvaluateSeat(seat);
                sb.Append(seat.Name).Append(' ').Append(score.Label).Append("  ");
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

            winner.Chips += Pot;
            var playerWin = winner.IsPlayer;
            LastResult = $"{sb}｜胜者 {winner.Name}（{best.Label}） 奖池 {Pot}";
            Log(LastResult);

            if (playerWin)
            {
                Run.ConsecutiveLosses = 0;
                Run.Tilted = false;
                var relicMult = RelicMultiplier(best);
                var damage = HandEvaluator.ComputeDamage(best, Pot, relicMult);
                var target = LowestHpPercentEnemy();
                if (target != null)
                {
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
                }
            }
            else
            {
                Run.ConsecutiveLosses++;
                if (Run.ConsecutiveLosses >= 2)
                {
                    Run.Tilted = true;
                    Log("心态崩了：下一局最大下注限制为当前筹码 50%");
                }
            }

            ApplyBankruptcy(playerWin, winner);
            Pot = 0;
            AfterRound();
        }

        public void Continue()
        {
            if (Phase == GamePhase.RoundSettle)
            {
                if (!AnyEnemyAlive())
                {
                    EnterShop();
                    return;
                }

                if (Player.Chips < GameBalance.MinBet)
                {
                    TryAutoLoanOrFail();
                    return;
                }

                Run.MagnifierThisRound = false;
                StartRound();
            }
        }

        private void ApplyDamage(SeatState target, int damage, bool main)
        {
            var dealt = Math.Min(target.Hp, Math.Max(1, damage));
            target.Hp -= dealt;
            target.Banner = main ? $"-{dealt}" : $"溅射 -{dealt}";
            Log($"攻击 {target.Name} {dealt}，剩余 HP {target.Hp}/{target.MaxHp}");
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

                if (ai.Chips >= GameBalance.MinBet)
                {
                    continue;
                }

                if (playerWon)
                {
                    ai.Hp = 0;
                    ai.Status = "斩杀";
                    ai.Banner = "濒死斩杀";
                    Log($"互助斩杀：{ai.Name} 破产，被你斩杀");
                }
                else if (winner != null && !winner.IsPlayer && winner != ai)
                {
                    var help = Math.Max(GameBalance.MinBet, (int)Math.Floor(Pot * ratio));
                    help = Math.Min(help, Math.Max(0, winner.Chips));
                    winner.Chips -= help;
                    ai.Chips += help;
                    ai.Banner = "获得援助";
                    ai.Status = "获救";
                    Log($"{winner.Name} 抽出 {help} 筹码救助 {ai.Name}");
                }
            }
        }

        private void AfterRound()
        {
            SyncPlayerChips();
            Phase = GamePhase.RoundSettle;
            if (!AnyEnemyAlive())
            {
                Hint = LastResult + "\n已击杀全部敌人，点击进入商店";
            }
            else if (Player.Chips < GameBalance.MinBet)
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
            var remain = Player.Chips;
            var gold = GameBalance.ConvertChipsToGold(remain);
            if (Run.DoubleGoldThisStage)
            {
                gold *= 2;
            }

            Run.Gold += gold;
            Log($"通关结算：剩余筹码 {remain} → {gold} 金币（总金币 {Run.Gold}）");
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
                GrantChips(GameBalance.MinBet);
                Log("借贷券生效，获得最低下注额");
                StartRound();
                return;
            }

            Phase = GamePhase.StageFail;
            Hint = "筹码不足。可看广告借贷继续，或重开本关。";
            Notify();
        }

        private void ContinueAfterLoan()
        {
            if (Player.Chips < GameBalance.MinBet)
            {
                Phase = GamePhase.StageFail;
                Hint = "仍不足以继续";
                Notify();
                return;
            }

            StartRound();
        }

        private bool TryCommitUnits(SeatState seat, int units, out int paid)
        {
            units = Math.Max(units, seat.StreetUnits);
            var cost = CostFor(seat, units) - CostFor(seat, seat.StreetUnits);
            if (Run.Affix == BossAffix.AntiRaise && seat.IsPlayer)
            {
                cost = (int)Math.Ceiling(cost * 1.5f);
            }

            paid = cost;
            if (cost > seat.Chips)
            {
                return false;
            }

            seat.Chips -= cost;
            seat.TotalBet += cost;
            seat.StreetUnits = units;
            Pot += cost;
            if (seat.IsPlayer)
            {
                Run.Chips = seat.Chips;
            }

            return true;
        }

        private int CostFor(SeatState seat, int units)
        {
            var looked = seat.IsPlayer && seat.Looked;
            return looked ? units * 2 : units;
        }

        private int UnitsAffordable(SeatState seat)
        {
            var denom = (seat.IsPlayer && seat.Looked) ? 2 : 1;
            if (Run.Affix == BossAffix.AntiRaise && seat.IsPlayer)
            {
                denom = Math.Max(1, (int)Math.Ceiling(denom * 1.5f));
            }

            return AlignBet(seat.Chips / denom);
        }

        private int MaxBetUnits()
        {
            var cap = UnitsAffordable(Player);
            if (Run.Tilted)
            {
                cap = Math.Min(cap, AlignBet(Player.Chips / 2 / (Player.Looked ? 2 : 1)));
            }

            return Math.Max(GameBalance.MinBet, cap);
        }

        private SeatState LowestHpPercentEnemy()
        {
            SeatState best = null;
            var bestPct = 2f;
            for (var i = 0; i < Enemies.Length; i++)
            {
                var e = Enemies[i];
                if (!e.Alive)
                {
                    continue;
                }

                var pct = e.MaxHp <= 0 ? 1f : (float)e.Hp / e.MaxHp;
                if (pct < bestPct)
                {
                    bestPct = pct;
                    best = e;
                }
            }

            return best;
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

            return seat.Alive;
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
            seat.TotalBet = 0;
            seat.Folded = false;
            seat.Looked = false;
            seat.Status = seat.Alive || seat.IsPlayer ? string.Empty : "未上场";
            if (!seat.IsPlayer && !seat.Alive)
            {
                seat.Status = seat.ActiveInStage ? "阵亡" : "未上场";
            }
        }

        private void GrantChips(int amount, bool reset = false)
        {
            if (reset)
            {
                Run.Chips = amount;
            }
            else
            {
                Run.Chips += amount;
            }

            Player.Chips = Run.Chips;
        }

        private void SyncPlayerChips()
        {
            Run.Chips = Player.Chips;
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

        private SeatState CreateSeat(int id, string name, bool player)
        {
            return new SeatState
            {
                Id = id,
                Name = name,
                IsPlayer = player,
                Chips = player ? GameBalance.PlayerStartChips : 0
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
    }
}
