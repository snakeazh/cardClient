using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;

namespace App.UI.Effects
{
    /// <summary>
    /// 列表卡槽的交错入场（staggered entrance）：对一组卡槽逐张延迟弹入
    /// （CanvasGroup alpha 0→1 + 0.85×静止缩放回弹，OutBack）。
    /// 只写 alpha 与 localScale，不碰布局属性；虚拟化列表请在整页填充完成后
    /// 对视口内已生成的卡槽播一次，滚动时新生成的行不要播。
    /// 卡槽是池化对象（UiCardPool），租用前须调 <see cref="ResetSlot"/> 清掉上一任的动画残留。
    /// </summary>
    public static class StaggerReveal
    {
        /// <summary>相邻两张卡的启动间隔（秒）。</summary>
        public const float Stagger = 0.025f;

        /// <summary>参与错峰的最大张数，之后的卡与最后一张同拍入场。</summary>
        public const int StaggerCap = 12;

        /// <summary>单张卡的弹入时长（秒）。</summary>
        public const float Duration = 0.18f;

        /// <summary>
        /// 播交错入场。slots 顺序即出场顺序（行收集顺序=从上到下）。
        /// delayBase 用于给弹窗自身的根入场（PopupViewTransition 0.25s 缩放）让路。
        /// </summary>
        public static void Play(IReadOnlyList<Component> slots, float delayBase = 0f)
        {
            if (slots == null)
            {
                return;
            }

            for (var i = 0; i < slots.Count; i++)
            {
                var rt = slots[i] != null ? slots[i].transform as RectTransform : null;
                if (rt == null)
                {
                    continue;
                }

                var rest = rt.localScale;
                var group = rt.GetComponent<CanvasGroup>();
                if (group == null)
                {
                    group = rt.gameObject.AddComponent<CanvasGroup>();
                }

                var delay = delayBase + Mathf.Min(i, StaggerCap) * Stagger;
                rt.DOKill();
                group.DOKill();
                group.alpha = 0f;
                rt.localScale = rest * 0.85f;
                group.DOFade(1f, Duration)
                    .SetDelay(delay)
                    .SetEase(Ease.OutQuad)
                    .SetUpdate(true)
                    .SetLink(rt.gameObject, LinkBehaviour.KillOnDestroy);
                rt.DOScale(rest, Duration)
                    .SetDelay(delay)
                    .SetEase(Ease.OutBack)
                    .SetUpdate(true)
                    .SetLink(rt.gameObject, LinkBehaviour.KillOnDestroy);
            }
        }

        /// <summary>
        /// 池化卡槽租用时的复位：清掉残留 tween 并把 alpha 补回 1
        /// （上一任视图若在动画中途关闭，alpha 会遗留 &lt;1）。缩放由租用方自行设回。
        /// </summary>
        public static void ResetSlot(Component slot)
        {
            var rt = slot != null ? slot.transform as RectTransform : null;
            if (rt == null)
            {
                return;
            }

            rt.DOKill();
            var group = rt.GetComponent<CanvasGroup>();
            if (group != null)
            {
                group.DOKill();
                group.alpha = 1f;
            }
        }
    }
}
