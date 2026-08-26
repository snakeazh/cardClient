using System.Threading.Tasks;
using App.Bootstrap;
using App.Resources;
using DG.Tweening;
using Framework.Log;
using Framework.UI.Navigation;
using Framework.UI.View;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace App.UI
{
    /// <summary>
    /// Toast 提示面板。注册在 TopMost 层：首次调用 Toast.Show 时由 ToastService 懒打开，
    /// 之后常驻；同时只显示一个 Toast，新的会立即替换旧的。
    /// ToastPrefab 引用外部 ToastItem 预制体（可后续从旧工程补齐）；为空时运行时构建条目。
    /// </summary>
    [AutoScreen(AppScreenIds.ToastPanel, UILayer.TopMost, ResResourcePaths.ToastPanel)]
    public sealed class ToastPanel : ViewBase<ToastViewModel>
    {
        #region UI组件
        [SerializeField]
        private GameObject ToastPrefab;
        [SerializeField]
        private Transform ToastContainer;
        #endregion

        #region 私有字段
        private GameObject mCurrentToast;
        private Sequence mCurrentSequence;
        private ToastService mToastService;
        private Image mToastBackground;
        private TextMeshProUGUI mToastText;
        private Color mBackgroundColor = new Color(0f, 0f, 0f, 0.78f);
        private const float ToastMoveInY = -198f;
        private const float ToastMoveOutY = 112f;
        private const float ToastAnimationDuration = 0.5f;
        #endregion

        protected override void OnBind()
        {
            // 确保容器存在
            if (ToastContainer == null)
            {
                ToastContainer = transform;
            }

            // 获取ToastService
            mToastService = AppServices.IsReady ? AppServices.Resolve<ToastService>() : null;
            if (mToastService == null)
            {
                AppLog.Error(LogChannel.UI, "[ToastPanel] 找不到ToastService！");
                return;
            }

            // 订阅Service事件
            mToastService.OnShowToast += OnShowToast;
            mToastService.OnToastClosed += OnToastClosed;
        }

        protected override Task OnViewClose()
        {
            // 取消订阅
            if (mToastService != null)
            {
                mToastService.OnShowToast -= OnShowToast;
                mToastService.OnToastClosed -= OnToastClosed;
                mToastService = null;
            }

            ClearCurrentToast();
            return Task.CompletedTask;
        }

        #region Toast显示逻辑

        private void OnShowToast(string message, float duration, ToastType type)
        {
            // 常驻面板可能被后开的 TopMost 屏压住，置顶保证 Toast 永远最上层
            transform.SetAsLastSibling();

            // 清理旧的Toast（如果有的话），再创建新的Toast
            ClearCurrentToast();
            CreateToast(message, type);

            // 播放动画
            PlayToastAnimation(duration);
        }

        private void OnToastClosed()
        {
            // 清理当前Toast
            ClearCurrentToast();
        }

        private void CreateToast(string message, ToastType type)
        {
            mCurrentToast = ToastPrefab != null
                ? Instantiate(ToastPrefab, ToastContainer)
                : BuildToastItem();
            mCurrentToast.name = "ToastItem";

            var rectTransform = mCurrentToast.GetComponent<RectTransform>();
            if (rectTransform == null)
            {
                AppLog.Error(LogChannel.UI, "[ToastPanel] ToastItem缺少RectTransform！");
                ClearCurrentToast();
                return;
            }

            // 兼容外部预制体的任意层级：按组件类型收集引用
            mToastBackground = mCurrentToast.GetComponentInChildren<Image>(true);
            mToastText = mCurrentToast.GetComponentInChildren<TextMeshProUGUI>(true);
            if (mToastBackground != null)
            {
                // 预制体自带的颜色作为底色，运行时构建的用默认深色
                mBackgroundColor = mToastBackground.color;
                mToastBackground.raycastTarget = false;
            }

            if (mToastText != null)
            {
                mToastText.text = message;
                mToastText.raycastTarget = false;
            }
            else
            {
                AppLog.Error(LogChannel.UI, "[ToastPanel] ToastItem缺少文本组件！");
            }
        }

        /// <summary>
        /// 运行时构建 Toast 条目：背景 + 居中文本
        /// </summary>
        private GameObject BuildToastItem()
        {
            var root = new GameObject("ToastItem", typeof(RectTransform));
            var rect = (RectTransform)root.transform;
            rect.SetParent(ToastContainer, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(560f, 72f);
            rect.anchoredPosition = new Vector2(0f, ToastMoveInY);

            var background = root.AddComponent<Image>();
            background.color = mBackgroundColor;
            background.raycastTarget = false;

            var textGo = new GameObject("TipTxt", typeof(RectTransform));
            var textRect = (RectTransform)textGo.transform;
            textRect.SetParent(root.transform, false);
            textRect.anchorMin = textRect.anchorMax = Vector2.zero;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;
            var text = textGo.AddComponent<TextMeshProUGUI>();
            text.fontSize = 30f;
            text.alignment = TextAlignmentOptions.Center;
            text.color = Color.white;
            text.raycastTarget = false;

            return root;
        }

        /// <summary>
        /// 播放Toast动画：入场（背景淡入+上滑）→ 停留 → 出场（整体淡出+上滑）
        /// </summary>
        private void PlayToastAnimation(float duration)
        {
            if (mCurrentToast == null)
            {
                return;
            }

            var rectTransform = mCurrentToast.GetComponent<RectTransform>();
            if (rectTransform == null)
            {
                ClearCurrentToast();
                return;
            }

            var baseColor = mBackgroundColor;
            if (mToastBackground != null)
            {
                mToastBackground.color = new Color(baseColor.r, baseColor.g, baseColor.b, 0f);
            }

            if (mToastText != null)
            {
                var textColor = mToastText.color;
                mToastText.color = new Color(textColor.r, textColor.g, textColor.b, 1f);
            }

            rectTransform.anchoredPosition = new Vector2(0f, ToastMoveInY);

            mCurrentSequence = DOTween.Sequence().SetUpdate(UpdateType.Normal, true);

            // 入场
            mCurrentSequence.Append(mToastBackground != null
                ? mToastBackground.DOFade(baseColor.a, ToastAnimationDuration)
                : rectTransform.DOAnchorPos(Vector2.zero, ToastAnimationDuration));
            if (mToastBackground != null)
            {
                mCurrentSequence.Join(rectTransform.DOAnchorPos(Vector2.zero, ToastAnimationDuration));
            }

            // 停留
            mCurrentSequence.Append(DOVirtual.Float(0f, 1f, duration, _ => { })
                .SetUpdate(UpdateType.Normal, true));

            // 出场
            mCurrentSequence.Append(rectTransform.DOAnchorPos(new Vector2(0f, ToastMoveOutY), ToastAnimationDuration));
            if (mToastBackground != null)
            {
                mCurrentSequence.Join(mToastBackground.DOFade(0f, ToastAnimationDuration));
            }
            if (mToastText != null)
            {
                mCurrentSequence.Join(mToastText.DOFade(0f, ToastAnimationDuration));
            }

            mCurrentSequence.OnComplete(ClearCurrentToast);
        }

        private void ClearCurrentToast()
        {
            // 停止动画
            if (mCurrentSequence != null)
            {
                mCurrentSequence.Kill();
                mCurrentSequence = null;
            }

            // 销毁Toast对象
            if (mCurrentToast != null)
            {
                Destroy(mCurrentToast);
                mCurrentToast = null;
                mToastBackground = null;
                mToastText = null;
            }
        }

        #endregion
    }
}
