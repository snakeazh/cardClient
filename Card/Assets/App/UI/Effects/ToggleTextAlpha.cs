using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace App.UI
{
    /// <summary>
    /// Toggle 扩展：按开关状态调整子节点文本透明度——选中不透明，未选中半透明。
    /// 默认按名取子节点 Name 的 TMP_Text，也可序列化指定；主界面底栏按钮文字用。
    /// 直接挂在带 Toggle 的节点上即可生效，不挂此组件的 Toggle 行为不变，
    /// 与 ToggleStateObjects 可同时使用（一个切节点、一个调文字透明度）。
    /// </summary>
    [RequireComponent(typeof(Toggle))]
    [DisallowMultipleComponent]
    public sealed class ToggleTextAlpha : MonoBehaviour
    {
        private const string DefaultLabelName = "Name";

        #region UI组件
        [SerializeField]
        [Range(0f, 1f)]
        [Tooltip("未选中时文字透明度")]
        private float UnselectedAlpha = 0.5f;

        [SerializeField]
        [Tooltip("要调透明度的 TMP 文本；留空按名找 Name 子节点")]
        private TMP_Text Label;
        #endregion

        private Toggle mToggle;
        private Color mColor;

        private void Awake()
        {
            mToggle = GetComponent<Toggle>();
            EnsureLabel();
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

        /// <summary>运行时替换目标文本（如代码动态搭建的 UI），并立即按当前状态刷新。</summary>
        public void SetLabel(TMP_Text label)
        {
            Label = label;
            if (Label != null)
            {
                mColor = Label.color;
            }

            Apply(mToggle != null && mToggle.isOn);
        }

        private void EnsureLabel()
        {
            if (Label == null)
            {
                var node = transform.Find(DefaultLabelName);
                Label = node != null ? node.GetComponent<TMP_Text>() : null;
            }

            if (Label != null)
            {
                mColor = Label.color;
            }
        }

        private void OnToggleValueChanged(bool value)
        {
            Apply(value);
        }

        private void Apply(bool value)
        {
            if (Label == null)
            {
                return;
            }

            mColor.a = value ? 1f : UnselectedAlpha;
            Label.color = mColor;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            // 编辑器下拖引用或改 Toggle.isOn 时即时预览透明度效果
            var toggle = GetComponent<Toggle>();
            if (toggle != null)
            {
                EnsureLabel();
                Apply(toggle.isOn);
            }
        }
#endif
    }
}
