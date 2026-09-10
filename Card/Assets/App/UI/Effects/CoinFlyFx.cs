using System;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

namespace App.UI
{
    /// <summary>
    /// 在起点散落若干金币，停留后再飞向目标（通常是资源栏金币图标）。
    /// </summary>
    public static class CoinFlyFx
    {
        public const int DefaultCount = 8;
        public const float HoldDelay = 0.5f;
        private const float ScatterRadius = 90f;
        private const float ScatterDuration = 0.28f;
        private const float FlyDuration = 0.5f;
        private const float FlyStagger = 0.04f;
        private const float CoinScale = 0.55f;

        public static RectTransform FindGoldIcon()
        {
            var view = GameResourceView.FindOpen();
            return view != null ? GameResourceBarBinder.FindGoldIcon(view.transform) : null;
        }

        public static Sequence Play(
            GameObject prefab,
            RectTransform parent,
            Vector3 fromWorld,
            Vector3 toWorld,
            Action onArrived,
            int count = DefaultCount,
            Action onCoinArrived = null)
        {
            if (prefab == null || parent == null)
            {
                return null;
            }

            if (count < 1)
            {
                count = 1;
            }

            var fromLocal = WorldToLocal(parent, fromWorld);
            var toLocal = WorldToLocal(parent, toWorld);
            var spawned = new List<RectTransform>(count);
            var seq = DOTween.Sequence();
            seq.SetUpdate(true);

            for (var i = 0; i < count; i++)
            {
                var rt = Spawn(prefab, parent, fromLocal);
                if (rt == null)
                {
                    continue;
                }

                spawned.Add(rt);
                var scatter = fromLocal + UnityEngine.Random.insideUnitCircle * ScatterRadius;
                var angle = UnityEngine.Random.Range(-40f, 40f);
                var index = i;
                seq.Insert(0f, rt.DOAnchorPos(scatter, ScatterDuration).SetEase(Ease.OutCubic));
                seq.Insert(0f, rt.DOLocalRotate(new Vector3(0f, 0f, angle), ScatterDuration).SetEase(Ease.OutCubic));
                var flyAt = ScatterDuration + HoldDelay + index * FlyStagger;
                seq.Insert(flyAt, rt.DOAnchorPos(toLocal, FlyDuration)
                    .SetEase(Ease.InCubic)
                    .OnComplete(() => onCoinArrived?.Invoke()));
                seq.Insert(flyAt, rt.DOScale(CoinScale * 0.45f, FlyDuration).SetEase(Ease.InQuad));
            }

            if (spawned.Count == 0)
            {
                return null;
            }

            var finished = false;
            void Finish()
            {
                if (finished)
                {
                    return;
                }

                finished = true;
                DestroyAll(spawned);
                onArrived?.Invoke();
            }

            seq.OnComplete(Finish);
            seq.OnKill(Finish);
            return seq;
        }

        /// <summary>飞币落到金币图标时逐枚释放暂扣，资源栏数字跟着涨。</summary>
        public static Sequence PlayAndCredit(
            GameObject prefab,
            RectTransform parent,
            Vector3 fromWorld,
            Vector3 toWorld,
            GameResourceViewModel bar,
            int gold,
            int count = DefaultCount)
        {
            if (gold <= 0 || bar == null)
            {
                return Play(prefab, parent, fromWorld, toWorld, null, count);
            }

            var left = gold;
            var pendingCoins = count;
            return Play(
                prefab,
                parent,
                fromWorld,
                toWorld,
                () =>
                {
                    if (left <= 0)
                    {
                        return;
                    }

                    bar.ReleaseHeldGold(left);
                    left = 0;
                },
                count,
                () =>
                {
                    if (left <= 0)
                    {
                        return;
                    }

                    var chunk = pendingCoins <= 1 ? left : left / pendingCoins;
                    pendingCoins--;
                    if (chunk <= 0)
                    {
                        return;
                    }

                    left -= chunk;
                    bar.ReleaseHeldGold(chunk);
                });
        }

        private static RectTransform Spawn(GameObject prefab, RectTransform parent, Vector2 localPos)
        {
            var go = UnityEngine.Object.Instantiate(prefab, parent, false);
            go.name = "coinitem_fx";
            var graphics = go.GetComponentsInChildren<Graphic>(true);
            for (var i = 0; i < graphics.Length; i++)
            {
                graphics[i].raycastTarget = false;
            }

            var rt = go.transform as RectTransform;
            if (rt == null)
            {
                UnityEngine.Object.Destroy(go);
                return null;
            }

            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = localPos;
            rt.localRotation = Quaternion.identity;
            rt.localScale = Vector3.one * CoinScale;
            rt.SetAsLastSibling();
            go.SetActive(true);
            return rt;
        }

        private static Vector2 WorldToLocal(RectTransform parent, Vector3 world)
        {
            var canvas = parent.GetComponentInParent<Canvas>();
            var cam = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? canvas.worldCamera
                : null;
            var screen = RectTransformUtility.WorldToScreenPoint(cam, world);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, screen, cam, out var local);
            return local;
        }

        private static void DestroyAll(List<RectTransform> spawned)
        {
            if (spawned == null)
            {
                return;
            }

            for (var i = 0; i < spawned.Count; i++)
            {
                if (spawned[i] != null)
                {
                    UnityEngine.Object.Destroy(spawned[i].gameObject);
                }
            }

            spawned.Clear();
        }
    }
}
