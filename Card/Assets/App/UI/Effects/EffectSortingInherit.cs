using System.Collections.Generic;
using UnityEngine;

namespace App.UI
{
    /// <summary>
    /// 挂到特效根节点的层级继承组件：粒子/拖尾等 Renderer 不吃父物体的层级，
    /// 挂上后 sortingOrder 会在自身原值基础上叠加所有父物体的层级
    /// （父链上的 Canvas 取其 sortingOrder，其余取其 Renderer.sortingOrder），
    /// 挪到别的父节点下会自动重算，不会重复叠加。
    /// </summary>
    [DisallowMultipleComponent]
    public class EffectSortingInherit : MonoBehaviour
    {
        [Tooltip("在「自身原值 + 父物体层级」之上额外叠加的偏移，正数压过父物体，负数压到父物体下面")]
        [SerializeField] private int orderOffset;

        private readonly Dictionary<int, int> _baseOrders = new Dictionary<int, int>();

        private void OnEnable()
        {
            Apply();
        }

        private void OnTransformParentChanged()
        {
            Apply();
        }

        private void Apply()
        {
            var inherited = GetInheritedOrder(transform);
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

        /// <summary>
        /// 从直接父物体往上累计层级：父链上的 Canvas 只计根 Canvas 和勾了 Override Sorting 的
        /// 嵌套 Canvas（未 override 的嵌套 Canvas 自身排序无效），其余物体取 Renderer 的 sortingOrder。
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
                        order += canvas.sortingOrder;
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
