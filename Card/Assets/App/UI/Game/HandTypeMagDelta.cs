using System.Collections.Generic;
using App.Game;

namespace App.UI
{
    /// <summary>一次消耗品使用造成的「某牌型永久倍率」变化。</summary>
    public readonly struct HandTypeMagRaise
    {
        public HandTypeMagRaise(HandType type, float delta)
        {
            Type = type;
            Delta = delta;
        }

        public HandType Type { get; }
        public float Delta { get; }
    }

    /// <summary>
    /// 用「使用前拍快照 + 使用后求差」抓出消耗品加了哪些牌型的永久倍率。
    /// 这样 UI 不必关心 GameSession 内部走的是 <c>HandTypeMagUp</c> 还是 <c>RandomHandTypeMagUp</c>，
    /// 随机抽 3 种（时光宝盒）也能全部抓出来；对比 Phase 等方式更不容易误判。
    /// 注意：<see cref="HandType"/> 是 0 基枚举（HighCard = 0），配置表 HandType 是 1 基，索引一律用运行时枚举。
    /// </summary>
    public static class HandTypeMagDelta
    {
        /// <summary>参与结算的牌型数量（HighCard ~ ThreeOfAKind）。</summary>
        public const int HandTypeCount = (int)HandType.ThreeOfAKind + 1;

        private const float Epsilon = 0.0001f;

        /// <summary>把当前永久倍率写进 <paramref name="buffer"/>（长度用 <see cref="HandTypeCount"/>）。</summary>
        public static void Capture(RunState run, float[] buffer)
        {
            if (run == null || buffer == null)
            {
                return;
            }

            var count = buffer.Length < run.HandTypeMagForever.Length ? buffer.Length : run.HandTypeMagForever.Length;
            for (var i = 0; i < count; i++)
            {
                buffer[i] = run.HandTypeMagForever[i];
            }
        }

        /// <summary>对比快照与当前值，倒序（牌型大的在前）写入 <paramref name="dest"/>。</summary>
        public static void Diff(float[] before, RunState run, List<HandTypeMagRaise> dest)
        {
            if (dest == null)
            {
                return;
            }

            dest.Clear();
            if (before == null || run == null)
            {
                return;
            }

            for (var i = HandTypeCount - 1; i >= 0; i--)
            {
                if (i >= before.Length || i >= run.HandTypeMagForever.Length)
                {
                    continue;
                }

                var delta = run.HandTypeMagForever[i] - before[i];
                if (delta > Epsilon)
                {
                    dest.Add(new HandTypeMagRaise((HandType)i, delta));
                }
            }
        }

        /// <summary>
        /// 展示用倍率：牌型自身倍率（配置基础倍率 + 永久加成），与规则表 WinTip 同口径。
        /// 不要用 <c>GameSession.HandTypeMagnification</c>——那只含基础倍率，加成不在里面。
        /// </summary>
        public static float DisplayMagnification(RunState run, HandType type)
        {
            return HandEvaluator.TypeMultiplier(type) + (run != null ? run.HandTypeMagBonus(type) : 0f);
        }
    }
}
