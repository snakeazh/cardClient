using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace App.UI
{
    /// <summary>
    /// 选难度列表项，挂在预制体 <c>Res/UI/Icon/IevelItem</c> 根节点上（注意文件名首字母是大写 I）。
    /// 结构：Item(Button) / Shadow / ItemRoot(Bg + LevelText + Mask + DifficultTip)。
    /// 序列化字段优先、按路径懒查找兜底；选中态 = ItemRoot 上移约四分之一个卡高，撤销选中回退。
    /// </summary>
    public sealed class LevelItemCard : MonoBehaviour
    {
        private const float LiftDuration = 0.18f;
        private const float LiftHeightRatio = 0.125f;
        private const float CardArtHeight = 173f;

        [SerializeField] private TMP_Text levelText;
        [SerializeField] private GameObject mask;
        [SerializeField] private RectTransform itemRoot;

        private Button _button;
        private Coroutine _liftRoutine;
        private bool _selected;
        private bool _liftOn;

        /// <summary>根节点 Button 点击转发；预制体 OnClick 列表为空，监听在这里挂（同 ItemCard 模式）。</summary>
        public event System.Action<LevelItemCard> Clicked;

        public bool IsSelected => _selected;

        private float LiftHeight
        {
            get
            {
                EnsureRefs();
                // 网格 layout 当帧未算完时 rect.height 为 0，退回卡面美术高度，保证兜底值同样按比例缩放
                return (itemRoot != null && itemRoot.rect.height > 0f
                    ? itemRoot.rect.height
                    : CardArtHeight) * LiftHeightRatio;
            }
        }

        private void Awake()
        {
            EnsureRefs();
            if (_button != null)
            {
                _button.onClick.AddListener(OnClicked);
            }
        }

        private void OnDestroy()
        {
            if (_button != null)
            {
                _button.onClick.RemoveListener(OnClicked);
            }
        }

        private void OnDisable()
        {
            // 抬卡协程在节点隐藏时会中断，吸附到目标态，避免残留半程位移
            if (_liftOn)
            {
                SetLiftY(LiftHeight);
            }
        }

        /// <summary>难度数字，仅阿拉伯数字（「难度」文案是 DifficultTip 固定内容）。</summary>
        public void SetDifficulty(int difficulty)
        {
            EnsureRefs();
            if (levelText != null)
            {
                levelText.text = difficulty.ToString();
            }
        }

        /// <summary>未解锁显示 Mask 遮罩，解锁隐藏。</summary>
        public void SetUnlocked(bool unlocked)
        {
            EnsureRefs();
            if (mask != null)
            {
                mask.SetActive(!unlocked);
            }
        }

        /// <summary>
        /// 选中抬卡：ItemRoot 上移 LiftHeightRatio × 卡高，撤销选中回退。replay 为 true 时同状态也重播（面板重新激活用）。
        /// </summary>
        public void SetSelected(bool selected, bool replay = false)
        {
            _selected = selected;
            if (!replay && _liftOn == selected)
            {
                return;
            }

            _liftOn = selected;
            StopLiftRoutine();
            if (!isActiveAndEnabled)
            {
                SetLiftY(selected ? LiftHeight : 0f);
                return;
            }

            _liftRoutine = StartCoroutine(LiftRoutine(selected ? LiftHeight : 0f));
        }

        private IEnumerator LiftRoutine(float targetY)
        {
            var elapsed = 0f;
            var startY = itemRoot.anchoredPosition.y;
            while (elapsed < LiftDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                var t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / LiftDuration));
                SetLiftY(Mathf.Lerp(startY, targetY, t));
                yield return null;
            }

            SetLiftY(targetY);
            _liftRoutine = null;
        }

        private void StopLiftRoutine()
        {
            if (_liftRoutine != null)
            {
                StopCoroutine(_liftRoutine);
                _liftRoutine = null;
            }
        }

        private void SetLiftY(float y)
        {
            EnsureRefs();
            if (itemRoot == null)
            {
                return;
            }

            var pos = itemRoot.anchoredPosition;
            pos.y = y;
            itemRoot.anchoredPosition = pos;
        }

        private void OnClicked()
        {
            Clicked?.Invoke(this);
        }

        private void EnsureRefs()
        {
            // 节点都在固定路径上，直接按路径查找
            if (levelText == null)
            {
                var node = transform.Find("ItemRoot/LevelText");
                levelText = node != null ? node.GetComponent<TMP_Text>() : null;
            }

            if (mask == null)
            {
                var node = transform.Find("ItemRoot/Mask");
                mask = node != null ? node.gameObject : null;
            }

            if (itemRoot == null)
            {
                itemRoot = transform.Find("ItemRoot") as RectTransform;
            }

            if (_button == null)
            {
                _button = GetComponent<Button>();
            }
        }
    }
}
