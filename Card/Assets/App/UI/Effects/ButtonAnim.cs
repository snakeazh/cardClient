using App.Audio;
using App.Bootstrap;
using DG.Tweening;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace App.UI
{
    /// <summary>
    /// 按钮点下：指定物体下移 + 整体缩小，松开弹回；可选点下音效。
    /// 不抢 Button.onClick，与 BindCommand 兼容。
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("UI/Button Anim")]
    public sealed class ButtonAnim : MonoBehaviour,
        IPointerDownHandler,
        IPointerUpHandler,
        IPointerExitHandler
    {
        [SerializeField]
        [Tooltip("向下移动的物体，留空则移动自身")]
        private Transform pressMoveTarget;

        [SerializeField]
        [Tooltip("点下时沿本地 Y 移动的距离，负值为向下")]
        private float pressMoveY = -8f;

        [SerializeField]
        [Tooltip("点下时整体均匀缩放，小于 1 为按下感")]
        private float pressScale = 0.95f;

        [SerializeField]
        [Tooltip("按下/弹回时长")]
        private float duration = 0.08f;

        [SerializeField]
        [Tooltip("缓动")]
        private Ease ease = Ease.OutQuad;

        [SerializeField]
        [Tooltip("点下时播放；留空则播通用 ui_click_03")]
        private AudioClip clip;

        private Selectable _selectable;
        private Tween _scaleTween;
        private Tween _moveTween;
        private Transform _moveTarget;
        private Vector3 _restScale;
        private Vector3 _restLocalPos;
        private Vector2 _restAnchorPos;
        private bool _restCached;
        private bool _moveRestCached;
        private bool _pressed;

        public void OnPointerDown(PointerEventData eventData)
        {
            if (_pressed || !CanPress())
            {
                return;
            }

            _pressed = true;
            PlayPress(true);
            PlayClip();
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            ReleasePress();
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            ReleasePress();
        }

        private void Awake()
        {
            _selectable = GetComponent<Selectable>();
        }

        private void OnDisable()
        {
            _pressed = false;
            KillMotion(snapToRest: true);
        }

        private void LateUpdate()
        {
            if (_pressed && !CanPress())
            {
                ReleasePress();
            }
        }

        private void ReleasePress()
        {
            if (!_pressed)
            {
                return;
            }

            _pressed = false;
            PlayPress(false);
        }

        private bool CanPress()
        {
            return _selectable == null || _selectable.IsInteractable();
        }

        private void PlayPress(bool down)
        {
            var mover = pressMoveTarget != null ? pressMoveTarget : transform;
            CacheRestPose();
            CacheMoveRest(mover);
            var time = Mathf.Max(0f, duration);

            KillMotion(snapToRest: false);

            var scale = Mathf.Max(0.01f, pressScale);
            var targetScale = down
                ? new Vector3(_restScale.x * scale, _restScale.y * scale, _restScale.z)
                : _restScale;
            var targetLocalPos = _restLocalPos;
            var targetAnchor = _restAnchorPos;
            if (down)
            {
                targetLocalPos += new Vector3(0f, pressMoveY, 0f);
                targetAnchor += new Vector2(0f, pressMoveY);
            }

            if (time <= 0f)
            {
                transform.localScale = targetScale;
                ApplyMove(mover, targetLocalPos, targetAnchor);
                return;
            }

            _scaleTween = transform.DOScale(targetScale, time)
                .SetEase(ease)
                .SetUpdate(true)
                .SetLink(gameObject, LinkBehaviour.KillOnDestroy)
                .SetTarget(this);
            var rect = mover as RectTransform;
            if (rect != null)
            {
                _moveTween = rect.DOAnchorPos(targetAnchor, time)
                    .SetEase(ease)
                    .SetUpdate(true)
                    .SetLink(gameObject, LinkBehaviour.KillOnDestroy)
                    .SetTarget(this);
            }
            else
            {
                _moveTween = mover.DOLocalMove(targetLocalPos, time)
                    .SetEase(ease)
                    .SetUpdate(true)
                    .SetLink(gameObject, LinkBehaviour.KillOnDestroy)
                    .SetTarget(this);
            }
        }

        private void PlayClip()
        {
            if (!AppServices.IsReady)
            {
                return;
            }

            var audio = AppServices.Resolve<IAudioService>();
            if (clip != null)
            {
                audio.PlaySfx(clip);
            }
            else
            {
                audio.PlayUiClick();
            }
        }

        private void CacheRestPose()
        {
            if (_restCached)
            {
                return;
            }

            _restScale = transform.localScale;
            _restCached = true;
        }

        private void CacheMoveRest(Transform mover)
        {
            if (_moveRestCached && _moveTarget == mover)
            {
                return;
            }

            _moveTarget = mover;
            _restLocalPos = mover.localPosition;
            var rect = mover as RectTransform;
            _restAnchorPos = rect != null ? rect.anchoredPosition : Vector2.zero;
            _moveRestCached = true;
        }

        private void SnapRest()
        {
            transform.localScale = _restScale;
            if (_moveRestCached && _moveTarget != null)
            {
                ApplyMove(_moveTarget, _restLocalPos, _restAnchorPos);
            }
        }

        private static void ApplyMove(Transform mover, Vector3 localPos, Vector2 anchorPos)
        {
            var rect = mover as RectTransform;
            if (rect != null)
            {
                rect.anchoredPosition = anchorPos;
            }
            else
            {
                mover.localPosition = localPos;
            }
        }

        private void KillMotion(bool snapToRest)
        {
            if (_scaleTween != null && _scaleTween.IsActive())
            {
                _scaleTween.Kill();
            }

            if (_moveTween != null && _moveTween.IsActive())
            {
                _moveTween.Kill();
            }

            _scaleTween = null;
            _moveTween = null;
            if (snapToRest && _restCached)
            {
                SnapRest();
            }
        }
    }
}
