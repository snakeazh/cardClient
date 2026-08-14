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
        private Tween _flipTween;
        private Tween _punchTween;

        /// <param name="card">牌面数据。</param>
        /// <param name="faceState">正面或背面。</param>
        /// <param name="worldPosition">世界坐标位置。</param>
        /// <param name="worldRotation">世界旋转。</param>
        /// <param name="scale">本地缩放。</param>
        public void Initialize(Card card, CardFaceState faceState, Vector3 worldPosition, Quaternion worldRotation, Vector3 scale)
        {
            Card = card;
            FaceState = faceState;

            var t = transform;
            t.position = worldPosition;
            t.rotation = worldRotation;
            t.localScale = scale;

            ApplySprite();
        }

        public void Initialize(Card card, CardFaceState faceState, Vector3 worldPosition, Vector3 worldEulerAngles, Vector3 scale)
        {
            Initialize(card, faceState, worldPosition, Quaternion.Euler(worldEulerAngles), scale);
        }

        public void SetFace(CardFaceState faceState)
        {
            FaceState = faceState;
            ApplySprite();
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

        public Tween Flip(float duration = 0.35f, Ease ease = Ease.InOutSine)
        {
            var next = FaceState == CardFaceState.Front ? CardFaceState.Back : CardFaceState.Front;
            return FlipTo(next, duration, ease);
        }

        public Tween FlipTo(CardFaceState faceState, float duration = 0.35f, Ease ease = Ease.InOutSine)
        {
            _flipTween?.Kill();
            if (duration <= 0f)
            {
                SetFace(faceState);
                return null;
            }

            var half = duration * 0.5f;
            var seq = DOTween.Sequence();
            seq.Append(transform.DOLocalRotate(new Vector3(0f, 90f, 0f), half, RotateMode.LocalAxisAdd).SetEase(ease));
            seq.AppendCallback(() => SetFace(faceState));
            seq.Append(transform.DOLocalRotate(new Vector3(0f, -90f, 0f), half, RotateMode.LocalAxisAdd).SetEase(ease));
            _flipTween = seq;
            return seq;
        }

        public Tween PunchScale(float punch = 0.22f, float duration = 0.32f)
        {
            _punchTween?.Kill();
            _punchTween = transform.DOPunchScale(Vector3.one * punch, duration, 10, 0.6f);
            return _punchTween;
        }

        private void OnDestroy()
        {
            _moveTween?.Kill();
            _flipTween?.Kill();
            _punchTween?.Kill();
        }

        private void ApplySprite()
        {
            var renderer = ResolveRenderer();
            if (renderer == null)
            {
                return;
            }

            renderer.sprite = FaceState == CardFaceState.Front
                ? CardSpriteLibrary.GetFace(Card)
                : CardSpriteLibrary.Back;
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
    }
}
