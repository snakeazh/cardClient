#if UNITY_EDITOR
using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace App.Ad
{
    /// <summary>
    /// 编辑器模拟广告面板：全屏白色底，中间「领取奖励」领奖并关闭，右上角「关闭」直接关闭不领奖。
    /// 面板被意外销毁（如停止播放）按未领奖处理，避免调用方任务悬挂。
    /// </summary>
    public sealed class EditorAdOverlay : MonoBehaviour
    {
        private Action _onCompleted;
        private Action _onClosedEarly;
        private bool _settled;

        public static void Show(Action onCompleted, Action onClosedEarly)
        {
            EnsureEventSystem();

            var canvasGo = new GameObject("EditorAdOverlay");
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 30000;
            canvasGo.AddComponent<GraphicRaycaster>();

            var overlay = canvasGo.AddComponent<EditorAdOverlay>();
            overlay._onCompleted = onCompleted;
            overlay._onClosedEarly = onClosedEarly;
            overlay.Build();
        }

        private void Build()
        {
            var background = CreateImage(transform, "Background", Color.white);
            Stretch(background.rectTransform);

            var title = CreateLabel(background.transform, "Title", "模拟广告（仅编辑器）", 40);
            var titleRect = title.rectTransform;
            titleRect.anchorMin = new Vector2(0.5f, 1f);
            titleRect.anchorMax = new Vector2(0.5f, 1f);
            titleRect.pivot = new Vector2(0.5f, 1f);
            titleRect.anchoredPosition = new Vector2(0f, -80f);
            titleRect.sizeDelta = new Vector2(720f, 80f);

            var claim = CreateButton(
                background.transform,
                "ClaimButton",
                "领取奖励",
                new Vector2(0.5f, 0.5f),
                Vector2.zero,
                new Vector2(360f, 120f));
            claim.onClick.AddListener(Claim);

            var close = CreateButton(
                background.transform,
                "CloseButton",
                "关闭",
                new Vector2(1f, 1f),
                new Vector2(-110f, -70f),
                new Vector2(160f, 90f));
            close.onClick.AddListener(CloseEarly);
        }

        private void Claim()
        {
            Settle(true);
            Destroy(gameObject);
        }

        private void CloseEarly()
        {
            Settle(false);
            Destroy(gameObject);
        }

        private void OnDestroy()
        {
            Settle(false);
        }

        private void Settle(bool completed)
        {
            if (_settled)
            {
                return;
            }

            _settled = true;
            var callback = completed ? _onCompleted : _onClosedEarly;
            _onCompleted = null;
            _onClosedEarly = null;
            callback?.Invoke();
        }

        private static void EnsureEventSystem()
        {
            if (EventSystem.current != null)
            {
                return;
            }

            var go = new GameObject("EventSystem");
            go.AddComponent<EventSystem>();
            go.AddComponent<StandaloneInputModule>();
        }

        private static Image CreateImage(Transform parent, string name, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var image = go.GetComponent<Image>();
            image.color = color;
            return image;
        }

        private static Button CreateButton(
            Transform parent,
            string name,
            string label,
            Vector2 anchor,
            Vector2 anchoredPos,
            Vector2 size)
        {
            var image = CreateImage(parent, name, new Color(0.9f, 0.9f, 0.9f));
            var rect = image.rectTransform;
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = anchoredPos;
            rect.sizeDelta = size;

            var button = image.gameObject.AddComponent<Button>();
            var text = CreateLabel(image.transform, "Label", label, 32);
            Stretch(text.rectTransform);
            return button;
        }

        private static Text CreateLabel(Transform parent, string name, string content, int fontSize)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var text = go.GetComponent<Text>();
            text.text = content;
            text.font = GetDefaultFont();
            text.fontSize = fontSize;
            text.color = Color.black;
            text.alignment = TextAnchor.MiddleCenter;
            text.raycastTarget = false;
            return text;
        }

        private static Font GetDefaultFont()
        {
            var font = UnityEngine.Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font == null)
            {
                font = UnityEngine.Resources.GetBuiltinResource<Font>("Arial.ttf");
            }

            return font;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }
    }
}
#endif
