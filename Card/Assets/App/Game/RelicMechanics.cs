using System;
using System.Collections.Generic;
using App.Config;
using CardShare.Contracts.Config;

namespace App.Game
{
    /// <summary>本手结算上下文：亮出/未亮出牌、搓牌次数、幸运七掷骰等。不要只靠 HandScore。</summary>
    public readonly struct RelicCombatContext
    {
        public RelicCombatContext(
            Card[] unshown,
            Card[] shown,
            int rubsUsedThisHand,
            bool rubbedThisHand,
            int peekLeft,
            int xrayLeft,
            int replaceLeft,
            int luckySevenHits,
            bool treatAllAsFace)
        {
            Unshown = unshown ?? Array.Empty<Card>();
            Shown = shown ?? Array.Empty<Card>();
            RubsUsedThisHand = Math.Max(0, rubsUsedThisHand);
            RubbedThisHand = rubbedThisHand;
            PeekLeft = Math.Max(0, peekLeft);
            XRayLeft = Math.Max(0, xrayLeft);
            ReplaceLeft = Math.Max(0, replaceLeft);
            LuckySevenHits = Math.Max(0, luckySevenHits);
            TreatAllAsFace = treatAllAsFace;
        }

        public Card[] Unshown { get; }
        /// <summary>亮出牌，手牌槽位从左到右，与 CopySelectedCards 一致。</summary>
        public Card[] Shown { get; }
        public int RubsUsedThisHand { get; }
        public bool RubbedThisHand { get; }
        public int PeekLeft { get; }
        public int XRayLeft { get; }
        public int ReplaceLeft { get; }
        public int LuckySevenHits { get; }
        public bool TreatAllAsFace { get; }

        public static RelicCombatContext Empty { get; } = new RelicCombatContext(
            Array.Empty<Card>(), Array.Empty<Card>(), 0, false, 0, 0, 0, 0, false);
    }

    /// <summary>
    /// 按 RelicConfig / RelicEntryConfig 结算已购商品效果。收藏禁用的商品整件跳过。
    /// </summary>
    public static class RelicMechanics
    {
        public static bool IsDisabled(RunState run, int relicId)
        {
            return run != null && relicId > 0 && run.DisabledRelicIds != null && run.DisabledRelicIds.Contains(relicId);
        }

        public static float SumMultiplierExtra(RunState run, HandScore score, RelicCombatContext ctx = default)
        {
            return EvaluateShared(run, score, ctx).MagExtra;
        }

        public static float SumAttackExtra(RunState run, HandScore score, RelicCombatContext ctx = default)
        {
            return EvaluateShared(run, score, ctx).AttackExtra;
        }

        /// <summary>按装备栏顺序收集本手触发的倍率/攻击加成，一件装备一条。</summary>
        public static void CollectRelicBonuses(
            RunState run,
            HandScore score,
            List<RelicBonusPart> dest,
            RelicCombatContext ctx = default)
        {
            dest?.Clear();
            if (dest == null)
            {
                return;
            }

            var parts = EvaluateShared(run, score, ctx).Parts;
            for (var i = 0; i < parts.Count; i++)
            {
                var part = parts[i];
                dest.Add(new RelicBonusPart(part.RelicId, part.MagAdd, part.AttackAdd));
            }
        }

        /// <summary>本手触发了倍率加成的遗物配置 Id，槽位可据此播 NumberShackLow。</summary>
        public static void CollectContributingRelicIds(
            RunState run,
            HandScore score,
            List<int> dest,
            RelicCombatContext ctx = default)
        {
            dest?.Clear();
            if (dest == null)
            {
                return;
            }

            var parts = EvaluateShared(run, score, ctx).Parts;
            for (var i = 0; i < parts.Count; i++)
            {
                var part = parts[i];
                if ((part.MagAdd != 0f || part.AttackAdd != 0f) && !dest.Contains(part.RelicId))
                {
                    dest.Add(part.RelicId);
                }
            }
        }

        /// <summary>已触发的倍率词条，如「粗制长剑+1, 青铜项链+1」。未触发的不写。</summary>
        public static string CollectMultiplierParts(RunState run, HandScore score, RelicCombatContext ctx = default)
        {
            string text = null;
            var parts = EvaluateShared(run, score, ctx).Parts;
            for (var i = 0; i < parts.Count; i++)
            {
                var part = parts[i];
                if (part.MagAdd == 0f)
                {
                    continue;
                }

                var add = part.MagAdd;
                var name = !string.IsNullOrEmpty(part.Name) ? part.Name : part.RelicId.ToString();
                var piece = add == (int)add ? $"{name}+{(int)add}" : $"{name}+{add}";
                text = text == null ? piece : text + ", " + piece;
            }

            // 与 SumMultiplierExtra 一致：对应圣物已不在栏时，永久累计仍计入遗物加成。
            if (!HasMechanism(run, MechanismType.RubbingCardRelic) &&
                run != null &&
                run.RubRelicMagForever != 0f)
            {
                var add = run.RubRelicMagForever;
                var piece = add == (int)add ? $"老搓家+{(int)add}" : $"老搓家+{add}";
                text = text == null ? piece : text + ", " + piece;
            }

            if (!HasMechanism(run, MechanismType.ProOfUpCardType) &&
                run != null)
            {
                var add = run.HandTypeMagBonus(score.Type);
                if (add != 0f)
                {
                    var piece = add == (int)add ? $"牌型永久+{(int)add}" : $"牌型永久+{add}";
                    text = text == null ? piece : text + ", " + piece;
                }
            }

            if (!HasMechanism(run, MechanismType.NoKillMonsterGetMagnification) &&
                run != null &&
                run.PracticeMagForever != 0f)
            {
                var add = run.PracticeMagForever;
                var piece = add == (int)add ? $"练习卷+{(int)add}" : $"练习卷+{add}";
                text = text == null ? piece : text + ", " + piece;
            }

            return text ?? string.Empty;
        }

        public static string CollectAttackParts(RunState run, HandScore score, RelicCombatContext ctx = default)
        {
            string text = null;
            var parts = EvaluateShared(run, score, ctx).Parts;
            for (var i = 0; i < parts.Count; i++)
            {
                var part = parts[i];
                if (part.AttackAdd == 0f)
                {
                    continue;
                }

                var name = !string.IsNullOrEmpty(part.Name) ? part.Name : part.RelicId.ToString();
                var piece = $"{name}+{(int)Math.Round(part.AttackAdd)}";
                text = text == null ? piece : text + ", " + piece;
            }

            return text ?? string.Empty;
        }

        public static float SumValue(RunState run, MechanismType type, int index = 0)
        {
            var sum = 0f;
            ForEachEntry(run, (_, entry) =>
            {
                if (entry.Type == type)
                {
                    sum += ValueAt(entry, index);
                }
            });
            return sum;
        }

        public static float SumValueForRelic(int relicId, MechanismType type, int index = 0)
        {
            var relic = RelicConfig.Get(relicId);
            var sum = 0f;
            ForEachRelicEntry(relic, entry =>
            {
                if (entry.Type == type)
                {
                    sum += ValueAt(entry, index);
                }
            });
            return sum;
        }

        /// <summary>词条 <c>Value</c> 已是数组；缺项或越界返回 0。</summary>
        public static float ValueAt(RelicEntryConfig entry, int index = 0)
        {
            if (entry?.Value == null || index < 0 || index >= entry.Value.Length)
            {
                return 0f;
            }

            return entry.Value[index];
        }

        public static bool Roll(RunState run, MechanismType type, Random rng)
        {
            var chance = SumProbability(run, type);
            if (chance <= 0f || rng == null)
            {
                return false;
            }

            return rng.NextDouble() < chance;
        }

        public static HandScore WithType(HandScore score, HandType type)
        {
            if (score.Type == type)
            {
                return score;
            }

            return new HandScore(
                type,
                score.BaseChips,
                HandEvaluator.TypeMultiplier(type),
                score.Keys,
                score.UsedCards,
                HandEvaluator.TypeName(type),
                score.BeatsAll);
        }

        public static HandType ClampType(HandType type)
        {
            return HandEvaluator.TypeByLevel(HandEvaluator.TypeLevel(type));
        }

        public static HandType ShiftType(HandType type, int delta)
        {
            return HandEvaluator.TypeByLevel(HandEvaluator.TypeLevel(type) + delta);
        }

        public static HandScore ApplyPlayerTypeRewrite(RunState run, HandScore score)
        {
            var steps = (int)Math.Round(
                SumValue(run, MechanismType.CardUpGrade) +
                SumValue(run, MechanismType.UpLevel));
            if (steps == 0)
            {
                return score;
            }

            return WithType(score, ShiftType(score.Type, steps));
        }

        /// <summary>
        /// 降低：敌人牌型按 Level +Value 改写（比牌、提示、出伤倍率一起变）。
        /// 散牌已是最低，再降无效。须在好运来等牌型改写之后调用。
        /// </summary>
        public static HandScore ApplyCompareRankBonus(RunState run, HandScore score)
        {
            var steps = (int)Math.Round(SumValue(run, MechanismType.ReduceLevel));
            if (steps == 0)
            {
                return score;
            }

            var type = ShiftType(score.Type, steps);
            if (type == score.Type)
            {
                return score;
            }

            return new HandScore(
                type,
                score.BaseChips,
                HandEvaluator.TypeMultiplier(type),
                Array.Empty<int>(),
                score.UsedCards,
                HandEvaluator.TypeName(type),
                score.BeatsAll);
        }

        public static HandScore ApplyEnemyTypeRewrite(RunState run, HandScore score, int downgradeSteps)
        {
            var type = score.Type;
            if (downgradeSteps > 0)
            {
                type = ShiftType(type, -downgradeSteps);
            }

            if (HasMechanism(run, MechanismType.LuckyFlush) &&
                HandEvaluator.TypeLevel(type) > HandEvaluator.TypeLevel(HandType.Flush))
            {
                type = HandType.Flush;
            }

            if (HasMechanism(run, MechanismType.LuckyStraight) &&
                HandEvaluator.TypeLevel(type) > HandEvaluator.TypeLevel(HandType.Straight))
            {
                type = HandType.Straight;
            }

            if (HasMechanism(run, MechanismType.LuckyCouplet) &&
                HandEvaluator.TypeLevel(type) > HandEvaluator.TypeLevel(HandType.Pair))
            {
                type = HandType.Pair;
            }

            return WithType(score, type);
        }

        /// <summary>玩家相对敌人的牌型差：正数表示玩家更大。差值为配置 Level。</summary>
        public static int TypeGap(HandType playerType, HandType enemyType)
        {
            return HandEvaluator.TypeLevel(playerType) - HandEvaluator.TypeLevel(enemyType);
        }

        public static float GapDamagePercent(RunState run, HandType playerType, HandType enemyType, bool outgoing)
        {
            var unit = SumValue(run, MechanismType.GapDamage);
            var per = SumValue(run, MechanismType.GapDamage, 1);
            if (unit <= 0f || per == 0f)
            {
                return 0f;
            }

            var ranks = TypeGap(playerType, enemyType) / unit;
            return (outgoing ? ranks : -ranks) * per;
        }

        /// <summary>贪欲之冠：floor(金币 / Value[0]) × Value[1]，层数上限 Value[2]。</summary>
        public static float GoldDamageScalePercent(RunState run)
        {
            var percent = 0f;
            ForEachEntry(run, (_, entry) =>
            {
                if (entry.Type != MechanismType.GoldDamageScale || run == null)
                {
                    return;
                }

                var unit = ValueAt(entry);
                var per = ValueAt(entry, 1);
                if (unit <= 0f || per == 0f)
                {
                    return;
                }

                var stacks = (int)Math.Floor(run.Gold / (double)unit);
                var cap = entry.Value != null && entry.Value.Length > 2
                    ? (int)Math.Round(ValueAt(entry, 2))
                    : int.MaxValue;
                if (cap > 0)
                {
                    stacks = Math.Min(stacks, cap);
                }

                percent += stacks * per;
            });
            return percent;
        }

        /// <summary>「自身攻击力」：次数 Value[0]，倍率缺省为 1。</summary>
        public static int AttackPowerDamage(RelicEntryConfig entry, int attack)
        {
            var hits = Math.Max(0, (int)Math.Round(ValueAt(entry, 0)));
            var factor = entry?.Value != null && entry.Value.Length > 1 ? ValueAt(entry, 1) : 1f;
            if (hits <= 0 || attack <= 0)
            {
                return 0;
            }

            return Math.Max(0, (int)Math.Round(attack * factor * hits));
        }

        public static float StackedValue(RunState run, MechanismType type, int stacks)
        {
            if (run == null || stacks <= 0)
            {
                return 0f;
            }

            var sum = 0f;
            ForEachEntry(run, (_, entry) =>
            {
                if (entry.Type != type)
                {
                    return;
                }

                var unit = ValueAt(entry);
                if (unit <= 0f)
                {
                    unit = 1f;
                }

                var per = entry.Value != null && entry.Value.Length > 1 ? ValueAt(entry, 1) : unit;
                var cap = entry.Value != null && entry.Value.Length > 1
                    ? (int)Math.Round(ValueAt(entry, 1))
                    : int.MaxValue;
                if (type == MechanismType.EveryRoundEndingGetCritical ||
                    type == MechanismType.EveryRoundEndingGetEvade ||
                    type == MechanismType.LossRampDamage)
                {
                    var used = Math.Min(stacks, cap > 0 ? cap : stacks);
                    sum += used * unit;
                    return;
                }

                sum += (float)Math.Floor(stacks / (double)unit) * per;
            });
            return sum;
        }

        /// <summary>幸运之子：这些机制的 Value 是触发概率，会被翻倍；暴击/闪避等属性值与层数上限不在此列。</summary>
        private static bool IsProbabilityType(MechanismType type)
        {
            switch (type)
            {
                case MechanismType.ReverseResult:
                case MechanismType.AllPeacePer:
                case MechanismType.SteppingStone:
                case MechanismType.ProOfUpCardType:
                case MechanismType.ProOfHeadCardFunds:
                case MechanismType.EveryRoundEndingGetGoldPer:
                case MechanismType.DownGrade:
                case MechanismType.SpecialSevenCardPro:
                case MechanismType.SelfDestroyPerRound:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>读词条概率值；持有幸运之子时概率翻倍。</summary>
        public static float ProbabilityValue(RunState run, RelicEntryConfig entry, int index = 0)
        {
            var value = ValueAt(entry, index);
            if (value > 0f && IsProbabilityType(entry.Type) && HasMechanism(run, MechanismType.LuckyDouble))
            {
                value *= 2f;
            }

            return value;
        }

        /// <summary>按概率语义求和：幸运之子只翻概率型机制。</summary>
        public static float SumProbability(RunState run, MechanismType type, int index = 0)
        {
            var sum = 0f;
            ForEachEntry(run, (_, entry) =>
            {
                if (entry.Type == type)
                {
                    sum += ProbabilityValue(run, entry, index);
                }
            });
            return sum;
        }

        /// <summary>逐条机制独立掷骰（逆转沙漏与第六感分开判定），命中返回 true 并给出触发圣物名。</summary>
        public static bool RollEach(RunState run, MechanismType type, Random rng, out string relicName)
        {
            relicName = null;
            if (run == null || rng == null)
            {
                return false;
            }

            var hit = false;
            string hitName = null;
            ForEachEntry(run, (relic, entry) =>
            {
                if (hit || entry.Type != type)
                {
                    return;
                }

                var chance = ProbabilityValue(run, entry);
                if (chance > 0f && rng.NextDouble() < chance)
                {
                    hit = true;
                    hitName = relic?.Name;
                }
            });
            relicName = hitName;
            return hit;
        }

        /// <summary>高档甜品：当前剩余自衰减攻击合计（加在英雄攻击面板上）。缺键懒初始化满值。</summary>
        public static int SelfDecayAttackTotal(RunState run)
        {
            var total = 0;
            ForEachEntry(run, (relic, entry) =>
            {
                if (entry.Type == MechanismType.SelfDecayAttack)
                {
                    total += SelfDecayAttackLeftFor(run, relic, entry);
                }
            });
            return total;
        }

        /// <summary>高档饮品：某遗物剩余自衰减倍率。缺键懒初始化满值。</summary>
        public static float SelfDecayMagLeftFor(RunState run, int relicId)
        {
            var relic = RelicConfig.Get(relicId);
            var left = 0f;
            ForEachRelicEntry(relic, entry =>
            {
                if (entry.Type == MechanismType.SelfDecayMult)
                {
                    left += SelfDecayMagLeftFor(run, relic, entry);
                }
            });
            return left;
        }

        /// <summary>每次亮牌结算后调用：自衰减攻击/倍率各降一档（下限 0），返回要从英雄攻击扣掉的总量。</summary>
        public static int TickSelfDecay(RunState run)
        {
            var attackLoss = 0;
            if (run == null)
            {
                return 0;
            }

            ForEachEntry(run, (relic, entry) =>
            {
                if (entry.Type == MechanismType.SelfDecayAttack)
                {
                    var left = SelfDecayAttackLeftFor(run, relic, entry);
                    var decay = Math.Max(0, (int)Math.Round(ValueAt(entry, 1)));
                    var loss = Math.Min(left, decay);
                    if (loss > 0)
                    {
                        run.SelfDecayAttackLeft[relic.Id] = left - loss;
                        attackLoss += loss;
                    }
                }
                else if (entry.Type == MechanismType.SelfDecayMult)
                {
                    var left = SelfDecayMagLeftFor(run, relic, entry);
                    var decay = Math.Max(0f, ValueAt(entry, 1));
                    var loss = Math.Min(left, decay);
                    if (loss > 0f)
                    {
                        run.SelfDecayMagLeft[relic.Id] = left - loss;
                    }
                }
            });
            return attackLoss;
        }

        /// <summary>贪婪：每次比牌胜利自身倍率 +Value[0]，失败 -Value[1]，下限 0。跨关保留，卖掉重买重置。</summary>
        public static void OnCompareResult(RunState run, bool won)
        {
            if (run == null)
            {
                return;
            }

            ForEachEntry(run, (relic, entry) =>
            {
                if (entry.Type != MechanismType.SelfMultWinLose || relic == null)
                {
                    return;
                }

                run.WinLoseRelicMag.TryGetValue(relic.Id, out var current);
                var delta = won ? ValueAt(entry) : -ValueAt(entry, 1);
                run.WinLoseRelicMag[relic.Id] = Math.Max(0f, current + delta);
            });
        }

        /// <summary>商店每刷新一次（含免费刷新）调用：持有中的消费主义计数 +1。</summary>
        public static void OnShopRefreshed(RunState run)
        {
            if (run == null)
            {
                return;
            }

            ForEachEntry(run, (relic, entry) =>
            {
                if (entry.Type != MechanismType.ShopRefreshGetMult)
                {
                    return;
                }

                run.ShopRefreshRelicCounts.TryGetValue(relic.Id, out var count);
                run.ShopRefreshRelicCounts[relic.Id] = count + 1;
            });
        }

        /// <summary>倍率叠加 / 力量叠加：每次使用消耗类圣物后 +1。</summary>
        public static void OnConsumableUsed(RunState run)
        {
            if (run == null)
            {
                return;
            }

            run.ConsumableUsesThisRun++;
        }

        /// <summary>复制：每回合随机选一个其它已持有圣物；无候选则清零。</summary>
        public static void RefreshCopiedRelic(RunState run, Random rng)
        {
            if (run == null)
            {
                return;
            }

            run.RoundCopiedRelicId = 0;
            if (rng == null || !HasMechanism(run, MechanismType.CopyRandomRelic))
            {
                return;
            }

            var candidates = new List<int>();
            for (var i = 0; i < run.RelicConfigIds.Count; i++)
            {
                var id = run.RelicConfigIds[i];
                if (id <= 0 || IsDisabled(run, id))
                {
                    continue;
                }

                var relic = RelicConfig.Get(id);
                if (relic == null || IsConsumable(relic))
                {
                    continue;
                }

                var isCopy = false;
                ForEachRelicEntry(relic, entry =>
                {
                    if (entry.Type == MechanismType.CopyRandomRelic)
                    {
                        isCopy = true;
                    }
                });
                if (isCopy)
                {
                    continue;
                }

                candidates.Add(id);
            }

            if (candidates.Count == 0)
            {
                return;
            }

            run.RoundCopiedRelicId = candidates[rng.Next(candidates.Count)];
        }

        /// <summary>召唤：亮出同花顺且栏位有空时，随机塞入 1 个消耗品圣物。成功返回 true。</summary>
        public static bool TrySummonConsumable(RunState run, HandType shownType, Random rng, int carryMax, out RelicConfig summoned)
        {
            summoned = null;
            if (run == null || rng == null || shownType != HandType.StraightFlush)
            {
                return false;
            }

            if (!HasMechanism(run, MechanismType.SummonConsumable))
            {
                return false;
            }

            if (run.RelicConfigIds.Count >= carryMax)
            {
                return false;
            }

            var pool = new List<RelicConfig>();
            foreach (var kv in RelicConfig.All)
            {
                var relic = kv.Value;
                if (relic == null || !IsConsumable(relic) || run.RelicConfigIds.Contains(relic.Id))
                {
                    continue;
                }

                pool.Add(relic);
            }

            if (pool.Count == 0)
            {
                return false;
            }

            summoned = pool[rng.Next(pool.Count)];
            run.RelicConfigIds.Add(summoned.Id);
            return true;
        }

        /// <summary>
        /// 清理已不持有的遗物追踪器（衰减/刷新计数），重买时从头来过。返回需从英雄攻击扣掉的剩余自衰减攻击。
        /// </summary>
        public static int CleanupRelicTrackers(RunState run)
        {
            var attackLoss = 0;
            if (run == null)
            {
                return 0;
            }

            attackLoss = PruneUnownedAttack(run);
            PruneUnowned(run.SelfDecayMagLeft, run);
            PruneUnowned(run.ShopRefreshRelicCounts, run);
            PruneUnowned(run.WinLoseRelicMag, run);
            return attackLoss;
        }

        private static int PruneUnownedAttack(RunState run)
        {
            var dict = run.SelfDecayAttackLeft;
            if (dict.Count == 0)
            {
                return 0;
            }

            var loss = 0;
            List<int> stale = null;
            foreach (var kv in dict)
            {
                if (!run.RelicConfigIds.Contains(kv.Key))
                {
                    loss += kv.Value;
                    (stale ??= new List<int>()).Add(kv.Key);
                }
            }

            if (stale != null)
            {
                for (var i = 0; i < stale.Count; i++)
                {
                    dict.Remove(stale[i]);
                }
            }

            return loss;
        }

        private static void PruneUnowned<T>(Dictionary<int, T> dict, RunState run)
        {
            if (dict.Count == 0)
            {
                return;
            }

            List<int> stale = null;
            foreach (var key in dict.Keys)
            {
                if (!run.RelicConfigIds.Contains(key))
                {
                    (stale ??= new List<int>()).Add(key);
                }
            }

            if (stale == null)
            {
                return;
            }

            for (var i = 0; i < stale.Count; i++)
            {
                dict.Remove(stale[i]);
            }
        }

        private static int SelfDecayAttackLeftFor(RunState run, RelicConfig relic, RelicEntryConfig entry)
        {
            if (run == null || relic == null)
            {
                return 0;
            }

            if (run.SelfDecayAttackLeft.TryGetValue(relic.Id, out var left))
            {
                return left;
            }

            left = Math.Max(0, (int)Math.Round(ValueAt(entry)));
            run.SelfDecayAttackLeft[relic.Id] = left;
            return left;
        }

        private static float SelfDecayMagLeftFor(RunState run, RelicConfig relic, RelicEntryConfig entry)
        {
            if (run == null || relic == null)
            {
                return 0f;
            }

            if (run.SelfDecayMagLeft.TryGetValue(relic.Id, out var left))
            {
                return left;
            }

            left = Math.Max(0f, ValueAt(entry));
            run.SelfDecayMagLeft[relic.Id] = left;
            return left;
        }

        public static bool HasMechanism(RunState run, MechanismType type)
        {
            var found = false;
            ForEachEntry(run, (_, entry) =>
            {
                if (entry.Type == type)
                {
                    found = true;
                }
            });
            return found;
        }

        public static int SellPrice(RunState run, int relicId)
        {
            var relic = RelicConfig.Get(relicId);
            if (relic == null)
            {
                return 0;
            }

            var bonus = run != null ? run.RelicSellPriceBonus(relicId) : 0;
            return Math.Max(0, relic.SellingPrice + bonus);
        }

        public static int GoldFloor(RunState run)
        {
            if (!HasMechanism(run, MechanismType.Liability))
            {
                return 0;
            }

            return -(int)Math.Round(SumValue(run, MechanismType.Liability));
        }

        public static bool CanAfford(RunState run, int cost)
        {
            var gold = run != null ? run.Gold : 0;
            return gold - Math.Max(0, cost) >= GoldFloor(run);
        }

        public static RelicCombatContext BuildCombatContext(
            RunState run,
            HandScore score,
            Card[] unshown,
            Card[] shown,
            int rubsUsedThisHand,
            bool rubbedThisHand,
            Random rng)
        {
            var treatAllAsFace = HasMechanism(run, MechanismType.AllCardIsHeadCard);
            var luckyHits = 0;
            var chance = SumProbability(run, MechanismType.SpecialSevenCardPro);
            if (chance > 0f && score.UsedCards != null && rng != null)
            {
                for (var i = 0; i < score.UsedCards.Length; i++)
                {
                    if (score.UsedCards[i].Rank != Rank.Seven)
                    {
                        continue;
                    }

                    if (rng.NextDouble() < chance)
                    {
                        luckyHits++;
                    }
                }
            }

            return new RelicCombatContext(
                unshown,
                shown,
                rubsUsedThisHand,
                rubbedThisHand,
                run != null ? Math.Max(0, run.PeekGoodCharges) : 0,
                run != null ? Math.Max(0, run.ChaKanGoodCharges) : 0,
                run != null ? Math.Max(0, run.TiHuanGoodCharges) : 0,
                luckyHits,
                treatAllAsFace);
        }

        /// <summary>散牌 2+3+5 视为豹子，且比牌通杀。</summary>
        public static HandScore ApplyTwoThreeFive(HandScore score)
        {
            if (score.Type != HandType.HighCard || !IsNaturalTwoThreeFive(score.UsedCards))
            {
                return score;
            }

            return new HandScore(
                HandType.ThreeOfAKind,
                score.BaseChips,
                HandEvaluator.TypeMultiplier(HandType.ThreeOfAKind),
                score.Keys,
                score.UsedCards,
                "豹子 235",
                true);
        }

        public static void ForEachEntry(RunState run, Action<RelicConfig, RelicEntryConfig> action)
        {
            if (run == null || action == null)
            {
                return;
            }

            var ids = run.RelicConfigIds;
            if (ids == null)
            {
                return;
            }

            for (var i = 0; i < ids.Count; i++)
            {
                var relicId = ids[i];
                if (IsDisabled(run, relicId))
                {
                    continue;
                }

                var relic = RelicConfig.Get(relicId);
                ForEachRelicEntry(relic, entry => action(relic, entry));
            }
        }

        public static RelicUseType UseTypeOf(RelicConfig relic)
        {
            if (relic == null)
            {
                return RelicUseType.Passive;
            }

            switch (relic.UseType)
            {
                case (int)RelicUseType.InstantConsume:
                    return RelicUseType.InstantConsume;
                case (int)RelicUseType.PermanentConsume:
                    return RelicUseType.PermanentConsume;
                default:
                    return RelicUseType.Passive;
            }
        }

        public static bool IsConsumable(RelicConfig relic)
        {
            var type = UseTypeOf(relic);
            return type == RelicUseType.InstantConsume || type == RelicUseType.PermanentConsume;
        }

        public static bool RequiresComparePhase(RelicConfig relic)
        {
            var required = false;
            ForEachRelicEntry(relic, entry =>
            {
                switch (entry.Type)
                {
                    case MechanismType.UseDamageMul:
                    case MechanismType.UseDamageFixed:
                    case MechanismType.UseRoundNullify:
                        required = true;
                        break;
                }
            });
            return required;
        }

        public static void ForEachRelicEntry(RelicConfig relic, Action<RelicEntryConfig> action)
        {
            if (relic?.MechanismId == null || action == null)
            {
                return;
            }

            for (var i = 0; i < relic.MechanismId.Length; i++)
            {
                var entry = RelicEntryConfig.Get(relic.MechanismId[i]);
                if (entry != null)
                {
                    action(entry);
                }
            }
        }

        private static CardShare.Battle.RelicCombatResult EvaluateShared(
            RunState run,
            HandScore score,
            RelicCombatContext ctx)
        {
            return CardShare.Battle.RelicCombat.Evaluate(
                UnityGameConfigLoader.Current,
                ToSnapshot(run, score, ctx),
                SharedBattleBridge.ToShared(score));
        }

        private static CardShare.Battle.RelicCombatSnapshot ToSnapshot(
            RunState run,
            HandScore score,
            RelicCombatContext ctx)
        {
            var ids = run != null && run.RelicConfigIds != null
                ? (IReadOnlyList<int>)run.RelicConfigIds
                : Array.Empty<int>();
            var disabled = run != null && run.DisabledRelicIds != null && run.DisabledRelicIds.Count > 0
                ? new List<int>(run.DisabledRelicIds)
                : (IReadOnlyList<int>)Array.Empty<int>();
            var rankBonus = new int[14];
            if (run != null)
            {
                for (var i = 0; i < rankBonus.Length; i++)
                {
                    rankBonus[i] = run.RankAttackBonus((Rank)i);
                }
            }

            return new CardShare.Battle.RelicCombatSnapshot
            {
                RelicIds = ids,
                DisabledRelicIds = disabled,
                Shown = SharedBattleBridge.ToShared(ctx.Shown),
                Unshown = SharedBattleBridge.ToShared(ctx.Unshown),
                RubsUsedThisHand = ctx.RubsUsedThisHand,
                PeekLeft = ctx.PeekLeft,
                XRayLeft = ctx.XRayLeft,
                ReplaceLeft = ctx.ReplaceLeft,
                LuckySevenHits = ctx.LuckySevenHits,
                HandTypeShowCount = run != null ? run.HandTypeShowCount(score.Type) : 0,
                RubRelicMagForever = run != null ? run.RubRelicMagForever : 0f,
                HandTypeMagBonus = run != null ? run.HandTypeMagBonus(score.Type) : 0f,
                PracticeMagForever = run != null ? run.PracticeMagForever : 0f,
                DefeatMagStacks = run != null ? run.DefeatMagStacks : 0,
                GoldSpentThisRun = run != null ? run.GoldSpentThisRun : 0,
                RankAttackBonus = rankBonus,
                HandTypeShowCounts = run != null
                    ? (int[])run.HandTypeShowCounts.Clone()
                    : Array.Empty<int>(),
                RelicSelfDecayMag = BuildSelfDecayMag(run),
                RelicShopRefreshCounts = run != null ? run.ShopRefreshRelicCounts : null,
                RelicWinLoseMag = run != null ? run.WinLoseRelicMag : null,
                ConsumableUsesThisRun = run != null ? run.ConsumableUsesThisRun : 0,
                CopiedRelicId = run != null ? run.RoundCopiedRelicId : 0
            };
        }

        /// <summary>高档饮品：各持有遗物的剩余自衰减倍率（懒初始化满值），供共享结算读取。</summary>
        private static Dictionary<int, float> BuildSelfDecayMag(RunState run)
        {
            if (run == null)
            {
                return null;
            }

            Dictionary<int, float> map = null;
            ForEachEntry(run, (relic, entry) =>
            {
                if (entry.Type != MechanismType.SelfDecayMult)
                {
                    return;
                }

                map ??= new Dictionary<int, float>();
                map[relic.Id] = SelfDecayMagLeftFor(run, relic, entry);
            });
            return map;
        }

        /// <summary>亮出用牌恰好是 2+3+5，不依赖遗物改牌型。</summary>
        public static bool IsNaturalTwoThreeFive(Card[] cards)
        {
            if (cards == null || cards.Length != GameBalance.OpenHandSize)
            {
                return false;
            }

            var two = false;
            var three = false;
            var five = false;
            for (var i = 0; i < cards.Length; i++)
            {
                switch (cards[i].Rank)
                {
                    case Rank.Two:
                        two = true;
                        break;
                    case Rank.Three:
                        three = true;
                        break;
                    case Rank.Five:
                        five = true;
                        break;
                    default:
                        return false;
                }
            }

            return two && three && five;
        }
    }

    public readonly struct RelicBonusPart
    {
        public RelicBonusPart(int relicId, float multiplierAdd, float attackAdd)
        {
            RelicId = relicId;
            MultiplierAdd = multiplierAdd;
            AttackAdd = attackAdd;
        }

        public int RelicId { get; }
        public float MultiplierAdd { get; }
        public float AttackAdd { get; }
    }
}
