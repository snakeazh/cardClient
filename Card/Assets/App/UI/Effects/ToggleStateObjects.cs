using UnityEngine;
using UnityEngine.UI;

namespace App.UI
{
    /// <summary>
    /// Toggle 扩展：按开关状态切换两份节点的激活显示。
    /// Toggle 打开时激活 IsOn、关闭 IsOff；关闭时相反。
    /// 直接挂在带 Toggle 的节点上即可生效，两个字段留空则该侧不做处理；
    /// 不挂此组件的 Toggle 行为不变，与现有 UI.Get&lt;Toggle&gt; / Binding.BindToggle 完全兼容。
    /// </summary>
    [RequireComponent(typeof(Toggle))]
    [DisallowMultipleComponent]
    public sealed class ToggleStateObjects : MonoBehaviour
    {
        #region UI组件
        [SerializeField]
        [Tooltip("Toggle 打开时激活的节点")]
        private GameObject IsOn;

        [SerializeField]
        [Tooltip("Toggle 关闭时激活的节点")]
        private GameObject IsOff;
        #endregion

        private Toggle mToggle;

        private void Awake()
        {
            mToggle = GetComponent<Toggle>();
        }

        private void OnEnable()
        {
            mToggle.onValueChanged.AddListener(OnToggleValueChanged);
            Apply(mToggle.isOn);
        }

        private void OnDisable()
        {
            if (mToggle != null)
            {
                mToggle.onValueChanged.RemoveListener(OnToggleValueChanged);
            }
        }

        /// <summary>运行时替换目标节点（如代码动态搭建的 UI），并立即按当前状态刷新。</summary>
        public void SetTargets(GameObject isOn, GameObject isOff)
        {
            IsOn = isOn;
            IsOff = isOff;
            Apply(mToggle != null && mToggle.isOn);
        }

        private void OnToggleValueChanged(bool value)
        {
            Apply(value);
        }

        private void Apply(bool value)
        {
            SetActive(IsOn, value);
            SetActive(IsOff, !value);
        }

        private static void SetActive(GameObject target, bool active)
        {
            if (target != null && target.activeSelf != active)
            {
                target.SetActive(active);
            }
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            // 编辑器下拖引用或改 Toggle.isOn 时即时预览切换效果
            var toggle = GetComponent<Toggle>();
            if (toggle != null)
            {
                Apply(toggle.isOn);
            }
        }
#endif
    }
}
