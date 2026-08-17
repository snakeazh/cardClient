using DG.Tweening;
using UnityEngine;

namespace App.Game
{
    public enum CardFaceState
    {
        Front = 0,
        Back = 1
    }

    /// <summary>桌上单张牌的显示与翻转动画。逻辑牌面见 <see cref="Card"/>。</summary>
    public class CardItem : MonoBehaviour
    {
        public SpriteRenderer CurrentRenderer;

        public Card Card { get; private set; }
        public CardFaceState FaceState { get; private set; }

        private Tween _moveTween;
        private Tween _rotateTween;
        private Tween _flipTween;
        private Tween _punchTween;
        private Animator _tweenAnimator;
        private Transform _tweenTarget;
        private Transform _backNode;
        private SpriteRenderer _backRenderer;
        private bool _tweenAnimatorResolved;

        private const string ShuffleAppear01 = "aini_card_appear01";
        private const string ShuffleAppear02 = "aini_card_appear02";
        private const string DealClip = "aini_card_deal";
        private const string DealHighlightClip = "aini_card_back";
        private const string SettleClip = "aini_card_settle";

        /// <param name="card">牌面数据。</param>
        /// <param name="faceState">正面或背面。</param>
        /// <param name="worldPosition">世界坐标位置。</param>
        /// <param name="worldRotation">世界旋转。</param>
        /// <param name="scale">本地缩放。</param>
        public void Initialize(Card card, CardFaceState faceState, Vector3 worldPosition, Quaternion worldRotation, Vector3 scale)
        {
            Card = card;

            var t = transform;
            t.position = worldPosition;
            t.rotation = worldRotation;
            t.localScale = scale;

            DisableShuffleAnimator();
            SetFace(CardFaceState.Back);
        }

        public void Initialize(Card card, CardFaceState faceState, Vector3 worldPosition, Vector3 worldEulerAngles, Vector3 scale)
        {
            Initialize(card, faceState, worldPosition, Quaternion.Euler(worldEulerAngles), scale);
        }

        /// <summary>
        /// 本节点本地 Y：0 正面，180 背面。只改 CardItem 自身，不改子节点。
        /// </summary>
        public void SetFace(CardFaceState faceState)
        {
            FaceState = faceState;
            ApplySprite();
            transform.localRotation = FaceYaw(faceState);
        }

        public static Quaternion FaceYaw(CardFaceState faceState)
        {
            return Quaternion.Euler(0f, faceState == CardFaceState.Back ? 180f : 0f, 0f);
        }

        public void SetCard(Card card)
        {
            Card = card;
            ApplySprite();
        }

        public Tween MoveTo(Vector3 worldPosition, float duration, Ease ease = Ease.OutQuad)
        {
            _moveTween?.Kill();
            _moveTween = transform.DOMove(worldPosition, duration).SetEase(ease);
            return _moveTween;
        }

        public Tween RotateTo(Quaternion worldRotation, float duration, Ease ease = Ease.OutCubic)
        {
            _rotateTween?.Kill();
            _flipTween?.Kill();
            _rotateTween = transform.DORotateQuaternion(worldRotation, duration).SetEase(ease);
            return _rotateTween;
        }

        public Tween Flip(float duration = 0.35f, Ease ease = Ease.InOutSine)
        {
            var next = FaceState == CardFaceState.Front ? CardFaceState.Back : CardFaceState.Front;
            return FlipTo(next, duration, ease);
        }

        public Tween FlipTo(CardFaceState faceState, float duration = 0.35f, Ease ease = Ease.InOutSine)
        {
            _rotateTween?.Kill();
            _flipTween?.Kill();
            if (duration <= 0f)
            {
                SetFace(faceState);
                return null;
            }

            DisableShuffleAnimator();
            transform.localRotation = FaceYaw(FaceState);

            var seq = DOTween.Sequence();
            seq.Append(transform.DOLocalRotateQuaternion(FaceYaw(faceState), duration).SetEase(ease));
            seq.OnComplete(() => SetFace(faceState));
            _flipTween = seq;
            return seq;
        }

        public Tween PunchScale(float punch = 0.22f, float duration = 0.32f)
        {
            _punchTween?.Kill();
            _punchTween = transform.DOPunchScale(Vector3.one * punch, duration, 10, 0.6f);
            return _punchTween;
        }

        /// <summary>
        /// 洗牌出现：普通张播 aini_card_appear01，最后一张播 aini_card_appear02。
        /// </summary>
        public void PlayShuffleAppear(bool lastCard)
        {
            SetFace(CardFaceState.Back);
            PlayTweenClip(lastCard ? ShuffleAppear02 : ShuffleAppear01);
        }

        /// <summary>发牌：堆顶飞出播 aini_card_deal。</summary>
        public void PlayDeal()
        {
            SetFace(CardFaceState.Back);
            PlayTweenClip(DealClip);
        }

        /// <summary>发牌后新堆顶高亮：播 aini_card_back。</summary>
        public void PlayDealHighlight()
        {
            SetFace(CardFaceState.Back);
            PlayTweenClip(DealHighlightClip);
        }

        /// <summary>亮牌结算牌型：播 aini_card_settle。</summary>
        public void PlaySettle()
        {
            PlayTweenClip(SettleClip);
        }

        private void OnDestroy()
        {
            _moveTween?.Kill();
            _rotateTween?.Kill();
            _flipTween?.Kill();
            _punchTween?.Kill();
        }

        private void ApplySprite()
        {
            var front = ResolveRenderer();
            if (front != null)
            {
                front.sprite = CardSpriteLibrary.GetFace(Card);
            }

            var back = ResolveBackRenderer();
            if (back != null)
            {
                back.sprite = CardSpriteLibrary.Back;
            }
        }

        private SpriteRenderer ResolveRenderer()
        {
            if (CurrentRenderer != null)
            {
                return CurrentRenderer;
            }

            CurrentRenderer = GetComponent<SpriteRenderer>();
            if (CurrentRenderer == null)
            {
                CurrentRenderer = GetComponentInChildren<SpriteRenderer>(true);
            }

            return CurrentRenderer;
        }

        private Animator ResolveTweenAnimator()
        {
            ResolveTweenHierarchy();
            return _tweenAnimator;
        }

        private SpriteRenderer ResolveBackRenderer()
        {
            ResolveTweenHierarchy();
            return _backRenderer;
        }

        private void ResolveTweenHierarchy()
        {
            if (_tweenAnimatorResolved)
            {
                return;
            }

            _tweenAnimatorResolved = true;
            _tweenTarget = transform.Find("TweenTarget");
            if (_tweenTarget != null)
            {
                _tweenAnimator = _tweenTarget.GetComponent<Animator>();
            }

            if (_tweenAnimator == null)
            {
                _tweenAnimator = GetComponentInChildren<Animator>(true);
                if (_tweenTarget == null && _tweenAnimator != null)
                {
                    _tweenTarget = _tweenAnimator.transform;
                }
            }

            if (_tweenTarget != null)
            {
                _backNode = _tweenTarget.Find("Back");
            }

            if (_backNode == null)
            {
                _backNode = transform.Find("TweenTarget/Back") ?? transform.Find("Back");
            }

            if (_backNode != null)
            {
                _backRenderer = _backNode.GetComponent<SpriteRenderer>();
            }
        }

        private void PlayTweenClip(string clipName)
        {
            gameObject.SetActive(true);
            var animator = ResolveTweenAnimator();
            if (animator == null)
            {
                return;
            }

            animator.enabled = true;
            animator.Play(clipName, 0, 0f);
        }


        private void DisableShuffleAnimator()
        {
            var animator = ResolveTweenAnimator();
            if (animator != null)
            {
                animator.enabled = false;
            }
        }
    }
}
