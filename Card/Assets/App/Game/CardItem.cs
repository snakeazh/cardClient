using DG.Tweening;
using Framework.Log;
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
        public GameObject CardDragEffect;
        public GameObject CardDragSuccessEffect;

        public Card Card { get; private set; }
        public CardFaceState FaceState { get; private set; }

        public Transform FxAnchor
        {
            get
            {
                return CurrentRenderer != null ? CurrentRenderer.transform : transform;
            }
        }

        private Tween _moveTween;
        private Tween _rotateTween;
        private Tween _flipTween;
        private Tween _backFadeTween;
        private Tween _successFxHide;
        private float _backAlphaTarget = 1f;
        private bool _backSeeThrough;
        private Animator _tweenAnimator;
        private Transform _tweenTarget;
        private Transform _backNode;
        private Transform _frontTransparentNode;
        public SpriteRenderer backRenderer;
        public SpriteRenderer frontTransparentRenderer;
        private bool _tweenAnimatorResolved;

        private Tween _successFxTween;

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
            _backSeeThrough = false;
            _backAlphaTarget = 1f;
            SetFace(CardFaceState.Back);
            ApplyFrontTransparentVisible(false);
            HideDragEffects();
            var back = ResolveBackRenderer();
            if (back != null)
            {
                var c = back.color;
                c.a = 1f;
                back.color = c;
            }
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
            _flipTween?.Kill();
            _flipTween = null;
            if (faceState == CardFaceState.Front)
            {
                SetBackSeeThrough(false, 0f);
            }

            FaceState = faceState;
            ApplySprite();
            transform.localRotation = FaceYaw(faceState);
        }

        /// <summary>停掉洗牌/飞牌 Animator，并把 TweenTarget 姿势归零（落位后调用）。</summary>
        public void StopTweenAnimation()
        {
            DisableShuffleAnimator();
            ResetTweenTargetPose();
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

        public void StopMove()
        {
            _moveTween?.Kill();
            _moveTween = null;
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
            if (faceState == CardFaceState.Front)
            {
                SetBackSeeThrough(false, 0f);
            }

            if (duration <= 0f)
            {
                SetFace(faceState);
                return null;
            }

            if (FaceState == faceState)
            {
                transform.localRotation = FaceYaw(faceState);
                return null;
            }

            DisableShuffleAnimator();
            transform.localRotation = FaceYaw(FaceState);

            // 转到侧立时换贴图，再转到目标朝向，避免背面被镜像。
            var fromYaw = FaceState == CardFaceState.Back ? 180f : 0f;
            var toYaw = faceState == CardFaceState.Back ? 180f : 0f;
            var midYaw = (fromYaw + toYaw) * 0.5f;
            var half = duration * 0.5f;
            var target = faceState;

            var seq = DOTween.Sequence();
            seq.Append(transform.DOLocalRotate(new Vector3(0f, midYaw, 0f), half).SetEase(ease));
            seq.AppendCallback(() =>
            {
                FaceState = target;
                ApplySprite();
            });
            seq.Append(transform.DOLocalRotate(new Vector3(0f, toYaw, 0f), half).SetEase(ease));
            seq.OnComplete(() =>
            {
                FaceState = target;
                ApplySprite();
                transform.localRotation = FaceYaw(target);
            });
            _flipTween = seq;
            return seq;
        }

        /// <summary>
        /// 洗牌出现：普通张播 aini_card_appear01，最后一张播 aini_card_appear02。
        /// </summary>
        public void PlayShuffleAppear(bool lastCard)
        {
            SetFace(CardFaceState.Back);
            PlayTweenClip(lastCard ? ShuffleAppear02 : ShuffleAppear01);
        }

        public void SetSpritesVisible(bool visible)
        {
            var front = ResolveRenderer();
            if (front != null)
            {
                front.enabled = visible;
            }

            var back = ResolveBackRenderer();
            if (back != null)
            {
                back.enabled = visible;
            }

            // 透视垫层只在放大镜偷看时亮，避免未偷看时透出牌面像翻牌。
            ApplyFrontTransparentVisible(visible && _backSeeThrough);
        }

        public void SetSortingOrder(int order)
        {
            var front = ResolveRenderer();
            if (front != null)
            {
                front.sortingOrder = order;
            }

            var back = ResolveBackRenderer();
            if (back != null)
            {
                back.sortingOrder = order;
            }

            var peek = ResolveFrontTransparentRenderer();
            if (peek != null)
            {
                peek.sortingOrder = order - 1;
            }

            ApplyEffectSorting(CardDragEffect, order + 1);
            ApplyEffectSorting(CardDragSuccessEffect, order + 1);
        }

        /// <summary>点选搓牌时显示循环拖拽特效。</summary>
        public void SetDragEffectVisible(bool visible)
        {
            SetEffectActive(CardDragEffect, visible);
            if (visible)
            {
                var order = CurrentRenderer != null ? CurrentRenderer.sortingOrder + 1 : 50;
                ApplyEffectSorting(CardDragEffect, order);
            }
        }

        /// <summary>幅度和时间都够时叠上成功特效，拖拽特效保持显示。</summary>
        public void PlayDragSuccessEffect(float autoHideAfter = 0f)
        {
            SetEffectActive(CardDragSuccessEffect, true);
            var order = CurrentRenderer != null ? CurrentRenderer.sortingOrder + 1 : 50;
            ApplyEffectSorting(CardDragSuccessEffect, Mathf.Max(order, 80));

            _successFxHide?.Kill();
            _successFxHide = null;
            if (autoHideAfter > 0f)
            {
                _successFxHide = DOVirtual.DelayedCall(autoHideAfter, () => SetEffectActive(CardDragSuccessEffect, false));
            }
        }

        public void HideDragSuccessLater(float delay = 1.2f)
        {
            _successFxHide?.Kill();
            if (CardDragSuccessEffect == null || !CardDragSuccessEffect.activeSelf)
            {
                _successFxHide = null;
                return;
            }

            _successFxHide = DOVirtual.DelayedCall(delay, () => SetEffectActive(CardDragSuccessEffect, false));
        }

        public void HideDragEffects()
        {
            _successFxHide?.Kill();
            _successFxHide = null;
            SetEffectActive(CardDragEffect, false);
            SetEffectActive(CardDragSuccessEffect, false);
        }

        /// <summary>
        /// 放大镜偷看：牌背半透明，垫层露出牌面。不改旋转、不翻面。
        /// </summary>
        public void SetBackSeeThrough(bool seeThrough, float duration = 0.2f)
        {
            if (_backSeeThrough == seeThrough)
            {
                return;
            }

            _backSeeThrough = seeThrough;
            if (seeThrough)
            {
                ApplySprite();
            }

            ApplyFrontTransparentVisible(seeThrough);
            FadeBackAlpha(seeThrough ? 0.5f : 1f, duration);
        }

        public bool IsBackSeeThrough => _backSeeThrough;

        /// <summary>Back 节点透明度渐变到目标值；重复调同一目标不会重播。放大镜偷看用。</summary>
        public void FadeBackAlpha(float alpha, float duration = 0.2f)
        {
            if (Mathf.Approximately(_backAlphaTarget, alpha))
            {
                return;
            }

            _backAlphaTarget = alpha;
            var back = ResolveBackRenderer();
            if (back == null)
            {
                return;
            }

            _backFadeTween?.Kill();
            _backFadeTween = back.DOFade(alpha, duration);
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
            _backFadeTween?.Kill();
            _successFxHide?.Kill();
        }

        private static void SetEffectActive(GameObject go, bool visible)
        {
            if (go == null)
            {
                return;
            }

            if (!visible)
            {
                if (go.activeSelf)
                {
                    go.SetActive(false);
                }

                return;
            }

            if (go.activeSelf)
            {
                go.SetActive(false);
            }

            go.SetActive(true);
            var systems = go.GetComponentsInChildren<ParticleSystem>(true);
            for (var i = 0; i < systems.Length; i++)
            {
                systems[i].Clear(true);
                systems[i].Play(true);
            }
        }

        private static void ApplyEffectSorting(GameObject go, int order)
        {
            if (go == null)
            {
                return;
            }

            var renderers = go.GetComponentsInChildren<ParticleSystemRenderer>(true);
            for (var i = 0; i < renderers.Length; i++)
            {
                renderers[i].sortingOrder = order;
            }
        }

        private void ApplySprite()
        {
            var face = CardSpriteLibrary.GetFace(Card);
            var front = ResolveRenderer();
            if (front != null)
            {
                // 逻辑背面用牌背贴图画在 CurrentRenderer 上（与 FlipTo 中途换图一致）。
                front.sprite = FaceState == CardFaceState.Back ? CardSpriteLibrary.Back : face;
            }

            var back = ResolveBackRenderer();
            if (back != null)
            {
                back.sprite = CardSpriteLibrary.Back;
            }

            var peek = ResolveFrontTransparentRenderer();
            if (peek != null)
            {
                peek.sprite = face;
            }
        }

        private void ApplyFrontTransparentVisible(bool visible)
        {
            var peek = ResolveFrontTransparentRenderer();
            if (peek != null)
            {
                peek.enabled = visible;
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
            return backRenderer;
        }

        private SpriteRenderer ResolveFrontTransparentRenderer()
        {
            ResolveTweenHierarchy();
            return frontTransparentRenderer;
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

            if (frontTransparentRenderer != null)
            {
                _frontTransparentNode = frontTransparentRenderer.transform;
            }

            if (backRenderer != null)
            {
                _backNode = backRenderer.transform;
            }

            if (_frontTransparentNode != null)
            {
                _frontTransparentNode.localRotation = Quaternion.Euler(0f, 180f, 0f);
                if (backRenderer != null)
                {
                    // frontTransparentRenderer.sharedMaterial = backRenderer.sharedMaterial;
                }

                if (!_backSeeThrough)
                {
                    frontTransparentRenderer.enabled = false;
                }
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

            if (animator.runtimeAnimatorController == null)
            {
                AppLog.Warn(
                    LogChannel.Game,
                    $"'{animator.name}' has no AnimatorController — " +
                    "bundle dependency 'animations' not loaded. Cannot play " + clipName);
                return;
            }

            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            animator.enabled = true;
            if (!animator.isInitialized)
            {
                animator.Rebind();
                animator.Update(0f);
            }

            animator.Play(clipName, 0, 0f);
            animator.Update(0f);
            SetSpritesVisible(true);
        }


        private void DisableShuffleAnimator()
        {
            var animator = ResolveTweenAnimator();
            if (animator != null)
            {
                animator.enabled = false;
            }
        }

        private void ResetTweenTargetPose()
        {
            ResolveTweenHierarchy();
            if (_tweenTarget == null)
            {
                return;
            }

            _tweenTarget.localPosition = Vector3.zero;
            _tweenTarget.localRotation = Quaternion.identity;
            _tweenTarget.localScale = Vector3.one;
        }
    }
}
