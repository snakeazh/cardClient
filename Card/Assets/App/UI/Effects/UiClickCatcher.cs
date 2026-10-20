using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace App.UI
{
    /// <summary>
    /// 全屏透明点击捕获层：入场动画期间放在宿主最上层，点击任意处触发 <see cref="Clicked"/>。
    /// 只负责吃点击，不做任何视觉；由宿主在动画开始/结束时 SetActive。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class UiClickCatcher : MonoBehaviour, IPointerClickHandler
    {
        public event Action Clicked;

        public void OnPointerClick(PointerEventData eventData)
        {
            Clicked?.Invoke();
        }

        /// <summary>在 parent（须全屏拉伸）下创建并返回一个默认关闭的捕获层。</summary>
        public static UiClickCatcher Create(RectTransform parent, Action onClick)
        {
            if (parent == null)
            {
                return null;
            }

            var go = new GameObject(
                "ClickToSkip",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(UiClickCatcher));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.SetAsLastSibling();
            go.layer = parent.gameObject.layer;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.localScale = Vector3.one;

            var image = go.GetComponent<Image>();
            image.color = new Color(0f, 0f, 0f, 0f);
            image.raycastTarget = true;

            var catcher = go.GetComponent<UiClickCatcher>();
            if (onClick != null)
            {
                catcher.Clicked += onClick;
            }

            go.SetActive(false);
            return catcher;
        }
    }
}
