using System;
using App.Item;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

namespace App.UI
{
    /// <summary>
    /// 克隆一张 UI 卡，从起点飞到目标格子并缩放到目标尺寸。
    /// </summary>
    public static class ItemFlyFx
    {
        public const float FlyDuration = 0.45f;

        public static Sequence Play(
            RectTransform source,
            RectTransform parent,
            RectTransform destination,
            Action onArrived)
        {
            if (source == null || parent == null || destination == null)
            {
                return null;
            }

            var go = UnityEngine.Object.Instantiate(source.gameObject, parent, false);
            go.name = "item_fly_fx";
            DisableInteraction(go);
            SyncQuality(source, go);

            var rt = go.transform as RectTransform;
            if (rt == null)
            {
                UnityEngine.Object.Destroy(go);
                return null;
            }

            var fromLocal = WorldToLocal(parent, WorldCenter(source));
            var toLocal = WorldToLocal(parent, WorldCenter(destination));
            var startScale = WorldScaleToLocal(parent, source.lossyScale);
            var endScale = WorldScaleToLocal(parent, destination.lossyScale);

            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = source.rect.size;
            rt.anchoredPosition = fromLocal;
            rt.localRotation = Quaternion.identity;
            rt.localScale = startScale;
            rt.SetAsLastSibling();
            go.SetActive(true);

            var seq = DOTween.Sequence();
            seq.SetUpdate(true);
            seq.Join(rt.DOAnchorPos(toLocal, FlyDuration).SetEase(Ease.InCubic));
            seq.Join(rt.DOScale(endScale, FlyDuration).SetEase(Ease.InCubic));
            seq.OnComplete(() =>
            {
                DestroyFx(go);
                onArrived?.Invoke();
            });
            seq.OnKill(() => DestroyFx(go));
            return seq;
        }

        private static void SyncQuality(RectTransform source, GameObject clone)
        {
            var src = source.GetComponent<ItemCard>() ?? source.GetComponentInChildren<ItemCard>(true);
            var dst = clone.GetComponent<ItemCard>() ?? clone.GetComponentInChildren<ItemCard>(true);
            if (src == null || dst == null)
            {
                return;
            }

            dst.SetShadowVisible(false);
            dst.SetAnimationEnabled(false);
            dst.SetUnlocked(true);
            dst.ApplyQuality(src.Quality);
        }

        private static void DisableInteraction(GameObject go)
        {
            var graphics = go.GetComponentsInChildren<Graphic>(true);
            for (var i = 0; i < graphics.Length; i++)
            {
                graphics[i].raycastTarget = false;
            }

            var buttons = go.GetComponentsInChildren<Button>(true);
            for (var i = 0; i < buttons.Length; i++)
            {
                buttons[i].enabled = false;
            }

            var animators = go.GetComponentsInChildren<Animator>(true);
            for (var i = 0; i < animators.Length; i++)
            {
                animators[i].enabled = false;
            }
        }

        private static Vector3 WorldCenter(RectTransform rt)
        {
            var corners = new Vector3[4];
            rt.GetWorldCorners(corners);
            return (corners[0] + corners[2]) * 0.5f;
        }

        private static Vector3 WorldScaleToLocal(RectTransform parent, Vector3 worldScale)
        {
            var parentScale = parent.lossyScale;
            return new Vector3(
                parentScale.x != 0f ? worldScale.x / parentScale.x : worldScale.x,
                parentScale.y != 0f ? worldScale.y / parentScale.y : worldScale.y,
                parentScale.z != 0f ? worldScale.z / parentScale.z : worldScale.z);
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

        private static void DestroyFx(GameObject go)
        {
            if (go != null)
            {
                UnityEngine.Object.Destroy(go);
            }
        }
    }
}
