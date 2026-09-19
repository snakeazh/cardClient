using System;
using System.Collections.Generic;
using CardShare.Contracts;
using CardShare.Contracts.Config;

#nullable enable

namespace CardShare.Battle
{
    public sealed class RelicCombatSnapshot
    {
        public IReadOnlyList<int> RelicIds { get; init; } = Array.Empty<int>();

        public IReadOnlyList<int> DisabledRelicIds { get; init; } = Array.Empty<int>();

        public IReadOnlyList<Card> Shown { get; init; } = Array.Empty<Card>();

        public IReadOnlyList<Card> Unshown { get; init; } = Array.Empty<Card>();

        public int RubsUsedThisHand { get; init; }

        public int PeekLeft { get; init; }

        public int XRayLeft { get; init; }

        public int ReplaceLeft { get; init; }

        public int LuckySevenHits { get; init; }

        public int HandTypeShowCount { get; init; }

        public float RubRelicMagForever { get; init; }

        public float HandTypeMagBonus { get; init; }

        public float PracticeMagForever { get; init; }

        public int DefeatMagStacks { get; init; }

        public int GoldSpentThisRun { get; init; }

        public IReadOnlyList<int> RankAttackBonus { get; init; } = Array.Empty<int>();
    }

    public readonly struct RelicCombatPart
    {
        public RelicCombatPart(int relicId, string name, float magAdd, float attackAdd)
        {
            RelicId = relicId;
            Name = name;
            MagAdd = magAdd;
            AttackAdd = attackAdd;
        }

        public int RelicId { get; }

        public string Name { get; }

        public float MagAdd { get; }

        public float AttackAdd { get; }
    }

    public readonly struct RelicCombatResult
    {
        public RelicCombatResult(float magExtra, float attackExtra, IReadOnlyList<RelicCombatPart> parts)
        {
            MagExtra = magExtra;
            AttackExtra = attackExtra;
            Parts = parts;
        }

        public float MagExtra { get; }

        public float AttackExtra { get; }

        public IReadOnlyList<RelicCombatPart> Parts { get; }

        public static RelicCombatResult Empty { get; } = new RelicCombatResult(0f, 0f, Array.Empty<RelicCombatPart>());
    }

    /// <summary>
    /// 圣物本手倍率/加攻。PVE 客户端与 PVP 服务端共用。
    /// </summary>
    public static class RelicCombat
    {
        public static RelicCombatResult Evaluate(IGameTables tables, RelicCombatSnapshot snapshot, HandScore score)
        {
            var disabled = ToSet(snapshot.DisabledRelicIds);
            var mag = 0f;
            var attack = 0f;
            var parts = new List<RelicCombatPart>();
            ForEachEnabled(tables, snapshot, disabled, (relic, entries) =>
            {
                var magAdd = 0f;
                var attackAdd = 0f;
                for (var i = 0; i < entries.Count; i++)
                {
                    var entry = entries[i];
                    magAdd += MultiplierFromEntry(tables, snapshot, disabled, entry, score);
                    attackAdd += AttackFromEntry(tables, snapshot, disabled, entry, score);
                }

                mag += magAdd;
                attack += attackAdd;
                if (magAdd == 0f && attackAdd == 0f)
                {
                    return;
                }

                parts.Add(new RelicCombatPart(relic.Id, relic.Name, magAdd, attackAdd));
            });

            if (!HasMechanism(tables, snapshot, disabled, MechanismType.RubbingCardRelic))
            {
                mag += snapshot.RubRelicMagForever;
            }

            if (!HasMechanism(tables, snapshot, disabled, MechanismType.ProOfUpCardType))
            {
                mag += snapshot.HandTypeMagBonus;
            }

            if (!HasMechanism(tables, snapshot, disabled, MechanismType.NoKillMonsterGetMagnification))
            {
                mag += snapshot.PracticeMagForever;
            }

            return new RelicCombatResult(mag, attack, parts);
        }

        private static float MultiplierFromEntry(
            IGameTables tables,
            RelicCombatSnapshot snapshot,
            HashSet<int> disabled,
            RelicEntryConfig entry,
            HandScore score)
        {
            var value = ValueAt(entry);
            switch (entry.Type)
            {
                case MechanismType.CardMagnification:
                    return value;
                case MechanismType.SquarePlate:
                    return CountSuit(tables, snapshot, disabled, score, Suit.Diamond) * value;
                case MechanismType.Spades:
                    return CountSuit(tables, snapshot, disabled, score, Suit.Spade) * value;
                case MechanismType.RedHeart:
                    return CountSuit(tables, snapshot, disabled, score, Suit.Heart) * value;
                case MechanismType.PlumBlossom:
                    return CountSuit(tables, snapshot, disabled, score, Suit.Club) * value;
                case MechanismType.Couplet:
                    return score.Type == HandType.Pair ? value : 0f;
                case MechanismType.Flush:
                    return score.Type == HandType.Flush ? value : 0f;
                case MechanismType.Straight:
                    return score.Type == HandType.Straight ? value : 0f;
                case MechanismType.StraightFlush:
                    return score.Type == HandType.StraightFlush ? value : 0f;
                case MechanismType.Leopard:
                    return score.Type == HandType.ThreeOfAKind ? value : 0f;
                case MechanismType.EvenNumberCard:
                    return CountEven(score) * value;
                case MechanismType.OddNumberCard:
                    return CountOdd(score) * value;
                case MechanismType.HeadCard:
                    return CountFace(tables, snapshot, disabled, score) * value;
                case MechanismType.SpecialACard:
                    return CountRank(score, Rank.Ace) * value;
                case MechanismType.Camera:
                    return CountUnshown(snapshot, black: true) * value;
                case MechanismType.Cupid:
                    return CountUnshown(snapshot, black: false) * value;
                case MechanismType.EveryUseRubbingNum:
                    return snapshot.RubsUsedThisHand * value;
                case MechanismType.NoSkill:
                    return snapshot.PeekLeft == 0 && snapshot.XRayLeft == 0 && snapshot.ReplaceLeft == 0 ? value : 0f;
                case MechanismType.EveryRelic:
                    return snapshot.RelicIds.Count * value;
                case MechanismType.NoUseRubbingEveryRubbingNum:
                    return snapshot.PeekLeft * value;
                case MechanismType.AccumulatedNumOfCardType:
                    return snapshot.HandTypeShowCount * value;
                case MechanismType.RubbingCardRelic:
                    return snapshot.RubRelicMagForever;
                case MechanismType.ProOfUpCardType:
                    return snapshot.HandTypeMagBonus;
                case MechanismType.SpecialSevenCard:
                    return snapshot.LuckySevenHits * value;
                case MechanismType.DefeatGetMagnification:
                    return DefeatMagBonus(tables, snapshot, disabled);
                case MechanismType.NoKillMonsterGetMagnification:
                    return snapshot.PracticeMagForever;
                default:
                    return 0f;
            }
        }

        private static float AttackFromEntry(
            IGameTables tables,
            RelicCombatSnapshot snapshot,
            HashSet<int> disabled,
            RelicEntryConfig entry,
            HandScore score)
        {
            var value = ValueAt(entry);
            switch (entry.Type)
            {
                case MechanismType.SquarePlateAttack:
                    return CountSuit(tables, snapshot, disabled, score, Suit.Diamond) * value;
                case MechanismType.SpadesAttack:
                    return CountSuit(tables, snapshot, disabled, score, Suit.Spade) * value;
                case MechanismType.RedHeartAttack:
                    return CountSuit(tables, snapshot, disabled, score, Suit.Heart) * value;
                case MechanismType.PlumBlossomAttack:
                    return CountSuit(tables, snapshot, disabled, score, Suit.Club) * value;
                case MechanismType.CoupletAttack:
                    return score.Type == HandType.Pair ? value : 0f;
                case MechanismType.StraightAttack:
                    return score.Type == HandType.Straight ? value : 0f;
                case MechanismType.FlushAttack:
                    return score.Type == HandType.Flush ? value : 0f;
                case MechanismType.StraightFlushAttack:
                    return score.Type == HandType.StraightFlush ? value : 0f;
                case MechanismType.LeopardAttack:
                    return score.Type == HandType.ThreeOfAKind ? value : 0f;
                case MechanismType.SpecialEightCard:
                    return CountRank(score, Rank.Eight) * value;
                case MechanismType.DoubleCardAttack:
                    return Math.Max(0, score.BaseChips) * value;
                case MechanismType.HeadCardAttack:
                    return CountFace(tables, snapshot, disabled, score) * value;
                case MechanismType.ACardAttack:
                    return CountRank(score, Rank.Ace) * value;
                case MechanismType.TheSwordOfVictory:
                    var factor = value == 0f ? 1f : value;
                    return MaxUnshownChip(snapshot) * factor;
                case MechanismType.ConsumeFundsGetAttack:
                    if (value <= 0f)
                    {
                        return 0f;
                    }

                    return (float)Math.Floor(snapshot.GoldSpentThisRun / (double)value);
                case MechanismType.SpecialSevenCardAttack:
                    return snapshot.LuckySevenHits * value;
                case MechanismType.CardProvideAttack:
                    return SumCardProvideAttack(snapshot, entry);
                case MechanismType.EveryRubbingNum:
                    return snapshot.PeekLeft * value;
                default:
                    return 0f;
            }
        }

        private static float SumCardProvideAttack(RelicCombatSnapshot snapshot, RelicEntryConfig entry)
        {
            var extra = 0f;
            for (var i = 0; i < entry.Value.Length; i++)
            {
                var index = (int)Math.Round(entry.Value[i]) - 1;
                if (index < 0 || index >= snapshot.Shown.Count)
                {
                    continue;
                }

                var card = snapshot.Shown[index];
                if (!card.IsValid)
                {
                    continue;
                }

                extra += card.ChipValue;
                extra += RankBonus(snapshot, card.Rank);
            }

            return extra;
        }

        private static int RankBonus(RelicCombatSnapshot snapshot, Rank rank)
        {
            var key = (int)rank;
            var bonus = snapshot.RankAttackBonus;
            if (key < 0 || key >= bonus.Count)
            {
                return 0;
            }

            return bonus[key];
        }

        private static int CountSuit(
            IGameTables tables,
            RelicCombatSnapshot snapshot,
            HashSet<int> disabled,
            HandScore score,
            Suit suit)
        {
            var cards = score.UsedCards;
            var dual = false;
            var displaySuit = default(Suit);
            if (HasMechanism(tables, snapshot, disabled, MechanismType.SpecialFlush) &&
                (score.Type == HandType.Flush || score.Type == HandType.StraightFlush))
            {
                dual = HandEvaluator.TryColorFlushDisplaySuit(cards, out displaySuit);
            }

            var count = 0;
            for (var i = 0; i < cards.Length; i++)
            {
                var real = cards[i].Suit;
                if (real == suit)
                {
                    count++;
                }
                else if (dual && displaySuit == suit)
                {
                    count++;
                }
            }

            return count;
        }

        private static int CountRank(HandScore score, Rank rank)
        {
            var cards = score.UsedCards;
            var count = 0;
            for (var i = 0; i < cards.Length; i++)
            {
                if (cards[i].Rank == rank)
                {
                    count++;
                }
            }

            return count;
        }

        private static int CountFace(
            IGameTables tables,
            RelicCombatSnapshot snapshot,
            HashSet<int> disabled,
            HandScore score)
        {
            var cards = score.UsedCards;
            if (HasMechanism(tables, snapshot, disabled, MechanismType.AllCardIsHeadCard))
            {
                return cards.Length;
            }

            var count = 0;
            for (var i = 0; i < cards.Length; i++)
            {
                if (cards[i].IsFace)
                {
                    count++;
                }
            }

            return count;
        }

        private static int CountEven(HandScore score)
        {
            var cards = score.UsedCards;
            var count = 0;
            for (var i = 0; i < cards.Length; i++)
            {
                var rank = cards[i].Rank;
                if (rank == Rank.Two || rank == Rank.Four || rank == Rank.Six || rank == Rank.Eight || rank == Rank.Ten)
                {
                    count++;
                }
            }

            return count;
        }

        private static int CountOdd(HandScore score)
        {
            var cards = score.UsedCards;
            var count = 0;
            for (var i = 0; i < cards.Length; i++)
            {
                var rank = cards[i].Rank;
                if (rank == Rank.Ace || rank == Rank.Three || rank == Rank.Five || rank == Rank.Seven || rank == Rank.Nine)
                {
                    count++;
                }
            }

            return count;
        }

        private static int CountUnshown(RelicCombatSnapshot snapshot, bool black)
        {
            var cards = snapshot.Unshown;
            var count = 0;
            for (var i = 0; i < cards.Count; i++)
            {
                var card = cards[i];
                if (!card.IsValid)
                {
                    continue;
                }

                var isBlack = card.Suit == Suit.Spade || card.Suit == Suit.Club;
                if (black == isBlack)
                {
                    count++;
                }
            }

            return count;
        }

        private static int MaxUnshownChip(RelicCombatSnapshot snapshot)
        {
            var cards = snapshot.Unshown;
            var max = 0;
            for (var i = 0; i < cards.Count; i++)
            {
                if (!cards[i].IsValid)
                {
                    continue;
                }

                var chip = cards[i].ChipValue;
                if (chip > max)
                {
                    max = chip;
                }
            }

            return max;
        }

        private static float DefeatMagBonus(IGameTables tables, RelicCombatSnapshot snapshot, HashSet<int> disabled)
        {
            if (snapshot.DefeatMagStacks <= 0)
            {
                return 0f;
            }

            var sum = 0f;
            ForEachEnabled(tables, snapshot, disabled, (_, entries) =>
            {
                for (var i = 0; i < entries.Count; i++)
                {
                    var entry = entries[i];
                    if (entry.Type != MechanismType.DefeatGetMagnification)
                    {
                        continue;
                    }

                    var unit = ValueAt(entry);
                    if (unit <= 0f)
                    {
                        unit = 1f;
                    }

                    var per = entry.Value.Length > 1 ? ValueAt(entry, 1) : unit;
                    sum += (float)Math.Floor(snapshot.DefeatMagStacks / (double)unit) * per;
                }
            });
            return sum;
        }

        private static bool HasMechanism(
            IGameTables tables,
            RelicCombatSnapshot snapshot,
            HashSet<int> disabled,
            MechanismType type)
        {
            var found = false;
            ForEachEnabled(tables, snapshot, disabled, (_, entries) =>
            {
                for (var i = 0; i < entries.Count; i++)
                {
                    if (entries[i].Type == type)
                    {
                        found = true;
                    }
                }
            });
            return found;
        }

        private static void ForEachEnabled(
            IGameTables tables,
            RelicCombatSnapshot snapshot,
            HashSet<int> disabled,
            Action<RelicConfig, List<RelicEntryConfig>> action)
        {
            var ids = snapshot.RelicIds;
            for (var i = 0; i < ids.Count; i++)
            {
                var relicId = ids[i];
                if (relicId <= 0 || disabled.Contains(relicId) || !tables.TryGetRelic(relicId, out var relic))
                {
                    continue;
                }

                var entries = CollectEntries(tables, relic);
                if (entries.Count == 0)
                {
                    continue;
                }

                action(relic, entries);
            }
        }

        private static List<RelicEntryConfig> CollectEntries(IGameTables tables, RelicConfig relic)
        {
            var list = new List<RelicEntryConfig>();
            var mechanismIds = relic.MechanismId;
            for (var i = 0; i < mechanismIds.Length; i++)
            {
                if (tables.TryGetRelicEntry(mechanismIds[i], out var entry))
                {
                    list.Add(entry);
                }
            }

            return list;
        }

        private static HashSet<int> ToSet(IReadOnlyList<int> ids)
        {
            var set = new HashSet<int>();
            for (var i = 0; i < ids.Count; i++)
            {
                if (ids[i] > 0)
                {
                    set.Add(ids[i]);
                }
            }

            return set;
        }

        private static float ValueAt(RelicEntryConfig entry, int index = 0)
        {
            if (index >= entry.Value.Length)
            {
                return 0f;
            }

            return entry.Value[index];
        }
    }
}
