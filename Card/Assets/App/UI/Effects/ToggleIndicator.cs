using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

namespace App.UI
{
    /// <summary>
    /// 页签指示器：共享图片跟随选中的 Toggle 平滑移动到其正下方（如下划线）。
    /// 挂在指示器图片上，自动收集容器内全部 Toggle 并监听选中变化；
    /// 只对齐 X，Y 保持美术在编辑器摆的初始值；初次对齐瞬移，之后切换带缓动。
    /// 按钮常由 LayoutGroup 排布（首帧渲染前才 rebuild），故初始对齐延后一帧、
    /// 且无动画时每帧锚定兜底；指示器自身设 IgnoreLayout，防止被父级布局当子项排走。
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("UI/Toggle Indicator")]
    public sealed class ToggleIndicator : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("页签容器，留空取指示器父节点")]
        private RectTransform container;

        [SerializeField]
        [Tooltip("移动时长（秒），0 为瞬移")]
        private float duration = 0.25f;

        [SerializeField]
        [Tooltip("缓动曲线")]
        private Ease ease = Ease.OutCubic;

        private readonly List<Toggle> toggles = new List<Toggle>();
        private RectTransform rect;
        private Tween moveTween;
        private Toggle current;
        private bool positioned;

        private void Awake()
        {
            rect = (RectTransform)transform;
            EnsureIgnoreLayout();
            var root = container != null ? container : (RectTransform)transform.parent;
            root.GetComponentsInChildren(true, toggles);
            for (var i = 0; i < toggles.Count; i++)
            {
                var toggle = toggles[i];
                toggle.onValueChanged.AddListener(on =>
                {
                    if (on)
                    {
                        MoveTo(toggle);
                    }
                });
            }
        }

        private IEnumerator Start()
        {
            // 等 LayoutGroup 完成首帧排布再对齐；Awake 时按钮还在未排布位置，
            // 曾导致首次进入游戏指示器被钉在最左侧
            yield return null;
            for (var i = 0; i < toggles.Count; i++)
            {
                if (toggles[i].isOn)
                {
                    SnapTo(toggles[i]);
                    break;
                }
            }
        }

        private void LateUpdate()
        {
            // 静态锚定：无动画进行时持续对齐选中按钮中心，
            // 吸收布局 rebuild、分辨率/适配变化等一切时机问题
            if (current == null || (moveTween != null && moveTween.IsActive()))
            {
                return;
            }

            var local = rect.localPosition;
            var x = TargetLocalX(current);
            if (!Mathf.Approximately(local.x, x))
            {
                local.x = x;
                rect.localPosition = local;
            }
        }

        private void OnDestroy()
        {
            if (moveTween != null && moveTween.IsActive())
            {
                moveTween.Kill();
            }
        }

        private void EnsureIgnoreLayout()
        {
            var element = GetComponent<LayoutElement>();
            if (element == null)
            {
                element = gameObject.AddComponent<LayoutElement>();
            }

            element.ignoreLayout = true;
        }

        private void MoveTo(Toggle toggle)
        {
            if (!positioned || duration <= 0f)
            {
                SnapTo(toggle);
                return;
            }

            if (moveTween != null && moveTween.IsActive())
            {
                moveTween.Kill();
            }

            var local = rect.localPosition;
            moveTween = rect.DOLocalMove(new Vector3(TargetLocalX(toggle), local.y, local.z), duration)
                .SetEase(ease)
                .SetUpdate(true)
                .SetLink(gameObject, LinkBehaviour.KillOnDestroy)
                .SetTarget(this);
            current = toggle;
            positioned = true;
        }

        private void SnapTo(Toggle toggle)
        {
            var local = rect.localPosition;
            local.x = TargetLocalX(toggle);
            rect.localPosition = local;
            current = toggle;
            positioned = true;
        }

        /// <summary>目标按钮中心的世界坐标换到指示器父节点局部系取 X；同父级直位对齐，绕开 anchor 换算。</summary>
        private float TargetLocalX(Toggle toggle)
        {
            var parent = (RectTransform)rect.parent;
            return parent.InverseTransformPoint(toggle.transform.position).x;
        }
    }
}
