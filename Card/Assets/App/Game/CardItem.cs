using System.Collections.Generic;
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
        private GameObject _shakeCardEffect02;
        private GameObject _changeCard01;
        private GameObject _changeCard02;
        private readonly Dictionary<int, int> _effectSortBase = new Dictionary<int, int>();
        public SpriteRenderer backRenderer;
        public SpriteRenderer frontTransparentRenderer;
        private bool _tweenAnimatorResolved;
        private const int PrefabCardSorting = 3;

        private Tween _successFxTween;

        // --- 空闲漂浮两套风格：未选中=纯上下波浪（共享时钟+index 相位，无摆动）；
        //     选中=旧 CardHover 呼吸抖动风格（独立时钟+随机相位+Z 轴摆动，各自旋转不与波浪对齐）。
        //     只写 TweenTarget 子节点，根节点世界坐标归 CardTableAnimator 管 ---
        [Header("Idle 呼吸抖动")]
        [Tooltip("上下浮动幅度（世界单位，牌高 1.57）")]
        [SerializeField] private float idleFloatAmplitude = 0.03f;
        [Tooltip("一个起伏周期的秒数")]
        [SerializeField] private float idleFloatPeriod = 3.0f;
        [Tooltip("选中时左右摆动角度（度，绕 Z 轴）；未选中纯上下浮动无摆动")]
        [SerializeField] private float idleFloatSwayDegrees = 1.2f;
        [Tooltip("摆动相对浮动的相位差（弧度）")]
        [SerializeField] private float idleFloatSwayLag = 0.8f;
        [Tooltip("漂浮开启/模式切换/从动画让位恢复时的幅度渐入时长（秒）")]
        [SerializeField] private float idleFloatBlendDuration = 0.6f;
        [Tooltip("选中后漂浮幅度倍率（呼吸略微增强）")]
        [SerializeField] private float idleFloatBoostAmpFactor = 1.6f;
        [Tooltip("选中后摆动角度倍率")]
        [SerializeField] private float idleFloatBoostSwayFactor = 1.5f;
        [Tooltip("相邻手牌相位差（弧度）：波浪密度，发牌落位时生效")]
        [SerializeField] private float idleFloatPhaseStep = 1.15f;
        [Tooltip("选中增强的平滑过渡时长（秒）")]
        [SerializeField] private float idleFloatBoostBlendDuration = 0.35f;

        private bool _idleFloatOn;
        private bool _idleFloatPoseActive;
        private bool _idleFloatBlocked;
        private bool _idleFloatBoosted;
        private float _idleFloatPhase;
        private float _idleFloatBlend;
        private float _idleFloatBoostBlend;

        // 选中态（旧 CardHover 呼吸抖动风格）：独立时钟 + 随机相位，自己转自己的，不做波浪跟随。
        private float _idleFloatSoloAngle;
        private float _idleFloatSoloPhase;

        /// <summary>相邻手牌相位差（波浪密度），发牌时由 CardTableAnimator 读取。</summary>
        public float IdleFloatPhaseStep => idleFloatPhaseStep;

        // 全局共享的波浪时钟（未选中的牌读同一角度，每帧只推进一次）。
        private static float _sharedFloatAngle;
        private static int _sharedFloatAngleFrame = -1;

        private const string ShuffleAppear01 = "aini_card_appear01";
        private const string ShuffleAppear02 = "aini_card_appear02";
        private const string DealClip = "aini_card_deal";
        private const string DealHighlightClip = "aini_card_back";
        private const string SettleClip = "aini_card_settle";
        private const string ChangeClip = "aini_card_change";
        private const float ChangeClipFallback = 0.6f;

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
            _idleFloatOn = false;
            _idleFloatBlocked = false;
            _idleFloatBoosted = false;
            ClearIdleFloatPose();
            _backSeeThrough = false;
            _backAlphaTarget = 1f;
            SetFace(CardFaceState.Back);
            ApplyFrontTransparentVisible(false);
            HideDragEffects();
            var front = ResolveRenderer();
            if (front != null)
            {
                front.color = Color.white;
            }

            var back = ResolveBackRenderer();
            if (back != null)
            {
                back.color = Color.white;
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

            ApplyEffectSorting(ResolveShakeCardEffect02(), order + 1);
            ApplyRelativeEffectSorting(ResolveChangeCard01(), order);
            ApplyRelativeEffectSorting(ResolveChangeCard02(), order);
            ApplyEffectSorting(CardDragEffect, order + 1);
            ApplyEffectSorting(CardDragSuccessEffect, order + 1);
        }

        /// <summary>搓牌点选阶段：己方牌抬起时显示 ChangeCard02。</summary>
        public void SetDragEffectVisible(bool visible)
        {
            var fx = ResolveChangeCard02();
            if (visible)
            {
                SetEffectActive(ResolveChangeCard01(), false);
                SetEffectActive(ResolveNamedEffect("ShakeCardEffect01"), false);
                SetEffectActive(ResolveShakeCardEffect02(), false);
            }

            if (fx != null && fx.activeSelf == visible)
            {
                if (visible)
                {
                    ApplyRelativeEffectSorting(fx, CardSorting());
                }

                return;
            }

            SetEffectActive(fx, visible);
            if (visible)
            {
                ApplyRelativeEffectSorting(fx, CardSorting());
            }
        }

        /// <summary>选中要替换的牌：关掉点选框，播 ChangeCard01。</summary>
        public void PlayChangeReplaceFx()
        {
            SetEffectActive(ResolveChangeCard02(), false);
            SetEffectActive(ResolveShakeCardEffect02(), false);
            var fx = ResolveChangeCard01();
            SetEffectActive(fx, true);
            ApplyRelativeEffectSorting(fx, CardSorting());
        }

        public void HideChangeCardFx()
        {
            SetEffectActive(ResolveChangeCard01(), false);
            SetEffectActive(ResolveChangeCard02(), false);
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
            HideChangeCardFx();
            SetEffectActive(ResolveShakeCardEffect02(), false);
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
            FadeBackAlpha(seeThrough ? 0.15f : 1f, duration);
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

        /// <summary>搓牌换牌：播 aini_card_change，返回片段时长。</summary>
        public float PlayChange()
        {
            ResetTweenTargetPose();
            PlayTweenClip(ChangeClip);
            return GetTweenClipLength(ChangeClip);
        }

        /// <summary>
        /// 空闲呼吸抖动开关。phase 为本张牌的波浪相位（落位时 index × <see cref="IdleFloatPhaseStep"/>，
        /// 五张牌错开形成波浪），省略时沿用上次相位。所有牌共享同一波浪时钟：
        /// 搓牌/动画让位暂停后恢复会自动回到波浪当前位置，不会掉队。
        /// TweenTarget 动画片段（发牌/结算/换牌）播放期间自动让位，播完自动恢复。
        /// 仅玩家手牌开启；敌人牌与发牌堆牌永不调用。
        /// </summary>
        public void SetIdleFloat(bool on, float phase = float.NaN)
        {
            if (!float.IsNaN(phase))
            {
                _idleFloatPhase = phase;
            }

            if (_idleFloatOn == on)
            {
                return;
            }

            _idleFloatOn = on;
            if (!on)
            {
                ClearIdleFloatPose();
            }
            else
            {
                // 幅度渐入：sin(相位) 不为 0，直接开会从静止跳到漂浮位。
                _idleFloatBlend = 0f;
            }
        }

        /// <summary>
        /// 选中开关：选中切换到旧 CardHover 呼吸抖动风格（独立时钟 + 随机相位 + Z 轴摆动），
        /// 未选中回到共享波浪（纯上下浮动）。切换不做相位对齐，靠幅度渐入平滑过渡。
        /// </summary>
        public void SetIdleFloatBoosted(bool boosted)
        {
            if (_idleFloatBoosted == boosted)
            {
                return;
            }

            _idleFloatBoosted = boosted;
            _idleFloatBlend = 0f;
            if (boosted)
            {
                _idleFloatSoloAngle = 0f;
                _idleFloatSoloPhase = Random.value * Mathf.PI * 2f;
            }
        }

        private void LateUpdate()
        {
            if (!_idleFloatOn)
            {
                return;
            }

            var animator = ResolveTweenAnimator();
            if (animator != null && animator.enabled && animator.runtimeAnimatorController != null)
            {
                var state = animator.GetCurrentAnimatorStateInfo(0);
                if (state.loop || state.normalizedTime < 1f)
                {
                    // 片段正在驱动 TweenTarget 姿势，漂浮让位且不推进相位；
                    // 片段末帧都是中性姿势，恢复时从静止重新渐入。
                    _idleFloatBlocked = true;
                    return;
                }

                // 片段已播完但 Animator 仍开着并钳制末帧（如 aini_card_settle 后无人关）：
                // 关掉并归零姿势，漂浮接管。各片段末帧均为中性姿势，归零无跳变。
                animator.enabled = false;
                ResetTweenTargetPose();
            }

            if (_tweenTarget == null)
            {
                return;
            }

            if (_idleFloatBlocked)
            {
                _idleFloatBlocked = false;
                _idleFloatBlend = 0f;
            }

            _idleFloatBlend = Mathf.Min(1f, _idleFloatBlend + Time.deltaTime / Mathf.Max(0.01f, idleFloatBlendDuration));
            _idleFloatBoostBlend = Mathf.MoveTowards(
                _idleFloatBoostBlend,
                _idleFloatBoosted ? 1f : 0f,
                Time.deltaTime / Mathf.Max(0.01f, idleFloatBoostBlendDuration));

            float t;
            float amp;
            float sway;
            if (_idleFloatBoosted)
            {
                // 选中：旧 CardHover 呼吸抖动风格——独立时钟 + 随机相位 + Z 轴摆动，各自旋转不与波浪对齐。
                _idleFloatSoloAngle += Time.deltaTime * (Mathf.PI * 2f / Mathf.Max(0.1f, idleFloatPeriod));
                t = _idleFloatSoloAngle + _idleFloatSoloPhase;
                amp = idleFloatAmplitude * Mathf.Lerp(1f, idleFloatBoostAmpFactor, _idleFloatBoostBlend) * _idleFloatBlend;
                sway = idleFloatSwayDegrees * Mathf.Lerp(1f, idleFloatBoostSwayFactor, _idleFloatBoostBlend) * _idleFloatBlend;
            }
            else
            {
                // 未选中：共享波浪时钟 + index 相位，纯上下浮动（无摆动）。
                // 某张牌暂停（搓牌/动画让位）后时钟仍在走，恢复时自动回到波浪当前位置，不会掉队。
                if (_sharedFloatAngleFrame != Time.frameCount)
                {
                    _sharedFloatAngleFrame = Time.frameCount;
                    _sharedFloatAngle += Time.deltaTime * (Mathf.PI * 2f / Mathf.Max(0.1f, idleFloatPeriod));
                }

                t = _sharedFloatAngle + _idleFloatPhase;
                amp = idleFloatAmplitude * _idleFloatBlend;
                sway = 0f;
            }

            _tweenTarget.localPosition = new Vector3(0f, Mathf.Sin(t) * amp, 0f);
            _tweenTarget.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(t + idleFloatSwayLag) * sway);
            _idleFloatPoseActive = true;
        }

        /// <summary>漂浮关闭时把 TweenTarget 归位；动画片段正在驱动姿势时不抢写（归零会盖掉片段曲线）。</summary>
        private void ClearIdleFloatPose()
        {
            if (!_idleFloatPoseActive)
            {
                return;
            }

            _idleFloatPoseActive = false;
            var animator = _tweenAnimator;
            var busy = animator != null && animator.enabled && animator.runtimeAnimatorController != null;
            if (busy)
            {
                var state = animator.GetCurrentAnimatorStateInfo(0);
                busy = state.loop || state.normalizedTime < 1f;
            }

            if (!busy)
            {
                ResetTweenTargetPose();
            }
        }

        private void OnDestroy()
        {
            _moveTween?.Kill();
            _rotateTween?.Kill();
            _flipTween?.Kill();
            _backFadeTween?.Kill();
            _successFxHide?.Kill();
        }

        /// <summary>对象池回收：停掉全部 tween 与洗牌/飞牌动画、隐藏特效；
        /// 显示状态（牌面/朝向/透明度）由下次租用的 Initialize 全量重置。</summary>
        public void ResetForPool()
        {
            _moveTween?.Kill();
            _moveTween = null;
            _rotateTween?.Kill();
            _rotateTween = null;
            _flipTween?.Kill();
            _flipTween = null;
            _backFadeTween?.Kill();
            _backFadeTween = null;
            _successFxHide?.Kill();
            _successFxHide = null;
            _successFxTween?.Kill();
            _successFxTween = null;
            StopTweenAnimation();
            HideDragEffects();
            _idleFloatOn = false;
            _idleFloatBlocked = false;
            _idleFloatBoosted = false;
            _idleFloatPoseActive = false;
            _backSeeThrough = false;
            _backAlphaTarget = 1f;
            var front = ResolveRenderer();
            if (front != null)
            {
                front.color = Color.white;
            }

            var back = ResolveBackRenderer();
            if (back != null)
            {
                back.color = Color.white;
            }

            // aini_card_appear01 播完会把 Back 压暗到 0.5 并钳制保持，deal 剪辑不写颜色；
            // Rebind 把全部动画属性写回预制体默认值，避免池化复用带出上一局的残留姿势/颜色。
            var animator = ResolveTweenAnimator();
            if (animator != null)
            {
                animator.Rebind();
                animator.enabled = false;
            }
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
            var animators = go.GetComponentsInChildren<Animator>(true);
            for (var i = 0; i < animators.Length; i++)
            {
                var animator = animators[i];
                if (animator == null || animator.runtimeAnimatorController == null)
                {
                    continue;
                }

                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                animator.enabled = true;
                animator.Rebind();
                animator.Play(0, 0, 0f);
                animator.Update(0f);
            }

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

            var particles = go.GetComponentsInChildren<ParticleSystemRenderer>(true);
            for (var i = 0; i < particles.Length; i++)
            {
                particles[i].sortingOrder = order;
            }
        }

        /// <summary>
        /// 按预制体相对牌面的前后关系平移层级：ChangeCard02 在牌下，ChangeCard01 一手在下、一手在上。
        /// </summary>
        private void ApplyRelativeEffectSorting(GameObject go, int cardOrder)
        {
            if (go == null)
            {
                return;
            }

            var renderers = go.GetComponentsInChildren<Renderer>(true);
            for (var i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i];
                if (renderer == null)
                {
                    continue;
                }

                var id = renderer.GetInstanceID();
                if (!_effectSortBase.TryGetValue(id, out var original))
                {
                    original = renderer.sortingOrder;
                    _effectSortBase[id] = original;
                }

                renderer.sortingOrder = original + (cardOrder - PrefabCardSorting);
            }
        }

        private int CardSorting()
        {
            return CurrentRenderer != null ? CurrentRenderer.sortingOrder : PrefabCardSorting;
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

        private GameObject ResolveShakeCardEffect02()
        {
            if (_shakeCardEffect02 != null)
            {
                return _shakeCardEffect02;
            }

            _shakeCardEffect02 = ResolveNamedEffect("ShakeCardEffect02");
            if (_shakeCardEffect02 != null)
            {
                return _shakeCardEffect02;
            }

            if (CardDragEffect != null && CardDragEffect.name == "ShakeCardEffect02")
            {
                _shakeCardEffect02 = CardDragEffect;
            }
            else if (CardDragSuccessEffect != null && CardDragSuccessEffect.name == "ShakeCardEffect02")
            {
                _shakeCardEffect02 = CardDragSuccessEffect;
            }
            else
            {
                _shakeCardEffect02 = CardDragEffect;
            }

            return _shakeCardEffect02;
        }

        private GameObject ResolveChangeCard01()
        {
            if (_changeCard01 == null)
            {
                _changeCard01 = ResolveNamedEffect("ChangeCard01");
            }

            return _changeCard01;
        }

        private GameObject ResolveChangeCard02()
        {
            if (_changeCard02 == null)
            {
                _changeCard02 = ResolveNamedEffect("ChangeCard02");
            }

            return _changeCard02;
        }

        private GameObject ResolveNamedEffect(string name)
        {
            ResolveTweenHierarchy();
            var root = _tweenTarget != null ? _tweenTarget : transform;
            var node = root.Find(name);
            if (node == null)
            {
                node = transform.Find(name);
            }

            return node != null ? node.gameObject : null;
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

        private float GetTweenClipLength(string clipName)
        {
            var animator = ResolveTweenAnimator();
            var controller = animator != null ? animator.runtimeAnimatorController : null;
            if (controller == null)
            {
                return ChangeClipFallback;
            }

            var clips = controller.animationClips;
            for (var i = 0; i < clips.Length; i++)
            {
                if (clips[i] != null && clips[i].name == clipName)
                {
                    return Mathf.Max(clips[i].length, 0.05f);
                }
            }

            return ChangeClipFallback;
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
