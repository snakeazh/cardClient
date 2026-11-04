using UnityEngine;

namespace App.UI
{
    /// <summary>
    /// 牌型图标（cardinfoItem）的特效开关：按 HandScoreConfig.Level 决定扫光与粒子。
    /// 对局 HUD 与商店「牌型倍率提升」弹窗共用同一套规则，避免两处判定走偏。
    /// </summary>
    public static class CardInfoFx
    {
        /// <summary>顺子及以上（Level &gt; 3）扫光。</summary>
        public const int FlowLightMinLevel = 4;

        /// <summary>顺金。</summary>
        public const int StraightFlushLevel = 5;

        /// <summary>豹子。</summary>
        public const int BombLevel = 6;

        public const string StraightFlushNode = "PokerHandStraightFlush01";
        public const string BombNode = "PokerHandBomb01";

        /// <summary>
        /// 按牌型顺位开/关特效。<paramref name="level"/> 是配置 <c>HandScoreConfig.Level</c>（1~6）。
        /// </summary>
        public static void Apply(GameObject root, int level)
        {
            if (root == null)
            {
                return;
            }

            var flow = root.GetComponent<UiFlowLight>();
            if (flow != null)
            {
                if (level >= FlowLightMinLevel)
                {
                    flow.Play();
                }
                else
                {
                    flow.Stop();
                }
            }

            SetChildActive(root.transform, StraightFlushNode, level == StraightFlushLevel);
            SetChildActive(root.transform, BombNode, level == BombLevel);
        }

        /// <summary>按名字开/关子节点（先直接子节点，找不到再全树找）。</summary>
        public static void SetChildActive(Transform root, string name, bool active)
        {
            var child = FindChild(root, name);
            if (child != null)
            {
                child.gameObject.SetActive(active);
            }
        }

        /// <summary>先 <c>Find</c> 直接子节点，再退化成全树查找。</summary>
        public static Transform FindChild(Transform root, string name)
        {
            if (root == null || string.IsNullOrEmpty(name))
            {
                return null;
            }

            var child = root.Find(name);
            return child != null ? child : FindDeep(root, name);
        }

        private static Transform FindDeep(Transform root, string name)
        {
            for (var i = 0; i < root.childCount; i++)
            {
                var child = root.GetChild(i);
                if (child == null)
                {
                    continue;
                }

                if (child.name == name)
                {
                    return child;
                }

                var found = FindDeep(child, name);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }
    }
}
