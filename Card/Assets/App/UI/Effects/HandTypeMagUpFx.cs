using System.Collections.Generic;
using App.Atlas;
using App.Game;
using DG.Tweening;
using Framework.Log;
using Framework.UI.Binding;
using UnityEngine;
using UnityEngine.UI;

namespace App.UI
{
    /// <summary>
    /// 商店消耗品提升牌型永久倍率时的一次性提示：克隆 <c>cardinfoItem</c> 预制体，
    /// 复用它自带的表现（Animator 弹跳、UiFlowLight 扫光、PokerHand 粒子），
    /// 显示「牌型图标 + 提升后的倍率」，停留后淡出销毁。
    ///
    /// 预制体本身不绑任何业务数据，克隆后要做的只有：换图标、刷倍率、按 Level 开特效。
    /// 调用方负责提供父节点（要挂在不随详情页销毁的层，见 ShopDetailView.ResolveFxParent）
    /// 与使用前后的倍率差（见 <see cref="HandTypeMagDelta"/>）。
    /// </summary>
    public static class HandTypeMagUpFx
    {
        /// <summary>多行之间的竖向间距（父节点本地坐标）。</summary>
        private const float RowGap = 84f;

        /// <summary>整体缩放，比对局 HUD 上的稍小一点。</summary>
        private const float Scale = 0.9f;

        /// <summary>入场（Animator 弹跳）播完后的停留时长。</summary>
        private const float HoldSeconds = 0.9f;

        /// <summary>淡出时长。</summary>
        private const float FadeSeconds = 0.25f;

        /// <summary>
        /// 扫光频率。HUD 上默认 0.3Hz（3.3s 一轮）是为常驻面板调的，
        /// 这个提示只活 1 秒多，得调快才能在生命周期内扫完一遍。
        /// </summary>
        private const float FlowFrequency = 0.8f;

        /// <summary>
        /// 播放提示。<paramref name="raises"/> 一般 1 行；时光宝盒那种一次随机 3 种会排成 3 行。
        /// 倍率按「基础 + 永久加成」显示，与规则表同口径。
        /// </summary>
        public static void Play(
            RectTransform parent,
            GameObject prefab,
            RunState run,
            IReadOnlyList<HandTypeMagRaise> raises,
            IAtlasService atlas,
            Vector2 anchoredPosition)
        {
            if (parent == null || prefab == null || raises == null || raises.Count == 0)
            {
                return;
            }

            var startY = (raises.Count - 1) * RowGap * 0.5f;
            for (var i = 0; i < raises.Count; i++)
            {
                var position = anchoredPosition + new Vector2(0f, startY - i * RowGap);
                Spawn(parent, prefab, run, raises[i], atlas, position);
            }
        }

        private static void Spawn(
            RectTransform parent,
            GameObject prefab,
            RunState run,
            HandTypeMagRaise raise,
            IAtlasService atlas,
            Vector2 anchoredPosition)
        {
            // 外层 holder 负责定位与整体缩放：cardinfoItem 的 Animator 会直接写根的 localScale
            // （0 → 1.2/0.8 → 1），缩放只能加在它外面，否则每帧被动画覆盖。
            var holder = new GameObject("HandTypeMagUpFx", typeof(RectTransform), typeof(CanvasGroup));
            var holderRect = (RectTransform)holder.transform;
            holderRect.SetParent(parent, false);
            holderRect.anchorMin = new Vector2(0.5f, 0.5f);
            holderRect.anchorMax = new Vector2(0.5f, 0.5f);
            holderRect.pivot = new Vector2(0.5f, 0.5f);
            holderRect.sizeDelta = Vector2.zero;
            holderRect.anchoredPosition = anchoredPosition;
            holderRect.localScale = Vector3.one * Scale;

            var group = holder.GetComponent<CanvasGroup>();
            group.alpha = 1f;
            group.blocksRaycasts = false;
            group.interactable = false;

            var clone = Object.Instantiate(prefab, holderRect, false);
            clone.name = "cardinfoItem";
            // 先关掉再配置：避免弹出瞬间出现未换图的牌型，同时让 Animator 从第 0 帧开始播。
            clone.SetActive(false);

            var rect = (RectTransform)clone.transform;
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.localRotation = Quaternion.identity;

            StripBindings(clone);
            DisableRaycast(clone);
            DisableNumberAnimator(clone);
            ApplyIcon(clone, atlas, raise.Type);
            ApplyNumber(clone, atlas, run, raise.Type);
            CardInfoFx.Apply(clone, HandEvaluator.TypeLevel(raise.Type));
            ApplyFlowFrequency(clone);

            clone.SetActive(true);
            FadeOutLater(holder, group);
        }

        /// <summary>克隆体上的 UIBind 指向原预制体的键，留着会被本页的绑定误解析。</summary>
        private static void StripBindings(GameObject clone)
        {
            var binds = clone.GetComponentsInChildren<UIBind>(true);
            for (var i = 0; i < binds.Length; i++)
            {
                if (binds[i] != null)
                {
                    Object.Destroy(binds[i]);
                }
            }
        }

        /// <summary>纯表现物，不要挡商店点击。</summary>
        private static void DisableRaycast(GameObject clone)
        {
            var graphics = clone.GetComponentsInChildren<Graphic>(true);
            for (var i = 0; i < graphics.Length; i++)
            {
                if (graphics[i] != null)
                {
                    graphics[i].raycastTarget = false;
                }
            }
        }

        /// <summary>数字节点上的 NumberShack（默认状态是空 clip）在这里没用，关掉省一次动画求值。</summary>
        private static void DisableNumberAnimator(GameObject clone)
        {
            var num = CardInfoFx.FindChild(clone.transform, "cardtypeNum");
            if (num == null)
            {
                return;
            }

            var animator = num.GetComponent<Animator>();
            if (animator != null)
            {
                animator.enabled = false;
            }
        }

        private static void ApplyIcon(GameObject clone, IAtlasService atlas, HandType type)
        {
            var image = clone.GetComponent<Image>();
            if (image == null)
            {
                return;
            }

            image.sprite = CardTypeSpriteNames.GetIcon(atlas, type);
            image.enabled = image.sprite != null;
            image.preserveAspect = true;
            if (image.sprite == null)
            {
                AppLog.Warn(
                    LogChannel.Atlas,
                    $"HandTypeMagUpFx missing card type icon '{CardTypeSpriteNames.Icon(type)}'.");
            }
        }

        private static void ApplyNumber(GameObject clone, IAtlasService atlas, RunState run, HandType type)
        {
            var num = CardInfoFx.FindChild(clone.transform, "cardtypeNum");
            if (num == null)
            {
                return;
            }

            CardTypeValueSprites.Apply(
                atlas,
                num,
                GameTableViewModel.FormatMultiplier(HandTypeMagDelta.DisplayMagnification(run, type)));
        }

        private static void ApplyFlowFrequency(GameObject clone)
        {
            var flow = clone.GetComponent<UiFlowLight>();
            if (flow != null)
            {
                flow.SetFrequency(FlowFrequency);
            }
        }

        private static void FadeOutLater(GameObject holder, CanvasGroup group)
        {
            group.DOFade(0f, FadeSeconds)
                .SetDelay(HoldSeconds)
                .SetUpdate(true)
                .SetLink(holder, LinkBehaviour.KillOnDestroy)
                .OnComplete(() =>
                {
                    if (holder != null)
                    {
                        Object.Destroy(holder);
                    }
                });
        }
    }
}
