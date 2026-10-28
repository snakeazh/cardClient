using System.Collections.Generic;
using UnityEngine;

namespace App.UI
{
    /// <summary>
    /// 挂到特效根节点的层级继承组件：粒子/拖尾等 Renderer 不吃父物体的层级，
    /// 挂上后 sortingOrder 会在自身原值基础上叠加父物体的层级
    /// （父链上**最近的**根/Override Canvas 取其 sortingOrder，其下方的 Renderer 逐级累加），
    /// 子物体里勾了 Override Sorting 的 Canvas 同样平移；挪到别的父节点下会自动重算，不会重复叠加。
    /// </summary>
    [DisallowMultipleComponent]
    public class EffectSortingInherit : MonoBehaviour
    {
        [Tooltip("在「自身原值 + 父物体层级」之上额外叠加的偏移，正数压过父物体，负数压到父物体下面")]
        [SerializeField] private int orderOffset;

        /// <summary>运行时 AddComponent 后用该属性调偏移；赋值即按新偏移重算全部子 Renderer。</summary>
        public int OrderOffset
        {
            get => orderOffset;
            set
            {
                orderOffset = value;
                Apply();
            }
        }

        private readonly Dictionary<int, int> _baseOrders = new Dictionary<int, int>();

        private void OnEnable()
        {
            Apply();
        }

        private void OnTransformParentChanged()
        {
            Apply();
        }

        /// <summary>换父 / 改偏移后手动重算（生成路径在 SetActive 之后再调一次保底）。</summary>
        public void Refresh()
        {
            Apply();
        }

        private void Apply()
        {
            var inherited = GetInheritedOrder(transform);
            ApplyRenderers(inherited);
            ApplyCanvases(inherited);
        }

        private void ApplyRenderers(int inherited)
        {
            var renderers = GetComponentsInChildren<Renderer>(true);
            for (var i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i];
                if (renderer == null)
                {
                    continue;
                }

                // 记住每个 Renderer 的预制体原值，重算/换父时基于原值平移，避免越叠越多
                var id = renderer.GetInstanceID();
                if (!_baseOrders.TryGetValue(id, out var baseOrder))
                {
                    baseOrder = renderer.sortingOrder;
                    _baseOrders[id] = baseOrder;
                }

                renderer.sortingOrder = baseOrder + inherited + orderOffset;
            }
        }

        private void ApplyCanvases(int inherited)
        {
            var canvases = GetComponentsInChildren<Canvas>(true);
            for (var i = 0; i < canvases.Length; i++)
            {
                var canvas = canvases[i];
                if (canvas == null)
                {
                    continue;
                }

                // 根 Canvas 是整个 UI 的层总闸不能动；未 override 的嵌套 Canvas 排序跟随父级，改字段不生效
                if (canvas.isRootCanvas || !canvas.overrideSorting)
                {
                    continue;
                }

                var id = canvas.GetInstanceID();
                if (!_baseOrders.TryGetValue(id, out var baseOrder))
                {
                    baseOrder = canvas.sortingOrder;
                    _baseOrders[id] = baseOrder;
                }

                canvas.sortingOrder = baseOrder + inherited + orderOffset;
            }
        }

        /// <summary>
        /// 从直接父物体往上累计层级：遇到第一个「根 Canvas 或勾了 Override Sorting 的嵌套 Canvas」就停，
        /// 取它的 sortingOrder —— 嵌套 Canvas 是**就近覆盖**的排序组，祖先 Canvas 的 order 不会叠加
        /// （之前是逐级相加，UI 层 200 + 中间 Canvas 300 会被算成 500，特效直接飞到弹窗/引导之上）。
        /// 该 Canvas 下方的 Renderer 层级仍然逐级累加（特效挂 SpriteRenderer 等非 Canvas 父物体时用）。
        /// </summary>
        private static int GetInheritedOrder(Transform self)
        {
            var order = 0;
            var t = self.parent;
            while (t != null)
            {
                var canvas = t.GetComponent<Canvas>();
                if (canvas != null)
                {
                    if (canvas.isRootCanvas || canvas.overrideSorting)
                    {
                        return order + canvas.sortingOrder;
                    }
                }
                else
                {
                    var renderer = t.GetComponent<Renderer>();
                    if (renderer != null)
                    {
                        order += renderer.sortingOrder;
                    }
                }

                t = t.parent;
            }

            return order;
        }
    }
}
