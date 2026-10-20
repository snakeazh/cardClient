using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using App.Audio;
using App.Bootstrap;
using App.Game;
using App.Resources;
using CardShare.Contracts.Config;
using DG.Tweening;
using Framework.Assets;
using Framework.Log;
using UnityEngine;

namespace App.UI
{
    /// <summary>
    /// 攻击演出：PlayerRoot 播 clip，位移由 DOTween 驱动；mask / hptextdi 由 GameUI 绑定。
    /// 有 HeroEffects 时玩家出刀可走释放→等待→命中，替换冲刺近战。
    /// </summary>
    public sealed class AttackCutscene
    {
        private const string DefaultClip = "ani_default";
        private const string MissClip = "ani_atk_lv03_miss";
        private const int DeathFxSortingOrder = 240;
        /// <summary>英雄特效（UI 层）的粒子排序，压在卡面和 mask 之上。</summary>
        private const int HeroFxSortingOrder = 230;
        /// <summary>角色特效所在世界平面 Z（与主相机对位用）。</summary>
        private const float FxPlaneZ = 0f;

        // 暗灵法师齐射（01/02/03 + 04）节奏与抛物线参数在 AttackTuning.asset 的「暗灵法师齐射」段，
        // 不读 HeroEffectsConfig 的 Time。主目标命中统一结算，溅射落地只补 04。

        private Transform _hud;
        private Canvas _hudCanvas;
        private RectTransform _playerRoot;
        private Transform _playerHome;
        private Animator _playerAnim;
        private Vector2 _playerHomeAnchored;
        private readonly RectTransform[] _enemyRoots = new RectTransform[3];
        private readonly Animator[] _enemyAnims = new Animator[3];
        private RectTransform _flight;
        private Sequence _seq;
        private int _playToken;
        private RectTransform _incomingRoot;
        private Transform _incomingHome;
        private Vector2 _incomingHomeAnchored;
        private RectTransform _playerCardRect;
        private readonly RectTransform[] _enemyCardRects = new RectTransform[3];
        private readonly List<RectTransform> _knockRects = new List<RectTransform>(3);
        private readonly List<Vector2> _knockHomes = new List<Vector2>(3);
        private readonly List<Sequence> _knockSeqs = new List<Sequence>(3);
        private GameObject _deathFx;
        private readonly List<Animator> _missVictims = new List<Animator>(3);
        private readonly List<Tween> _missHoldTweens = new List<Tween>(3);
        private float _playTimeScale = 1f;
        private int _impactLevel = 1;
        /// <summary>本次命中用的自定义受击动画（HeroConfig.hitAnimation），空则用 ani_atk_lvXX_hit。</summary>
        private string _victimHitClip;
        private AttackTuningConfig.LevelTuning _impactBeat;
        private Vector3 _impactAttackerPos;
        private AudioClip _impactSfx;
        private bool _disposed;
        /// <summary>特效攻击受击：不播卡牌击退抖动（主目标与溅射）。</summary>
        private bool _skillHitNoKnockback;
        private readonly Dictionary<string, GameObject> _heroFxPrefabs = new Dictionary<string, GameObject>(4);
        private readonly List<string> _heroFxOwnedKeys = new List<string>(4);
        private readonly List<GameObject> _skillFxSpawned = new List<GameObject>(4);
        private readonly Dictionary<GameObject, Stack<GameObject>> _heroFxIdle = new Dictionary<GameObject, Stack<GameObject>>(4);
        private readonly Dictionary<GameObject, GameObject> _heroFxOwner = new Dictionary<GameObject, GameObject>(4);
        private Transform _heroFxPoolRoot;
        private IResourceService _heroFxResources;

        public void Bind(Transform hud, PlayerItem player, PlayerItem[] enemies)
        {
            _disposed = false;
            _hud = hud;
            _hudCanvas = hud != null ? hud.GetComponentInParent<Canvas>() : null;
            BindPlayer(player);

            var count = enemies != null ? Math.Min(enemies.Length, _enemyRoots.Length) : 0;
            for (var i = 0; i < _enemyRoots.Length; i++)
            {
                _enemyRoots[i] = null;
                _enemyAnims[i] = null;
                _enemyCardRects[i] = null;
            }

            for (var i = 0; i < count; i++)
            {
                BindEnemy(i, enemies[i]);
            }

            _ = PreloadImpactSfxAsync();
        }

        public Vector3 HitPosition(int visualSlot)
        {
            if (visualSlot < 0 || visualSlot >= _enemyRoots.Length || _enemyRoots[visualSlot] == null)
            {
                return Vector3.zero;
            }

            return _enemyRoots[visualSlot].position;
        }

        public Vector3 HitPositionPlayer()
        {
            return _playerRoot != null ? _playerRoot.position : Vector3.zero;
        }

        /// <summary>
        /// 闪避反击的演出参数：<see cref="HeroConfig.MissAttackEffect"/> 指定反击特效，
        /// <see cref="HeroConfig.hitAnimation"/> 指定敌人受击动画；时长/延迟取那个特效行的 Time / hitTime。
        /// </summary>
        public sealed class DodgeCounterFx
        {
            public GameObject Prefab;

            /// <summary>反击特效时长（HeroEffectsConfig.Time）。</summary>
            public float Time;

            /// <summary>反击开始后多久敌人挨打（HeroEffectsConfig.hitTime），0 表示立刻。</summary>
            public float HitDelay;

            /// <summary>敌人受击动画（HeroConfig.hitAnimation），空则只演特效。</summary>
            public string HitClip;

            public bool HasAny => Prefab != null || !string.IsNullOrEmpty(HitClip);
        }

        /// <summary>
        /// 解析闪避反击演出。没配 <see cref="HeroConfig.MissAttackEffect"/>、或特效与动画都没有时返回 null，按原流程走。
        /// </summary>
        public DodgeCounterFx BuildDodgeCounter(HeroConfig hero)
        {
            if (hero?.MissAttackEffect == null || hero.MissAttackEffect.Length == 0)
            {
                return null;
            }

            HeroEffectsConfig row = null;
            for (var i = 0; i < hero.MissAttackEffect.Length; i++)
            {
                var fx = HeroEffectsConfig.Get(hero.MissAttackEffect[i]);
                if (fx != null && fx.Type == EffectsType.Attack && !string.IsNullOrEmpty(fx.Effects))
                {
                    row = fx;
                    break;
                }
            }

            var prefab = row != null && TryGetHeroFxPrefab(row.Effects, out var cached) ? cached : null;
            var clip = string.IsNullOrEmpty(hero.hitAnimation) ? null : hero.hitAnimation;
            if (prefab == null && clip == null)
            {
                return null;
            }

            var time = row != null ? Mathf.Max(0.01f, row.Time) : 0f;
            return new DodgeCounterFx
            {
                Prefab = prefab,
                Time = time,
                HitDelay = row != null ? Mathf.Clamp(row.hitTime, 0f, time) : 0f,
                HitClip = clip
            };
        }

        public void PlayIncoming(
            int visualSlot,
            int level,
            Func<bool> onHit,
            Action onCollisionDone,
            Action onReturned,
            Action onDone,
            DodgeCounterFx dodgeCounter = null,
            Action onDodgeCounterHit = null)
        {
            Kill();
            level = Mathf.Clamp(level, 1, 3);
            var enemyRoot = visualSlot >= 0 && visualSlot < _enemyRoots.Length ? _enemyRoots[visualSlot] : null;
            var enemyAnim = visualSlot >= 0 && visualSlot < _enemyAnims.Length ? _enemyAnims[visualSlot] : null;
            if (_playerRoot == null || enemyRoot == null)
            {
                Debug.LogWarning(
                    $"[AttackCutscene] PlayIncoming skipped: playerRoot={_playerRoot != null} slot={visualSlot} enemyRoot={enemyRoot != null}");
                onHit?.Invoke();
                onCollisionDone?.Invoke();
                onReturned?.Invoke();
                onDone?.Invoke();
                return;
            }

            var token = ++_playToken;
            BeginPlay(1f);
            var tuning = AttackTuningConfig.Instance;
            var beat = tuning.Level(level);
            _incomingRoot = enemyRoot;
            _incomingHome = enemyRoot.parent;
            _incomingHomeAnchored = enemyRoot.anchoredPosition;
            var homePos = enemyRoot.position;
            var hitPos = _playerRoot.position;

            AttachRootToFlight(_incomingRoot, homePos);
            var homeAnchored = _flight.anchoredPosition;
            var hitAnchored = WorldToHudAnchored(hitPos);

            _seq = DOTween.Sequence();
            _seq.timeScale = _playTimeScale;
            AppendAimAndRetreat(enemyAnim, level, beat, homeAnchored, hitAnchored, invertAim: true);
            _seq.AppendCallback(() =>
            {
                if (token != _playToken)
                {
                    return;
                }

                PlayClip(enemyAnim, Clip(level, "move"));
            });
            _seq.Append(_flight.DOAnchorPos(hitAnchored, beat.MoveDuration).SetEase(Ease.InQuad));
            _seq.AppendCallback(() =>
            {
                if (token != _playToken)
                {
                    return;
                }

                var missed = PlayImpact(enemyAnim, _playerAnim, level, onHit, beat, _playerCardRect, homePos, hitPos);
                if (missed)
                {
                    var counterReady = dodgeCounter != null && dodgeCounter.HasAny &&
                                       (dodgeCounter.Prefab != null || ClipLength(enemyAnim, dodgeCounter.HitClip) > 0f);
                    if (counterReady)
                    {
                        // 下一帧接管：避免在本序列回调里 Kill 自己；也不再往本序列后面接回位，
                        // 否则会跟 StartDodgeCounter 抢 Flight，特效容易还停在人物重叠处。
                        DOVirtual.DelayedCall(0f, () =>
                        {
                            if (token != _playToken)
                            {
                                return;
                            }

                            StartDodgeCounter(token, level, beat, tuning, dodgeCounter, enemyAnim, enemyRoot, homeAnchored,
                                onCollisionDone, onReturned, onDone, onDodgeCounterHit);
                        }, false);
                        return;
                    }

                    // 没配反击演出（或资源还没进来）：反击伤害字直接出在闪避这一帧。
                    AppLog.Info(
                        LogChannel.UI,
                        "[DodgeCounter] 无反击演出（MissAttackEffect 未配或特效/动画资源缺失），伤害字直接出在闪避帧");
                    onDodgeCounterHit?.Invoke();
                }

                DOVirtual.DelayedCall(0f, () =>
                {
                    if (token != _playToken)
                    {
                        return;
                    }

                    PlayIncomingAftermath(
                        token, enemyAnim, beat, tuning, homeAnchored, onCollisionDone, onReturned, onDone);
                }, false);
            });
        }

        /// <summary>来袭命中/闪避后的回位收尾（反击演出另走 <see cref="StartDodgeCounter"/>）。</summary>
        private void PlayIncomingAftermath(
            int token,
            Animator enemyAnim,
            AttackTuningConfig.LevelTuning beat,
            AttackTuningConfig tuning,
            Vector2 homeAnchored,
            Action onCollisionDone,
            Action onReturned,
            Action onDone)
        {
            _seq?.Kill();
            _seq = DOTween.Sequence();
            _seq.timeScale = _playTimeScale;
            _seq.AppendInterval(beat.HitHoldDuration);
            _seq.AppendCallback(() =>
            {
                if (token != _playToken)
                {
                    return;
                }

                PlayClip(enemyAnim, Clip(_impactLevel, "back"));
                PlayVictimIdle(_playerAnim);
                onCollisionDone?.Invoke();
            });
            AppendReturnHome(homeAnchored, beat.BackDuration);
            _seq.AppendCallback(() =>
            {
                if (token != _playToken)
                {
                    return;
                }

                RestoreIncoming();
                RestoreHitTarget();
                PlayClip(enemyAnim, DefaultClip);
                onReturned?.Invoke();
            });
            _seq.AppendInterval(tuning.HpTextHoldDuration);
            _seq.OnComplete(() =>
            {
                if (token != _playToken)
                {
                    return;
                }

                SetAllAnimSpeed(1f);
                onDone?.Invoke();
            });
        }

        /// <summary>
        /// 闪避反击：先把怪物退到**屏幕中心 (0,0)** 受击，反击特效播在**人物身上**；
        /// 过 <see cref="DodgeCounterFx.HitDelay"/> 给敌人播受击动画，
        /// 演完再把卡退回自己的格子、交回父节点，然后 onReturned → 伤害字停留 → onDone。
        /// </summary>
        private void StartDodgeCounter(
            int token,
            int level,
            AttackTuningConfig.LevelTuning beat,
            AttackTuningConfig tuning,
            DodgeCounterFx counter,
            Animator enemyAnim,
            RectTransform enemyRoot,
            Vector2 homeAnchored,
            Action onCollisionDone,
            Action onReturned,
            Action onDone,
            Action onDodgeCounterHit)
        {
            _seq?.Kill();

            var hitClip = string.IsNullOrEmpty(counter.HitClip) ? null : counter.HitClip;
            var clipLength = hitClip != null ? ClipLength(enemyAnim, hitClip) : 0f;
            if (hitClip != null && clipLength <= 0f)
            {
                // 片段没进 PlayerRoot.controller（美术还没配）就别 Play，免得 Animator 报状态找不到。
                AppLog.Warn(LogChannel.UI, $"[AttackCutscene] 反击受击动画缺失 anim={hitClip}，本次跳过");
                hitClip = null;
            }

            var hold = Mathf.Max(counter.Time, counter.HitDelay + Mathf.Max(0f, clipLength));
            AppLog.Info(
                LogChannel.UI,
                $"[DodgeCounter] 起手（敌人退到屏幕中心受击，特效播在人物）fx={(counter.Prefab != null ? counter.Prefab.name : "无")} " +
                $"time={counter.Time:0.###} hitDelay={counter.HitDelay:0.###} anim={hitClip ?? "无"} " +
                $"clip={clipLength:0.###} hold={hold:0.###} back={beat.BackDuration:0.###} home={homeAnchored}");

            _seq = DOTween.Sequence();
            _seq.timeScale = _playTimeScale;

            // 1) 怪物退到屏幕中心 (0,0) 受击：AttackFlight 挂在 HUD 居中锚点上，anchoredPosition = 0 就是屏幕中心。
            _seq.AppendCallback(() =>
            {
                if (token != _playToken)
                {
                    return;
                }

                PlayClip(enemyAnim, Clip(level, "back"));
                PlayVictimIdle(_playerAnim);
                // 挂到 Flight 时 worldPositionStays 可能留下 local 偏移，清掉保证卡心 = Flight。
                if (enemyRoot != null && enemyRoot.parent == _flight)
                {
                    enemyRoot.localPosition = Vector3.zero;
                    enemyRoot.localRotation = Quaternion.identity;
                }
            });
            _seq.Append(_flight.DOAnchorPos(Vector2.zero, beat.BackDuration).SetEase(Ease.OutQuad));
            _seq.Join(_flight.DOLocalRotate(Vector3.zero, beat.BackDuration).SetEase(Ease.OutCubic));
            _seq.AppendCallback(() =>
            {
                if (token != _playToken)
                {
                    return;
                }

                if (enemyRoot != null && enemyRoot.parent == _flight)
                {
                    enemyRoot.localPosition = Vector3.zero;
                    enemyRoot.localRotation = Quaternion.identity;
                }

                // 反击特效播在人物身上（释放点），敌人只在中心挨打。
                var fxPos = _playerRoot != null ? _playerRoot.position : Vector3.zero;
                AppLog.Info(
                    LogChannel.UI,
                    $"[DodgeCounter] 生成点 player={fxPos} enemy={(enemyRoot != null ? enemyRoot.position.ToString() : "null")} " +
                    $"flight={_flight.anchoredPosition}");
                if (counter.Prefab != null)
                {
                    SpawnSkillFxAt(
                        counter.Prefab,
                        fxPos,
                        counter.Time,
                        forceUiLayer: true,
                        scaleMultiplier: 1f,
                        sortingOrder: HeroFxSortingOrder);
                }

                AppLog.Info(
                    LogChannel.UI,
                    $"[DodgeCounter] 敌人已到屏幕中心，人物处播反击 fx={(counter.Prefab != null ? counter.Prefab.name : "无")}");
            });

            // 2) 反击命中：出伤害字 + 敌人受击动画。
            if (counter.HitDelay > 0f)
            {
                _seq.AppendInterval(counter.HitDelay);
            }

            _seq.AppendCallback(() =>
            {
                if (token != _playToken)
                {
                    return;
                }

                onDodgeCounterHit?.Invoke();
                if (hitClip != null)
                {
                    PlayClip(enemyAnim, hitClip);
                }
            });
            _seq.AppendInterval(Mathf.Max(0f, hold - counter.HitDelay));

            // 3) 演完把卡退回自己的格子，交回 mask 与伤害字停留。
            _seq.AppendCallback(() =>
            {
                if (token != _playToken)
                {
                    return;
                }

                onCollisionDone?.Invoke();
            });
            AppendReturnHome(homeAnchored, beat.BackDuration);
            _seq.AppendCallback(() =>
            {
                if (token != _playToken)
                {
                    return;
                }

                RestoreIncoming();
                RestoreHitTarget();
                PlayClip(enemyAnim, DefaultClip);
                onReturned?.Invoke();
            });
            _seq.AppendInterval(tuning.HpTextHoldDuration);
            _seq.OnComplete(() =>
            {
                if (token != _playToken)
                {
                    return;
                }

                SetAllAnimSpeed(1f);
                onDone?.Invoke();
            });
        }

        public void Play(
            int visualSlot,
            int level,
            Func<bool> onHit,
            Action onCollisionDone,
            Action onReturned,
            Action onDone,
            float timeScale = 1f)
        {
            Kill();
            level = Mathf.Clamp(level, 1, 3);
            if (_playerRoot == null || visualSlot < 0 || visualSlot >= _enemyRoots.Length ||
                _enemyRoots[visualSlot] == null)
            {
                Debug.LogWarning(
                    $"[AttackCutscene] Play skipped: playerRoot={_playerRoot != null} slot={visualSlot} enemyRoot={(visualSlot >= 0 && visualSlot < _enemyRoots.Length ? _enemyRoots[visualSlot] != null : false)}");
                onHit?.Invoke();
                onCollisionDone?.Invoke();
                onReturned?.Invoke();
                onDone?.Invoke();
                return;
            }

            var token = ++_playToken;
            BeginPlay(timeScale);
            var tuning = AttackTuningConfig.Instance;
            var beat = tuning.Level(level);
            var targetAnim = _enemyAnims[visualSlot];
            var homeParent = _playerHome != null ? _playerHome : _playerRoot.parent;
            _playerHomeAnchored = _playerRoot.anchoredPosition;
            var homePos = _playerRoot.position;
            var hitPos = _enemyRoots[visualSlot].position;

            AttachRootToFlight(_playerRoot, homePos);
            var homeAnchored = _flight.anchoredPosition;
            var hitAnchored = WorldToHudAnchored(hitPos);

            _seq = DOTween.Sequence();
            _seq.timeScale = _playTimeScale;
            AppendAimAndRetreat(_playerAnim, level, beat, homeAnchored, hitAnchored, invertAim: false);
            _seq.AppendCallback(() =>
            {
                if (token != _playToken)
                {
                    return;
                }

                PlayClip(_playerAnim, Clip(level, "move"));
            });
            _seq.Append(_flight.DOAnchorPos(hitAnchored, beat.MoveDuration).SetEase(Ease.InQuad));
            _seq.AppendCallback(() =>
            {
                if (token != _playToken)
                {
                    return;
                }

                PlayImpact(_playerAnim, targetAnim, level, onHit, beat, _enemyCardRects[visualSlot], homePos, hitPos);
            });
            _seq.AppendInterval(beat.HitHoldDuration);
            _seq.AppendCallback(() =>
            {
                if (token != _playToken)
                {
                    return;
                }

                PlayClip(_playerAnim, Clip(level, "back"));
                PlayVictimIdle(targetAnim);
                for (var i = 0; i < _enemyAnims.Length; i++)
                {
                    if (i != visualSlot)
                    {
                        PlayVictimIdle(_enemyAnims[i]);
                    }
                }
                onCollisionDone?.Invoke();
            });
            AppendReturnHome(homeAnchored, beat.BackDuration);
            _seq.AppendCallback(() =>
            {
                if (token != _playToken)
                {
                    return;
                }

                RestoreRoot(homeParent);
                RestoreHitTarget();
                PlayClip(_playerAnim, DefaultClip);
                onReturned?.Invoke();
            });
            _seq.AppendInterval(tuning.HpTextHoldDuration);
            _seq.OnComplete(() =>
            {
                if (token != _playToken)
                {
                    return;
                }

                SetAllAnimSpeed(1f);
                onDone?.Invoke();
            });
        }

        /// <summary>主目标之外的溅射/AOE：按本波结算播受击或 miss，不改攻击方时间轴。</summary>
        public void ReactSlot(int visualSlot, bool missed)
        {
            if (visualSlot < 0 || visualSlot >= _enemyAnims.Length || _impactBeat == null)
            {
                return;
            }

            var victim = _enemyAnims[visualSlot];
            if (missed)
            {
                PlayClip(victim, MissClip);
                HoldMissUntilDone(victim);
                return;
            }

            var hitClip = _victimHitClip ?? Clip(_impactLevel, "hit");
            PlayClip(victim, hitClip);
            if (_victimHitClip != null)
            {
                // 自定义受击动画比通用 hit 片段长，别让后面的 PlayVictimIdle 打断。
                HoldVictimClipUntilDone(victim, hitClip);
            }

            if (_skillHitNoKnockback)
            {
                return;
            }

            var hitPos = _enemyRoots[visualSlot] != null ? _enemyRoots[visualSlot].position : Vector3.zero;
            InsertHitKnockback(_impactBeat, _enemyCardRects[visualSlot], _impactAttackerPos, hitPos);
        }

        /// <summary>
        /// 玩家出刀：释放特效（按自己的 Time 演完）→ 命中时刻（攻击行 hitTime，0 则等释放演完）：
        /// 结算 + 受击特效 + 受击动画 + 伤害字 → 等命中特效播完 → onReturned → 伤害字停留 → onDone。
        /// 命中特效可以比释放特效先开（116|207 就是 0.3s 出命中、1.2s 播完，和释放一起在 1.5s 收）。
        /// 预制体未预载时返回 false，由调用方回退近战。
        /// </summary>
        /// <param name="onHit">命中瞬间结算（扣血/溶解），不要在这里出伤害字。</param>
        /// <param name="onHitDamageText">与受击动画同帧显示伤害数字。</param>
        /// <param name="hitAnimation">配置了 <c>HeroConfig.hitAnimation</c> 时，与释放特效同帧给被打的敌人开播（替代命中瞬间的 ani_atk_lvXX_hit）。</param>
        public bool PlayHeroSkill(
            int visualSlot,
            int level,
            HeroEffectsConfig attackFx,
            HeroEffectsConfig hitFx,
            string hitAnimation,
            Func<bool> onHit,
            Action onHitDamageText,
            Action onCollisionDone,
            Action onReturned,
            Action onDone,
            float timeScale = 1f)
        {
            Kill();
            level = Mathf.Clamp(level, 1, 3);
            if (_playerRoot == null || visualSlot < 0 || visualSlot >= _enemyRoots.Length ||
                _enemyRoots[visualSlot] == null ||
                attackFx == null || hitFx == null ||
                !TryGetHeroFxPrefab(attackFx.Effects, out var attackPrefab) ||
                !TryGetHeroFxPrefab(hitFx.Effects, out var hitPrefab))
            {
                Debug.LogWarning(
                    $"[AttackCutscene] PlayHeroSkill skipped: slot={visualSlot} attack={attackFx?.Effects} hit={hitFx?.Effects}");
                return false;
            }

            var token = ++_playToken;
            BeginPlay(timeScale);
            _skillHitNoKnockback = true;
            var tuning = AttackTuningConfig.Instance;
            var beat = tuning.Level(level);
            var targetAnim = _enemyAnims[visualSlot];
            var homeUi = _playerRoot.position;
            var hitUi = _enemyRoots[visualSlot].position;
            var projectile = IsProjectile(attackFx);
            var rolePoint = IsRolePoint(attackFx);
            var configuredWait = Mathf.Max(0f, attackFx.Time);
            var castClip = projectile ? 0f : LongestClipLength(attackPrefab);
            var castTime = ResolveCastDuration(configuredWait, castClip, projectile, tuning);
            // 命中时刻：攻击行 hitTime = 释放开始后多久命中；没配就等释放动作演完再命中。
            var hitDelay = ResolveHitDelay(attackFx, castTime, projectile);
            // 命中特效显示时长：命中行 hitTime 优先，否则按命中行的 Time（207 = 1.2s）。
            var hitLife = ResolveHitLife(hitFx);
            // 整段动作时长：释放特效演完、命中特效也播完，谁长算谁。116|207 正好都是 1.5s。
            var actionEnd = Mathf.Max(castTime, hitDelay + hitLife);
            var tail = Mathf.Max(0f, actionEnd - hitDelay);
            var t0 = Time.realtimeSinceStartup;
            float Elapsed() => Time.realtimeSinceStartup - t0;

            PlayClip(_playerAnim, DefaultClip);

            _seq = DOTween.Sequence();
            _seq.timeScale = _playTimeScale;

            // hitAnimation：与释放特效同帧开播，命中帧不再重开（MISS 仍会打断成闪避）。
            var hitAnimAtCast = TryBeginHitAnimationAtCast(targetAnim, hitAnimation);

            AppLog.Info(
                LogChannel.UI,
                $"[HeroSkill][t={Elapsed():0.000}] 开始 slot={visualSlot} lv={level} " +
                $"attack={attackFx.Effects} attackTime={configuredWait:0.###} castTime={castTime:0.###} " +
                $"hitDelay={hitDelay:0.###} projectile={projectile} rolepoint={rolePoint} " +
                $"hit={hitFx.Effects} hitShow={hitLife:0.###} hitAnim={(hitAnimAtCast ? hitAnimation : "命中帧")} " +
                $"tail={tail:0.###} actionEnd={actionEnd:0.###} hpHold={tuning.HpTextHoldDuration:0.###}");

            // 释放特效：投射物飞向目标；非投射物按 rolepoint 落点，各自按自己的时长回池。
            // 命中早于释放结束时，释放特效照常演完，不再命中当帧掐断。
            if (projectile)
            {
                // 投射物飞到目标的那一刻就是命中，飞行时长即命中延迟。
                var castGo = SpawnSkillFx(attackPrefab, homeUi, life: hitDelay);
                AppLog.Info(
                    LogChannel.UI,
                    $"[HeroSkill][t={Elapsed():0.000}] 阶段1-释放(投射物) 生成={castGo != null} 飞行={Mathf.Max(0.01f, hitDelay):0.###}s");
                if (castGo != null)
                {
                    var hitFxPos = ResolveFxWorldPos(attackPrefab, hitUi);
                    FaceToward(castGo.transform, castGo.transform.position, hitFxPos);
                    var fly = Mathf.Max(0.01f, hitDelay);
                    _seq.Append(castGo.transform.DOMove(hitFxPos, fly).SetEase(Ease.InQuad));
                }
                else
                {
                    _seq.AppendInterval(hitDelay);
                }
            }
            else
            {
                var castUi = rolePoint ? homeUi : hitUi;
                SpawnSkillFx(attackPrefab, castUi, life: castTime);
                AppLog.Info(
                    LogChannel.UI,
                    $"[HeroSkill][t={Elapsed():0.000}] 阶段1-释放(定点) pos={(rolePoint ? "人物" : "目标")} " +
                    $"演完 castTime={castTime:0.###}s（配置 {configuredWait:0.###}，动画 {castClip:0.###}），" +
                    $"命中延迟={hitDelay:0.###}s");
                _seq.AppendInterval(hitDelay);
            }

            // 命中瞬间：结算 + 受击特效 + 伤害字（hitAnimation 已在释放时开播则不再重开）。
            _seq.AppendCallback(() =>
            {
                if (token != _playToken)
                {
                    return;
                }

                AppLog.Info(LogChannel.UI, $"[HeroSkill][t={Elapsed():0.000}] 阶段2-命中开始");
                var missed = PlayHeroSkillImpact(
                    targetAnim, level, hitAnimation, onHit, beat, homeUi, hitAnimAtCast);
                AppLog.Info(
                    LogChannel.UI,
                    $"[HeroSkill][t={Elapsed():0.000}] 阶段2a-结算完成 missed={missed}（受击动画={(missed ? "MISS" : (_victimHitClip ?? Clip(level, "hit")))}）");
                if (!missed)
                {
                    SpawnHitFx(hitPrefab, hitFx, homeUi, hitUi, hitLife);
                    AppLog.Info(
                        LogChannel.UI,
                        $"[HeroSkill][t={Elapsed():0.000}] 阶段2b-受击特效 hit={hitFx.Effects} show={hitLife:0.###}");
                }

                AppLog.Info(LogChannel.UI, $"[HeroSkill][t={Elapsed():0.000}] 阶段2c-出伤害字");
                onHitDamageText?.Invoke();
                AppLog.Info(LogChannel.UI, $"[HeroSkill][t={Elapsed():0.000}] 阶段2d-collisionDone");
                onCollisionDone?.Invoke();
                RestoreHitTarget();
                AppLog.Info(
                    LogChannel.UI,
                    $"[HeroSkill][t={Elapsed():0.000}] 阶段3-等特效演完 tail={tail:0.###}s（actionEnd={actionEnd:0.###}）");
            });
            _seq.AppendInterval(tail);
            _seq.AppendCallback(() =>
            {
                if (token != _playToken)
                {
                    return;
                }

                // 两条特效各自按自己的时长回池（释放 = castTime，命中 = hitDelay + hitLife），这里兜底清一次。
                ClearSkillFx();
                onReturned?.Invoke();
            });
            _seq.AppendInterval(tuning.HpTextHoldDuration);
            _seq.OnComplete(() =>
            {
                if (token != _playToken)
                {
                    return;
                }

                AppLog.Info(LogChannel.UI, $"[HeroSkill][t={Elapsed():0.000}] 阶段5-结束 onDone");
                SetAllAnimSpeed(1f);
                onDone?.Invoke();
            });
            return true;
        }

        /// <summary>
        /// 暗灵法师的齐射出刀（特殊处理，别的英雄不走这里）：
        /// 01 持续 1.5s → 02 在人物处蓄力 1.0s → 主弹直线飞向主目标 0.3s，同时两侧每个敌人各飞一发 03（0.4s 抛物线）
        /// → 主弹 1.3s 落地统一结算（扣血/受击动画/伤害字）+ 04，溅射弹 1.4s 各自落地只补 04。
        /// 时间与抛物线参数读 <see cref="AttackTuningConfig"/>「暗灵法师齐射」段，不读特效表 Time；档位对这套不生效。
        /// </summary>
        public bool PlayVolleySkill(
            int visualSlot,
            int level,
            HeroEffectsConfig channelFx,
            HeroEffectsConfig mainFx,
            HeroEffectsConfig splashFx,
            HeroEffectsConfig hitFx,
            IList<int> splashSlots,
            string hitAnimation,
            Func<bool> onHit,
            Action onHitDamageText,
            Action onCollisionDone,
            Action onReturned,
            Action onDone,
            float timeScale = 1f)
        {
            Kill();
            level = Mathf.Clamp(level, 1, 3);
            if (_playerRoot == null || visualSlot < 0 || visualSlot >= _enemyRoots.Length ||
                _enemyRoots[visualSlot] == null ||
                channelFx == null || mainFx == null || hitFx == null ||
                !TryGetHeroFxPrefab(channelFx.Effects, out var channelPrefab) ||
                !TryGetHeroFxPrefab(mainFx.Effects, out var mainPrefab) ||
                !TryGetHeroFxPrefab(hitFx.Effects, out var hitPrefab))
            {
                Debug.LogWarning(
                    $"[AttackCutscene] PlayVolleySkill skipped: slot={visualSlot} " +
                    $"channel={channelFx?.Effects} main={mainFx?.Effects} hit={hitFx?.Effects}");
                return false;
            }

            // 溅射弹是可选的：没配 03、没预载或场上没有别的敌人时只打主目标。
            GameObject splashPrefab = null;
            if (splashSlots != null && splashSlots.Count > 0 && splashFx != null)
            {
                TryGetHeroFxPrefab(splashFx.Effects, out splashPrefab);
            }

            var token = ++_playToken;
            BeginPlay(timeScale);
            _skillHitNoKnockback = true;
            var tuning = AttackTuningConfig.Instance;
            var beat = tuning.Level(level);
            var targetAnim = _enemyAnims[visualSlot];
            var homeUi = _playerRoot.position;
            var hitUi = _enemyRoots[visualSlot].position;
            var hitLife = ResolveHitLife(hitFx);
            var channelTime = tuning.VolleyChannelTime;
            var chargeTime = tuning.VolleyChargeTime;
            var mainFlight = tuning.VolleyMainFlightTime;
            var splashFlight = tuning.VolleySplashFlightTime;
            var splashDelay = tuning.VolleySpawnDelay;
            var spawnOffsetY = tuning.VolleySpawnOffsetY;
            var fxScale = tuning.VolleyFxScale;
            var hitDelay = chargeTime + mainFlight;
            // 03 比主弹晚 splashDelay 再生成，落地也跟着后移。
            var splashEnd = splashPrefab != null
                ? chargeTime + splashDelay + splashFlight + hitLife
                : 0f;
            var actionEnd = Mathf.Max(channelTime, Mathf.Max(hitDelay + hitLife, splashEnd));
            var tail = Mathf.Max(0f, actionEnd - hitDelay);
            var t0 = Time.realtimeSinceStartup;
            float Elapsed() => Time.realtimeSinceStartup - t0;

            // 溅射弹 03：主弹起飞后再晚 splashDelay，从人物处贝塞尔飞向两侧，落地补 04。
            void LaunchSplash()
            {
                if (splashPrefab == null || splashSlots == null)
                {
                    return;
                }

                for (var i = 0; i < splashSlots.Count; i++)
                {
                    var slot = splashSlots[i];
                    if (slot < 0 || slot >= _enemyRoots.Length || _enemyRoots[slot] == null)
                    {
                        continue;
                    }

                    var slotUi = _enemyRoots[slot].position;
                    // 齐射特效统一强制 UI 层，落点直接用 UI 世界坐标（不转主相机平面）。
                    var dest = slotUi;
                    var splashOrigin = homeUi + new Vector3(0f, spawnOffsetY, 0f);
                    var go = SpawnSkillFxAt(
                        splashPrefab,
                        splashOrigin,
                        life: 0f,
                        forceUiLayer: true,
                        scaleMultiplier: fxScale,
                        sortingOrder: HeroFxSortingOrder);
                    if (go != null)
                    {
                        var jumpTween = CreateVolleySplashTween(go.transform, dest, tuning);
                        if (jumpTween != null)
                        {
                            jumpTween.timeScale = _playTimeScale;
                        }
                    }

                    // 落地计时不挂在弹丸 tween 上：弹丸到点就回池，tween 可能已经被清掉。
                    var landingGo = go;
                    var landingUi = slotUi;
                    var landingSlot = slot;
                    var landing = DOVirtual.DelayedCall(splashFlight, () =>
                    {
                        if (token != _playToken)
                        {
                            return;
                        }

                        if (landingGo != null)
                        {
                            ClearSkillFx(landingGo);
                        }

                        SpawnVolleyHitFx(hitPrefab, hitFx, homeUi, landingUi, hitLife, fxScale);
                        AppLog.Info(
                            LogChannel.UI,
                            $"[HeroVolley][t={Elapsed():0.000}] 阶段3b-溅射命中 slot={landingSlot} hit={hitFx.Effects}");
                    }, false);
                    landing.timeScale = _playTimeScale;
                }
            }

            PlayClip(_playerAnim, DefaultClip);

            _seq = DOTween.Sequence();
            _seq.timeScale = _playTimeScale;

            // hitAnimation：与齐射起手（01/02）同帧开播，主弹落地不再重开。
            var hitAnimAtCast = TryBeginHitAnimationAtCast(targetAnim, hitAnimation);

            AppLog.Info(
                LogChannel.UI,
                $"[HeroVolley][t={Elapsed():0.000}] 开始 slot={visualSlot} lv={level} " +
                $"channel={channelFx.Effects}({channelTime:0.###}s) main={mainFx.Effects}" +
                $"({chargeTime:0.###}+{mainFlight:0.###}s) " +
                $"splash={splashFx?.Effects}({splashSlots?.Count ?? 0}发 delay={splashDelay:0.###}+{splashFlight:0.###}s) " +
                $"hit={hitFx.Effects} hitShow={hitLife:0.###} hitAnim={(hitAnimAtCast ? hitAnimation : "命中帧")} " +
                $"actionEnd={actionEnd:0.###}");

            // 阶段1：01 法阵在人物处持续播放；02 也在人物处，先演蓄力不动（都不转、都拉到 UI 层）。
            var spawnOrigin = (IsRolePoint(channelFx) ? homeUi : hitUi) + new Vector3(0f, spawnOffsetY, 0f);
            var mainOrigin = (IsRolePoint(mainFx) ? homeUi : hitUi) + new Vector3(0f, spawnOffsetY, 0f);
            var channelGo = SpawnSkillFxAt(
                channelPrefab,
                spawnOrigin,
                channelTime,
                forceUiLayer: true,
                scaleMultiplier: fxScale,
                sortingOrder: HeroFxSortingOrder);
            LogVolleyFxState("法阵01", channelGo);
            var mainGo = SpawnSkillFxAt(
                mainPrefab,
                mainOrigin,
                life: 0f,
                forceUiLayer: true,
                scaleMultiplier: fxScale,
                sortingOrder: HeroFxSortingOrder);
            var mainDest = hitUi;

            // 阶段2：蓄满 → 主弹起飞；03 再晚 splashDelay 生成。
            _seq.AppendInterval(chargeTime);
            _seq.AppendCallback(() =>
            {
                if (token != _playToken)
                {
                    return;
                }

                AppLog.Info(
                    LogChannel.UI,
                    $"[HeroVolley][t={Elapsed():0.000}] 阶段2-发射 主弹={mainFx.Effects} " +
                    $"溅射={splashSlots?.Count ?? 0}发（晚 {splashDelay:0.###}s）");
                if (splashDelay <= 0f)
                {
                    LaunchSplash();
                    return;
                }

                var splashStart = DOVirtual.DelayedCall(splashDelay, () =>
                {
                    if (token != _playToken)
                    {
                        return;
                    }

                    LaunchSplash();
                }, false);
                splashStart.timeScale = _playTimeScale;
            });
            if (mainGo != null)
            {
                _seq.Append(mainGo.transform.DOMove(mainDest, mainFlight).SetEase(Ease.Linear));
            }
            else
            {
                _seq.AppendInterval(mainFlight);
            }

            // 阶段3：主弹落地，统一结算 + 出 04。
            _seq.AppendCallback(() =>
            {
                if (token != _playToken)
                {
                    return;
                }

                if (mainGo != null)
                {
                    ClearSkillFx(mainGo);
                }

                AppLog.Info(LogChannel.UI, $"[HeroVolley][t={Elapsed():0.000}] 阶段3-主弹命中");
                var missed = PlayHeroSkillImpact(
                    targetAnim, level, hitAnimation, onHit, beat, homeUi, hitAnimAtCast);
                AppLog.Info(
                    LogChannel.UI,
                    $"[HeroVolley][t={Elapsed():0.000}] 阶段3a-结算完成 missed={missed}（受击动画={(missed ? "MISS" : (_victimHitClip ?? Clip(level, "hit")))}）");
                if (!missed)
                {
                    SpawnVolleyHitFx(hitPrefab, hitFx, homeUi, hitUi, hitLife, fxScale);
                }

                onHitDamageText?.Invoke();
                onCollisionDone?.Invoke();
                RestoreHitTarget();
                AppLog.Info(
                    LogChannel.UI,
                    $"[HeroVolley][t={Elapsed():0.000}] 阶段4-等最后一发 04 播完 tail={tail:0.###}s（actionEnd={actionEnd:0.###}）");
            });
            _seq.AppendInterval(tail);
            _seq.AppendCallback(() =>
            {
                if (token != _playToken)
                {
                    return;
                }

                ClearSkillFx();
                onReturned?.Invoke();
            });
            _seq.AppendInterval(tuning.HpTextHoldDuration);
            _seq.OnComplete(() =>
            {
                if (token != _playToken)
                {
                    return;
                }

                AppLog.Info(LogChannel.UI, $"[HeroVolley][t={Elapsed():0.000}] 阶段5-结束 onDone");
                SetAllAnimSpeed(1f);
                onDone?.Invoke();
            });
            return true;
        }

        /// <summary>
        /// 进桌预载当前英雄用到的 Attack/Hit 预制体，各放一份进对象池（本局人物固定）。
        /// 弱/中/强三档 + 默认档全部预载，出刀按档位直接取；WebGL 禁止同步加载。
        /// </summary>
        public async Task PreloadHeroEffectsAsync(IResourceService resources, HeroConfig hero)
        {
            if (resources == null || hero == null)
            {
                return;
            }

            _disposed = false;
            _heroFxResources = resources;
            var names = CollectHeroFxNames(hero);
            if (names.Count == 0 && !HasHeroFxIds(hero))
            {
                // 这个英雄本来就没配特效（平凡之人 / 旅者），静默走近战。
                return;
            }

            for (var i = 0; i < names.Count; i++)
            {
                await PreloadHeroFxPrefabAsync(resources, names[i]);
                if (_disposed)
                {
                    return;
                }
            }

            LogHeroFxTiers(hero, names.Count);
        }

        private static bool HasHeroFxIds(HeroConfig hero)
        {
            return hero.CastEffect > 0 ||
                   HasIds(hero.HeroEffects) || HasIds(hero.SpecialEffect) ||
                   HasIds(hero.HeroEffectsCommon) || HasIds(hero.HeroEffectsElite) ||
                   HasIds(hero.HeroEffectsWeak) || HasIds(hero.HeroEffectsMid) || HasIds(hero.HeroEffectsStrong) ||
                   HasIds(hero.MissAttackEffect);
        }

        private static bool HasIds(int[] ids) => ids != null && ids.Length > 0;

        /// <summary>
        /// 默认档、CastEffect/SpecialEffect（暗灵法师齐射）、目标类型（普通/精英）、
        /// 弱/中/强三档、闪避反击里出现过的特效预制体名（去重，保持出现顺序）。
        /// </summary>
        private static List<string> CollectHeroFxNames(HeroConfig hero)
        {
            var names = new List<string>(8);
            AddHeroFxName(hero.CastEffect, names);
            AddHeroFxNames(hero.HeroEffects, names);
            AddHeroFxNames(hero.SpecialEffect, names);
            AddHeroFxNames(hero.HeroEffectsCommon, names);
            AddHeroFxNames(hero.HeroEffectsElite, names);
            AddHeroFxNames(hero.HeroEffectsWeak, names);
            AddHeroFxNames(hero.HeroEffectsMid, names);
            AddHeroFxNames(hero.HeroEffectsStrong, names);
            AddHeroFxNames(hero.MissAttackEffect, names);
            return names;
        }

        private static void AddHeroFxNames(int[] effectIds, List<string> names)
        {
            if (effectIds == null)
            {
                return;
            }

            for (var i = 0; i < effectIds.Length; i++)
            {
                AddHeroFxName(effectIds[i], names);
            }
        }

        private static void AddHeroFxName(int effectId, List<string> names)
        {
            if (effectId <= 0)
            {
                return;
            }

            var fx = HeroEffectsConfig.Get(effectId);
            if (fx == null || string.IsNullOrEmpty(fx.Effects) || names.Contains(fx.Effects))
            {
                return;
            }

            names.Add(fx.Effects);
        }

        /// <summary>逐档报告解析/预载结果，缺 Hit、只配了一种目标类型之类一眼能看出来。</summary>
        private void LogHeroFxTiers(HeroConfig hero, int preloaded)
        {
            // 暗灵法师走齐射，不按弱/中/强单条 Attack+Hit 报。
            if (HeroMechanics.TryResolveVolleyEffects(hero, out var channel, out var main, out var splash, out var volleyHit) &&
                TryGetHeroFxPrefab(channel.Effects, out _) &&
                TryGetHeroFxPrefab(main.Effects, out _) &&
                TryGetHeroFxPrefab(splash.Effects, out _) &&
                TryGetHeroFxPrefab(volleyHit.Effects, out _))
            {
                AppLog.Info(
                    LogChannel.Assets,
                    $"[AttackCutscene] 英雄特效已预载（{preloaded} 份）hero={hero.Name} " +
                    $"齐射={channel.Effects}/{main.Effects}/{splash.Effects}/{volleyHit.Effects}");
                return;
            }

            var parts = new List<string>(5);
            var ready = 0;
            for (var level = 1; level <= 3; level++)
            {
                var label = level == 1 ? "弱" : level == 2 ? "中" : "强";
                if (HeroMechanics.TryResolveEffects(hero, level, out var attack, out var hit) &&
                    CanPlayHeroSkill(attack, hit))
                {
                    ready++;
                    parts.Add($"{label}={attack.Effects}/{hit.Effects}");
                }
                else
                {
                    parts.Add($"{label}=未就绪");
                }
            }

            if (HasIds(hero.HeroEffectsCommon))
            {
                parts.Add($"普通={JoinIds(hero.HeroEffectsCommon)}");
            }

            if (HasIds(hero.HeroEffectsElite))
            {
                parts.Add($"精英={JoinIds(hero.HeroEffectsElite)}");
            }

            var summary = string.Join(" ", parts);
            if (ready > 0 || HasIds(hero.HeroEffectsCommon) || HasIds(hero.HeroEffectsElite))
            {
                AppLog.Info(
                    LogChannel.Assets,
                    $"[AttackCutscene] 英雄特效已预载（{preloaded} 份）hero={hero.Name} {summary}");
                return;
            }

            // 常见原因：HeroConfig.HeroEffects 里全是 Attack、缺 Hit（或反之），出刀会静默回退近战。
            AppLog.Warn(
                LogChannel.Assets,
                $"[AttackCutscene] 英雄特效未解析，出刀回退近战：hero={hero.Name} {summary} " +
                $"effects={(hero.HeroEffects != null ? string.Join(",", hero.HeroEffects) : "空")} " +
                $"cast={hero.CastEffect} special={JoinIds(hero.SpecialEffect)}（需要 Attack + Hit 各一条；暗灵法师要 CastEffect+主弹+溅射+Hit）");
        }

        private static string JoinIds(int[] ids) => ids == null ? "空" : string.Join(",", ids);

        public bool CanPlayHeroSkill(HeroEffectsConfig attackFx, HeroEffectsConfig hitFx)
        {
            return attackFx != null &&
                   hitFx != null &&
                   TryGetHeroFxPrefab(attackFx.Effects, out _) &&
                   TryGetHeroFxPrefab(hitFx.Effects, out _);
        }

        /// <summary>
        /// 在被打者当前位置补一个致死特效。敌人致死由 GameUI 在碰撞完成（命中定格结束）时调用。
        /// 不进攻击序列的时间轴，自己按配置时长计时销毁。
        /// </summary>
        public void PlayDeathEffect(Vector3 worldPos)
        {
            var tuning = AttackTuningConfig.Instance;
            var prefab = tuning.DeathEffect;
            if (prefab == null || _hud == null || worldPos == Vector3.zero)
            {
                return;
            }

            ClearDeathEffect();
            var go = UnityEngine.Object.Instantiate(prefab, _hud, false);
            go.transform.SetAsLastSibling();
            go.transform.position = worldPos;
            go.transform.localRotation = Quaternion.identity;
            go.SetActive(true);
            UiFx.ApplySorting(go, DeathFxSortingOrder);
            UiFx.RestartParticles(go);
            UiFx.ClearTrails(go);
            _deathFx = go;

            var life = tuning.DeathEffectDuration;
            if (life <= 0f)
            {
                return;
            }

            DOVirtual.DelayedCall(life, () => ClearDeathEffect(go), false).SetLink(go);
        }

        public void Dispose()
        {
            Kill();
            RestoreRoot(_playerHome);
            RestoreIncoming();
            RestoreHitTarget();
            PlayClip(_playerAnim, DefaultClip);
            DestroyFlight();
            ReleaseHeroFxPrefabs();
            _disposed = true;
            if (_impactSfx != null && AppServices.IsReady)
            {
                AppServices.Resolve<IResourceService>().Release(ResResourcePaths.SfxHurtBig02);
            }

            _impactSfx = null;
        }

        private async Task PreloadImpactSfxAsync()
        {
            if (_impactSfx != null || !AppServices.IsReady)
            {
                return;
            }

            try
            {
                var resources = AppServices.Resolve<IResourceService>();
                var clip = await resources.LoadAsync<AudioClip>(ResResourcePaths.SfxHurtBig02);
                if (_disposed)
                {
                    // await 期间已退局：立即释放，避免计数泄漏
                    resources.Release(ResResourcePaths.SfxHurtBig02);
                    return;
                }

                _impactSfx = clip;
            }
            catch (Exception ex)
            {
                AppLog.Warn(LogChannel.Assets, "Impact SFX load failed: " + ex.Message);
            }
        }

        private void PlayImpactSfx()
        {
            if (_impactSfx == null || !AppServices.IsReady)
            {
                return;
            }

            AppServices.Resolve<IAudioService>().PlaySfx(_impactSfx);
        }

        private void BindPlayer(PlayerItem player)
        {
            _playerRoot = player != null ? player.RootRect : null;
            _playerAnim = player != null ? player.RootAnimator : null;
            _playerCardRect = player != null ? player.GetComponent<RectTransform>() : null;
            if (_playerRoot != null)
            {
                _playerHome = _playerRoot.parent;
                _playerHomeAnchored = _playerRoot.anchoredPosition;
            }
            else
            {
                _playerHome = null;
            }
        }

        private void BindEnemy(int index, PlayerItem enemy)
        {
            if (enemy == null)
            {
                return;
            }

            _enemyRoots[index] = enemy.RootRect;
            _enemyAnims[index] = enemy.RootAnimator;
            _enemyCardRects[index] = enemy.GetComponent<RectTransform>();
        }

        private void AttachRootToFlight(RectTransform root, Vector3 worldPos)
        {
            if (root == null)
            {
                return;
            }

            var homeParent = root.parent;
            var flight = EnsureFlight();
            flight.gameObject.SetActive(true);
            flight.SetParent(_hud, false);
            flight.SetAsLastSibling();
            CopyRelativeScale(flight, homeParent);
            flight.localRotation = Quaternion.identity;
            flight.anchoredPosition = WorldToHudAnchored(worldPos);
            root.SetParent(flight, true);
            root.localScale = Vector3.one;
        }

        private static void CopyRelativeScale(Transform target, Transform worldSource)
        {
            if (target == null)
            {
                return;
            }

            if (target.parent == null || worldSource == null)
            {
                target.localScale = Vector3.one;
                return;
            }

            var parentLossy = target.parent.lossyScale;
            var sourceLossy = worldSource.lossyScale;
            target.localScale = new Vector3(
                DivideScale(sourceLossy.x, parentLossy.x),
                DivideScale(sourceLossy.y, parentLossy.y),
                DivideScale(sourceLossy.z, parentLossy.z));
        }

        private static float DivideScale(float value, float parent)
        {
            return Mathf.Abs(parent) < 0.0001f ? 1f : value / parent;
        }

        private void RestoreIncoming()
        {
            if (_incomingRoot == null || _incomingHome == null)
            {
                _incomingRoot = null;
                _incomingHome = null;
                return;
            }

            _incomingRoot.SetParent(_incomingHome, false);
            _incomingRoot.anchoredPosition = _incomingHomeAnchored;
            _incomingRoot.localScale = Vector3.one;
            _incomingRoot.localRotation = Quaternion.identity;
            _incomingRoot = null;
            _incomingHome = null;

            if (_flight != null && (_playerRoot == null || _playerRoot.parent != _flight))
            {
                _flight.gameObject.SetActive(false);
            }
        }

        /// <summary>受击卡回到原位。位移做在卡根节点上，不动父子关系。只 Kill 击退 Sequence，不动同卡其它 tween。</summary>
        private void RestoreHitTarget()
        {
            for (var i = 0; i < _knockRects.Count; i++)
            {
                var rect = _knockRects[i];
                if (i < _knockSeqs.Count)
                {
                    var seq = _knockSeqs[i];
                    if (seq != null && seq.IsActive())
                    {
                        seq.Kill();
                    }
                }

                if (rect != null)
                {
                    rect.anchoredPosition = _knockHomes[i];
                }
            }

            _knockRects.Clear();
            _knockHomes.Clear();
            _knockSeqs.Clear();
        }

        /// <summary>
        /// 命中瞬间：先结算扣血（由此得知是否闪避），再播受击或 miss。
        /// 闪避不击退；受击方自己等 miss 播完再回 default，攻击方仍按 HitHold 后撤。
        /// </summary>
        /// <returns>是否闪避（MISS）。</returns>
        private bool PlayImpact(
            Animator attacker,
            Animator victim,
            int level,
            Func<bool> onHit,
            AttackTuningConfig.LevelTuning beat,
            RectTransform hitRect,
            Vector3 attackerWorldPos,
            Vector3 targetWorldPos)
        {
            _impactLevel = level;
            _impactBeat = beat;
            _impactAttackerPos = attackerWorldPos;
            _victimHitClip = null;
            PlayImpactSfx();
            var missed = onHit != null && onHit();
            PlayClip(attacker, Clip(level, "end"));
            if (missed)
            {
                PlayClip(victim, MissClip);
                HoldMissUntilDone(victim);
                return true;
            }

            PlayClip(victim, Clip(level, "hit"));
            InsertHitKnockback(beat, hitRect, attackerWorldPos, targetWorldPos);
            return false;
        }

        /// <summary>
        /// 释放开始时开播 <see cref="HeroConfig.hitAnimation"/>。
        /// 片段在控制器里找不到时返回 false，命中帧再走通用 hit。
        /// </summary>
        private bool TryBeginHitAnimationAtCast(Animator victim, string hitAnimation)
        {
            if (victim == null || string.IsNullOrEmpty(hitAnimation))
            {
                return false;
            }

            if (ClipLength(victim, hitAnimation) <= 0f)
            {
                AppLog.Warn(
                    LogChannel.UI,
                    $"[AttackCutscene] hitAnimation 缺失 anim={hitAnimation}，改在命中帧播通用 hit");
                return false;
            }

            _victimHitClip = hitAnimation;
            PlayClip(victim, hitAnimation);
            HoldVictimClipUntilDone(victim, hitAnimation);
            return true;
        }

        /// <summary>
        /// 特效攻击命中：结算；MISS 播闪避；未在释放时开播的受击动画在这一帧播（与特效/伤害字同帧）。无击退。
        /// </summary>
        /// <param name="hitAnimStartedAtCast">释放时已播过 hitAnimation 时为 true，命中帧不再重开。</param>
        private bool PlayHeroSkillImpact(
            Animator victim,
            int level,
            string hitAnimation,
            Func<bool> onHit,
            AttackTuningConfig.LevelTuning beat,
            Vector3 attackerWorldPos,
            bool hitAnimStartedAtCast = false)
        {
            _impactLevel = level;
            _impactBeat = beat;
            _impactAttackerPos = attackerWorldPos;
            if (!hitAnimStartedAtCast)
            {
                _victimHitClip = null;
            }

            PlayImpactSfx();
            var missed = onHit != null && onHit();
            if (missed)
            {
                PlayClip(victim, MissClip);
                HoldVictimClipUntilDone(victim, MissClip);
                return true;
            }

            // 释放时已开播 hitAnimation：只保留标记给溅射 ReactSlot，不再 Play 以免从头重来。
            if (hitAnimStartedAtCast && !string.IsNullOrEmpty(hitAnimation))
            {
                _victimHitClip = hitAnimation;
                return false;
            }

            // HeroConfig.hitAnimation 配了就用它，否则用按牌型档位的通用受击片段。
            _victimHitClip = string.IsNullOrEmpty(hitAnimation) ? null : hitAnimation;
            var hitClip = _victimHitClip ?? Clip(level, "hit");
            PlayClip(victim, hitClip);
            HoldVictimClipUntilDone(victim, hitClip);
            return false;
        }

        private void HoldMissUntilDone(Animator victim)
        {
            HoldVictimClipUntilDone(victim, MissClip);
        }

        /// <summary>受击/miss 播完前不要被 PlayVictimIdle 打断。</summary>
        private void HoldVictimClipUntilDone(Animator victim, string clipName)
        {
            if (victim == null || string.IsNullOrEmpty(clipName))
            {
                return;
            }

            var length = ClipLength(victim, clipName);
            if (length <= 0f)
            {
                return;
            }

            if (!_missVictims.Contains(victim))
            {
                _missVictims.Add(victim);
            }

            var token = _playToken;
            var tween = DOVirtual.DelayedCall(length, () =>
            {
                if (token != _playToken)
                {
                    return;
                }

                PlayClip(victim, DefaultClip);
                _missVictims.Remove(victim);
            }, false);
            tween.timeScale = _playTimeScale;
            tween.SetLink(victim.gameObject);
            _missHoldTweens.Add(tween);
        }

        private void PlayVictimIdle(Animator victim)
        {
            if (victim == null || _missVictims.Contains(victim))
            {
                return;
            }

            PlayClip(victim, DefaultClip);
        }

        private void ClearMissHold(bool restoreIdle)
        {
            for (var i = 0; i < _missHoldTweens.Count; i++)
            {
                _missHoldTweens[i]?.Kill();
            }

            _missHoldTweens.Clear();
            if (restoreIdle)
            {
                for (var i = 0; i < _missVictims.Count; i++)
                {
                    PlayClip(_missVictims[i], DefaultClip);
                }
            }

            _missVictims.Clear();
        }

        private void ClearDeathEffect()
        {
            ClearDeathEffect(_deathFx);
        }

        private void ClearDeathEffect(GameObject go)
        {
            if (go == null)
            {
                return;
            }

            if (_deathFx == go)
            {
                _deathFx = null;
            }

            go.transform.DOKill();
            UnityEngine.Object.Destroy(go);
        }

        /// <summary>
        /// 命中时刻：攻击行的 <c>hitTime</c> 是「释放特效开始后多久命中」，0 表示释放动作演完那一刻命中。
        /// 例：萨满 116 释放 1.5s、命中 0.3s → 116 从头演到 1.5s，0.3s 时命中并在目标处出 207。
        /// 投射物按飞行时间命中，读 hitTime 没有意义，直接按释放时长。
        /// </summary>
        private static float ResolveHitDelay(HeroEffectsConfig attackFx, float castTime, bool projectile)
        {
            var cast = Mathf.Max(0f, castTime);
            if (projectile || attackFx == null || attackFx.hitTime <= 0f)
            {
                return cast;
            }

            return Mathf.Clamp(attackFx.hitTime, 0f, cast);
        }

        /// <summary>
        /// 命中特效显示时长。命中行的 <c>hitTime</c> 优先，否则用命中行 Time 作为回收时长。
        /// 投射物至少飞完 Time 再收回。
        /// </summary>
        private static float ResolveHitLife(HeroEffectsConfig hitFx)
        {
            if (hitFx == null)
            {
                return 0f;
            }

            var recycle = Mathf.Max(0f, hitFx.Time);
            var life = hitFx.hitTime > 0f ? hitFx.hitTime : recycle;
            return IsProjectile(hitFx) ? Mathf.Max(life, recycle) : life;
        }

        /// <summary>
        /// 释放特效的等待（动作）时长。非投射物的动作由它自己的动画编排决定：
        /// 动画比配置的 <c>Time</c> 短时按动画收，否则会出现「动画已经演完、命中特效还晚半拍才开」。
        /// 投射物按 <c>Time</c> 飞，飞行本身就对位，不截断。
        /// </summary>
        private static float ResolveCastDuration(
            float configured,
            float clipLength,
            bool projectile,
            AttackTuningConfig tuning)
        {
            if (projectile || configured <= 0f || tuning == null || !tuning.ClampCastTimeToClip)
            {
                return configured;
            }

            return clipLength > 0f && clipLength < configured ? clipLength : configured;
        }

        /// <summary>预制体上所有 Animator 片段的最长时长；没有 Animator 或片段为空时返回 0。</summary>
        private static float LongestClipLength(GameObject prefab)
        {
            if (prefab == null)
            {
                return 0f;
            }

            var longest = 0f;
            var animators = prefab.GetComponentsInChildren<Animator>(true);
            for (var i = 0; i < animators.Length; i++)
            {
                var controller = animators[i] != null ? animators[i].runtimeAnimatorController : null;
                if (controller == null)
                {
                    continue;
                }

                var clips = controller.animationClips;
                for (var j = 0; j < clips.Length; j++)
                {
                    var clip = clips[j];
                    if (clip != null && clip.length > longest)
                    {
                        longest = clip.length;
                    }
                }
            }

            return longest;
        }

        private void SpawnHitFx(
            GameObject prefab,
            HeroEffectsConfig hitFx,
            Vector3 attackerUi,
            Vector3 targetUi,
            float life)
        {
            if (prefab == null || hitFx == null)
            {
                return;
            }

            if (IsProjectile(hitFx))
            {
                var go = SpawnSkillFx(prefab, attackerUi, life);
                if (go == null)
                {
                    return;
                }

                var targetFx = ResolveFxWorldPos(prefab, targetUi);
                FaceToward(go.transform, go.transform.position, targetFx);
                var fly = Mathf.Max(0.01f, hitFx.Time);
                var tween = go.transform.DOMove(targetFx, fly).SetEase(Ease.InQuad);
                tween.timeScale = _playTimeScale;
                return;
            }

            // 非投射物：rolepoint=1 落在人物点，否则落在目标，显示满自身的 hitTime（否则 Time）后回池。
            var spawnUi = IsRolePoint(hitFx) ? attackerUi : targetUi;
            SpawnSkillFx(prefab, spawnUi, life);
        }

        /// <summary>
        /// 齐射用的受击特效：强制 UI 层 + 齐射缩放，位置按 rolepoint 落在目标或人物。
        /// 不用 <see cref="SpawnHitFx"/> 是因为那条路径会被投射物分支带到主相机平面。
        /// </summary>
        private void SpawnVolleyHitFx(
            GameObject prefab,
            HeroEffectsConfig hitFx,
            Vector3 attackerUi,
            Vector3 targetUi,
            float life,
            float fxScale)
        {
            if (prefab == null || hitFx == null)
            {
                return;
            }

            var spawnUi = IsRolePoint(hitFx) ? attackerUi : targetUi;
            SpawnSkillFxAt(
                prefab,
                spawnUi,
                life,
                forceUiLayer: true,
                scaleMultiplier: fxScale,
                sortingOrder: HeroFxSortingOrder);
        }

        /// <summary>
        /// 溅射弹 03：三次贝塞尔（参数在 <see cref="AttackTuningConfig"/>「暗灵法师齐射」段）。
        /// 形状像先反向勾一笔再拐向怪（S/J 钩）：
        /// P0 起点 → P1 反方向勾住（右怪先往左、左怪先往右）→ P2 抬起拐向怪 → P3 怪物。
        /// </summary>
        private static Tween CreateVolleySplashTween(Transform t, Vector3 dest, AttackTuningConfig tuning)
        {
            if (t == null || tuning == null)
            {
                return null;
            }

            var p0 = t.position;
            var jump = Mathf.Max(
                tuning.VolleyMinJumpPower,
                Vector3.Distance(p0, dest) * tuning.VolleyJumpHeightRatio);

            var dx = dest.x - p0.x;
            var dy = dest.y - p0.y;
            // 相对「飞向怪」的反向：右怪先往左勾、左怪先往右勾，两边镜像。
            var reverseSign = Mathf.Abs(dx) > 0.001f ? -Mathf.Sign(dx) : 1f;
            var reverseAmp = Mathf.Max(
                tuning.VolleyJumpReverseXMin,
                Mathf.Abs(dx) * tuning.VolleyJumpReverseXRatio);

            // P1：反方向勾——明显甩到对面，Y 略低于起点，一出来就先反着弯（如图）。
            // ReverseXAt 越大，勾得越“沉”（更往下拉）。
            var hookDown = jump * Mathf.Clamp01(tuning.VolleyJumpReverseXAt);
            var p1 = new Vector3(
                p0.x + reverseSign * reverseAmp,
                p0.y - hookDown * 0.35f,
                p0.z);

            // P2：拐回来往怪飞——X 过中线靠向怪，Y 抬高成上升弧。
            // XZeroAt 控制 P2 沿「起点→怪」靠得多远（默认约 0.55~0.7 比较像图）。
            var p2Along = Mathf.Clamp(tuning.VolleyJumpXZeroAt, 0.2f, 0.9f);
            var p2 = new Vector3(
                Mathf.Lerp(p0.x, dest.x, p2Along),
                Mathf.Lerp(p0.y, dest.y, p2Along * 0.85f) + jump,
                Mathf.Lerp(p0.z, dest.z, p2Along));

            // 若水平距离很小（几乎正上方），仍保证有横向勾，避免退化成直线。
            if (Mathf.Abs(dx) < 0.01f && Mathf.Abs(dy) > 0.01f)
            {
                p1.x = p0.x + reverseAmp;
                p2.x = p0.x - reverseAmp * 0.25f;
            }

            var p3 = dest;
            var splashFlight = tuning.VolleySplashFlightTime;

            var p = 0f;
            return DOTween.To(
                    () => p,
                    v =>
                    {
                        p = v;
                        // B(t) = (1-t)³P0 + 3(1-t)²t P1 + 3(1-t)t² P2 + t³ P3
                        var u = 1f - v;
                        var uu = u * u;
                        var vv = v * v;
                        t.position = (uu * u) * p0
                                     + (3f * uu * v) * p1
                                     + (3f * u * vv) * p2
                                     + (vv * v) * p3;
                    },
                    1f,
                    splashFlight)
                .SetEase(Ease.Linear)
                .SetTarget(t);
        }

        /// <summary>
        /// 齐射特效的可见性自检：0.12 秒后打一条日志，报场上渲染器包围盒和粒子数。
        /// 看不见特效时看这条——包围盒为 0 / 位置在屏幕外 / 粒子数为 0，一眼能分清是没生成、
        /// 生成了但没粒子，还是位置或尺寸不对。
        /// </summary>
        private void LogVolleyFxState(string label, GameObject go)
        {
            if (go == null)
            {
                AppLog.Warn(LogChannel.UI, $"[HeroVolley] {label} 没生成（预制体或池子为空）");
                return;
            }

            var token = _playToken;
            var probe = DOVirtual.DelayedCall(0.12f, () =>
            {
                if (token != _playToken || go == null)
                {
                    return;
                }

                var renderers = go.GetComponentsInChildren<Renderer>(true);
                var bounds = new Bounds(go.transform.position, Vector3.zero);
                var hasBounds = false;
                for (var i = 0; i < renderers.Length; i++)
                {
                    var r = renderers[i];
                    if (r == null || !r.enabled)
                    {
                        continue;
                    }

                    if (!hasBounds)
                    {
                        bounds = r.bounds;
                        hasBounds = true;
                    }
                    else
                    {
                        bounds.Encapsulate(r.bounds);
                    }
                }

                var systems = go.GetComponentsInChildren<ParticleSystem>(true);
                var alive = 0;
                for (var i = 0; i < systems.Length; i++)
                {
                    if (systems[i] != null)
                    {
                        alive += systems[i].particleCount;
                    }
                }

                AppLog.Info(
                    LogChannel.UI,
                    $"[HeroVolley] {label} 自检 layer={go.layer} active={go.activeInHierarchy} " +
                    $"pos={go.transform.position} scale={go.transform.localScale.x:0.###} " +
                    $"renderers={renderers.Length} boundsCenter={(hasBounds ? bounds.center.ToString() : "无")} " +
                    $"boundsSize={(hasBounds ? bounds.size.ToString() : "无")} particles={alive}");
            }, false);
            probe.timeScale = _playTimeScale;
            probe.SetLink(go);
        }

        private static bool IsProjectile(HeroEffectsConfig fx)
        {
            return fx != null && fx.IsProjectile != 0;
        }

        private static bool IsRolePoint(HeroEffectsConfig fx)
        {
            return fx != null && fx.rolepoint != 0;
        }

        private static void FaceToward(Transform t, Vector3 from, Vector3 to)
        {
            if (t == null)
            {
                return;
            }

            var dir = to - from;
            if (dir.sqrMagnitude < 0.0001f)
            {
                return;
            }

            var angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
            t.rotation = Quaternion.Euler(0f, 0f, angle);
        }

        /// <param name="uiWorldPos">人物卡 RectTransform 世界坐标；内部按预制体 Layer 决定是否转到主相机平面。</param>
        private GameObject SpawnSkillFx(GameObject prefab, Vector3 uiWorldPos, float life)
        {
            return SpawnSkillFxAt(prefab, uiWorldPos, life, false, 1f, HeroFxSortingOrder);
        }

        /// <summary>
        /// 生成一个英雄特效实例。
        /// <paramref name="forceUiLayer"/> 为真时把整棵子树拉到 UI 层：有些特效预制体是 Default 层，
        /// 那样会被主相机画在 UI 后面（法阵就是这样「看不见」的），而且缩放走的是 1 倍、比 UI 那套大近一倍。
        /// </summary>
        private GameObject SpawnSkillFxAt(
            GameObject prefab,
            Vector3 uiWorldPos,
            float life,
            bool forceUiLayer,
            float scaleMultiplier,
            int sortingOrder)
        {
            if (prefab == null)
            {
                return null;
            }

            // 从预热池取出，保留预制体 Layer（UI / Default），除非调用方要求强制 UI 层。
            var go = RentHeroFx(prefab);
            if (go == null)
            {
                return null;
            }

            if (forceUiLayer)
            {
                UiFx.ApplyUiLayer(go);
            }

            var uiLayer = forceUiLayer || IsUiLayer(go);
            var fxWorldPos = uiLayer ? uiWorldPos : UiToFxWorld(uiWorldPos);
            var inherit = go.GetComponent<EffectSortingInherit>();
            go.transform.SetParent(null, false);
            go.transform.position = fxWorldPos;
            // 保持预制体原始朝向：这些特效的尾焰/剑光是按固定朝向做的，按飞行方向转会把它们拧歪。
            go.transform.rotation = Quaternion.identity;
            // UI 层由正交尺寸更小的 UI 相机画，按两台相机的可视高度把世界尺寸缩回原先的屏幕大小。
            var scale = ResolveFxScale(uiLayer);
            if (!Mathf.Approximately(scaleMultiplier, 1f))
            {
                scale *= Mathf.Max(0.01f, scaleMultiplier);
            }

            go.transform.localScale = scale;
            // 挂了 EffectSortingInherit：挂到 HUD 下走继承，不再强制写成 230（否则预制体 OrderOffset 改了也不生效）。
            // OrderOffset < 0 可压到人物卡下面；没挂 Inherit 的 UI 层特效仍统一 230 压在卡面之上。
            if (inherit != null && _hud != null)
            {
                go.transform.SetParent(_hud, true);
            }

            go.SetActive(true);
            if (inherit != null)
            {
                inherit.Refresh();
            }
            else if (uiLayer)
            {
                UiFx.ApplySorting(go, sortingOrder);
            }

            RestartSkillAnimators(go);
            UiFx.RestartParticles(go);
            UiFx.ClearTrails(go);
            _skillFxSpawned.Add(go);
            AppLog.Info(
                LogChannel.UI,
                $"[AttackCutscene] SpawnSkillFx name={go.name} layer={go.layer} pos={fxWorldPos} " +
                $"scale={scale.x:0.###} sorting={(inherit != null ? $"inherit(offset={inherit.OrderOffset})" : sortingOrder.ToString())} life={life}");

            if (life <= 0f)
            {
                return go;
            }

            var token = _playToken;
            var lifeTween = DOVirtual.DelayedCall(life, () =>
            {
                if (token != _playToken)
                {
                    return;
                }

                ClearSkillFx(go);
            }, false);
            lifeTween.timeScale = _playTimeScale;
            lifeTween.SetLink(go);
            return go;
        }

        /// <summary>
        /// UI Layer 特效与卡面同空间（直接用 UI 世界坐标，UI 相机可见）；
        /// Default 等其它 Layer 转到主相机 FX 平面（此前 Default 位置正确的那套）。
        /// </summary>
        private Vector3 ResolveFxWorldPos(GameObject prefab, Vector3 uiWorld)
        {
            if (IsUiLayer(prefab))
            {
                return uiWorld;
            }

            return UiToFxWorld(uiWorld);
        }

        /// <summary>
        /// Default 层仍在主相机平面，保持预制体缩放。
        /// UI 层同一世界尺寸会按 主相机正交尺寸/UI 相机正交尺寸 放大，这里乘上相反的比例。
        /// </summary>
        private Vector3 ResolveFxScale(bool uiLayer)
        {
            if (!uiLayer)
            {
                return Vector3.one;
            }

            var uiCam = ResolveUiCamera();
            var worldCam = Camera.main;
            if (uiCam == null || worldCam == null || !uiCam.orthographic || !worldCam.orthographic)
            {
                return Vector3.one;
            }

            if (worldCam.orthographicSize <= 0.001f)
            {
                return Vector3.one;
            }

            var scale = uiCam.orthographicSize / worldCam.orthographicSize;
            return new Vector3(scale, scale, scale);
        }

        private Camera ResolveUiCamera()
        {
            var canvas = _hudCanvas != null ? _hudCanvas.rootCanvas : _hudCanvas;
            if (canvas == null || canvas.renderMode == RenderMode.ScreenSpaceOverlay)
            {
                return null;
            }

            return canvas.worldCamera;
        }

        private static bool IsUiLayer(GameObject go)
        {
            if (go == null)
            {
                return false;
            }

            var ui = LayerMask.NameToLayer("UI");
            return ui >= 0 && go.layer == ui;
        }

        /// <summary>
        /// UI 卡世界坐标 → 屏幕 → 主相机前方 FX 平面世界坐标（给 Default Layer 用）。
        /// </summary>
        private Vector3 UiToFxWorld(Vector3 uiWorld)
        {
            var canvas = _hudCanvas != null ? _hudCanvas.rootCanvas : _hudCanvas;
            Camera uiCam = null;
            if (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay)
            {
                uiCam = canvas.worldCamera;
            }

            var screen = RectTransformUtility.WorldToScreenPoint(uiCam, uiWorld);
            var worldCam = Camera.main;
            if (worldCam == null)
            {
                return uiWorld;
            }

            var depth = Mathf.Abs(worldCam.transform.position.z - FxPlaneZ);
            if (depth < 0.01f)
            {
                depth = 10f;
            }

            return worldCam.ScreenToWorldPoint(new Vector3(screen.x, screen.y, depth));
        }

        private static void RestartSkillAnimators(GameObject go)
        {
            if (go == null)
            {
                return;
            }

            var animators = go.GetComponentsInChildren<Animator>(true);
            for (var i = 0; i < animators.Length; i++)
            {
                var anim = animators[i];
                if (anim == null || !anim.isActiveAndEnabled)
                {
                    continue;
                }

                anim.Rebind();
                anim.Update(0f);
                anim.Play(0, 0, 0f);
            }
        }

        private void ClearSkillFx()
        {
            for (var i = _skillFxSpawned.Count - 1; i >= 0; i--)
            {
                ClearSkillFx(_skillFxSpawned[i]);
            }

            _skillFxSpawned.Clear();
        }

        private void ClearSkillFx(GameObject go)
        {
            if (go == null)
            {
                return;
            }

            ReturnHeroFx(go);
        }

        private async Task PreloadHeroFxPrefabAsync(IResourceService resources, string effectsName)
        {
            var key = ResResourcePaths.HeroEffect(effectsName);
            if (resources == null || string.IsNullOrEmpty(key))
            {
                return;
            }

            if (_heroFxPrefabs.TryGetValue(key, out var loaded) && loaded != null)
            {
                WarmHeroFx(loaded);
                return;
            }

            if (resources.TryGetCached<GameObject>(key, out var cached) && cached != null)
            {
                _heroFxPrefabs[key] = cached;
                WarmHeroFx(cached);
                return;
            }

            try
            {
                var prefab = await resources.LoadAsync<GameObject>(key);
                if (_disposed)
                {
                    resources.Release(key);
                    return;
                }

                if (prefab == null)
                {
                    return;
                }

                _heroFxPrefabs[key] = prefab;
                if (!_heroFxOwnedKeys.Contains(key))
                {
                    _heroFxOwnedKeys.Add(key);
                }

                WarmHeroFx(prefab);
            }
            catch (Exception ex)
            {
                AppLog.Warn(LogChannel.Assets, $"Hero effect preload failed '{key}': {ex.Message}");
            }
        }

        private bool TryGetHeroFxPrefab(string effectsName, out GameObject prefab)
        {
            prefab = null;
            var key = ResResourcePaths.HeroEffect(effectsName);
            if (string.IsNullOrEmpty(key))
            {
                return false;
            }

            if (_heroFxPrefabs.TryGetValue(key, out prefab) && prefab != null)
            {
                return true;
            }

            if (_heroFxResources != null &&
                _heroFxResources.TryGetCached<GameObject>(key, out prefab) &&
                prefab != null)
            {
                _heroFxPrefabs[key] = prefab;
                return true;
            }

            prefab = null;
            return false;
        }

        private void ReleaseHeroFxPrefabs()
        {
            ClearSkillFx();
            if (_heroFxResources != null)
            {
                for (var i = 0; i < _heroFxOwnedKeys.Count; i++)
                {
                    _heroFxResources.Release(_heroFxOwnedKeys[i]);
                }
            }

            _heroFxOwnedKeys.Clear();
            _heroFxPrefabs.Clear();
            _heroFxResources = null;
            DestroyHeroFxPool();
        }

        private void WarmHeroFx(GameObject prefab)
        {
            if (prefab == null)
            {
                return;
            }

            if (!_heroFxIdle.TryGetValue(prefab, out var stack))
            {
                stack = new Stack<GameObject>(1);
                _heroFxIdle[prefab] = stack;
            }

            if (stack.Count > 0)
            {
                return;
            }

            var go = UnityEngine.Object.Instantiate(prefab, HeroFxPoolRoot(), false);
            go.name = prefab.name;
            go.SetActive(false);
            _heroFxOwner[go] = prefab;
            stack.Push(go);
        }

        private GameObject RentHeroFx(GameObject prefab)
        {
            if (prefab == null)
            {
                return null;
            }

            if (_heroFxIdle.TryGetValue(prefab, out var stack))
            {
                while (stack.Count > 0)
                {
                    var pooled = stack.Pop();
                    if (pooled == null)
                    {
                        continue;
                    }

                    pooled.transform.SetParent(null, false);
                    return pooled;
                }
            }

            var go = UnityEngine.Object.Instantiate(prefab);
            go.name = prefab.name;
            _heroFxOwner[go] = prefab;
            return go;
        }

        private void ReturnHeroFx(GameObject go)
        {
            if (go == null)
            {
                return;
            }

            _skillFxSpawned.Remove(go);
            go.transform.DOKill();
            if (!_heroFxOwner.TryGetValue(go, out var prefab) || prefab == null)
            {
                UnityEngine.Object.Destroy(go);
                return;
            }

            var systems = go.GetComponentsInChildren<ParticleSystem>(true);
            for (var i = 0; i < systems.Length; i++)
            {
                systems[i].Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }

            UiFx.ClearTrails(go);
            go.SetActive(false);
            go.transform.SetParent(HeroFxPoolRoot(), false);
            if (!_heroFxIdle.TryGetValue(prefab, out var stack))
            {
                stack = new Stack<GameObject>(1);
                _heroFxIdle[prefab] = stack;
            }

            stack.Push(go);
        }

        private Transform HeroFxPoolRoot()
        {
            if (_heroFxPoolRoot == null)
            {
                var root = new GameObject("HeroFxPool");
                root.SetActive(false);
                _heroFxPoolRoot = root.transform;
            }

            return _heroFxPoolRoot;
        }

        private void DestroyHeroFxPool()
        {
            foreach (var pair in _heroFxOwner)
            {
                if (pair.Key != null)
                {
                    UnityEngine.Object.Destroy(pair.Key);
                }
            }

            _heroFxOwner.Clear();
            _heroFxIdle.Clear();
            if (_heroFxPoolRoot != null)
            {
                UnityEngine.Object.Destroy(_heroFxPoolRoot.gameObject);
                _heroFxPoolRoot = null;
            }
        }

        private RectTransform EnsureFlight()
        {
            if (_flight != null)
            {
                return _flight;
            }

            var go = new GameObject("AttackFlight", typeof(RectTransform));
            _flight = go.GetComponent<RectTransform>();
            _flight.anchorMin = new Vector2(0.5f, 0.5f);
            _flight.anchorMax = new Vector2(0.5f, 0.5f);
            _flight.pivot = new Vector2(0.5f, 0.5f);
            _flight.sizeDelta = Vector2.zero;
            _flight.localScale = Vector3.one;
            _flight.localRotation = Quaternion.identity;
            return _flight;
        }

        private void RestoreRoot(Transform home)
        {
            if (_playerRoot == null || home == null)
            {
                return;
            }

            _playerRoot.SetParent(home, false);
            _playerRoot.anchoredPosition = _playerHomeAnchored;
            _playerRoot.localScale = Vector3.one;
            _playerRoot.localRotation = Quaternion.identity;

            if (_flight != null)
            {
                _flight.gameObject.SetActive(false);
            }
        }

        private void Kill()
        {
            _playToken++;
            _seq?.Kill();
            _seq = null;
            ClearMissHold(restoreIdle: true);
            RestoreRoot(_playerHome);
            RestoreIncoming();
            RestoreHitTarget();
            ClearDeathEffect();
            ClearSkillFx();
            SetAllAnimSpeed(1f);
            _playTimeScale = 1f;
            _skillHitNoKnockback = false;
            _victimHitClip = null;
        }

        private void BeginPlay(float timeScale)
        {
            _playTimeScale = Mathf.Max(0.01f, timeScale);
            SetAllAnimSpeed(_playTimeScale);
        }

        private void SetAllAnimSpeed(float speed)
        {
            SetAnimSpeed(_playerAnim, speed);
            for (var i = 0; i < _enemyAnims.Length; i++)
            {
                SetAnimSpeed(_enemyAnims[i], speed);
            }
        }

        private static void SetAnimSpeed(Animator animator, float speed)
        {
            if (animator != null)
            {
                animator.speed = speed;
            }
        }

        private void DestroyFlight()
        {
            if (_flight == null)
            {
                return;
            }

            UnityEngine.Object.Destroy(_flight.gameObject);
            _flight = null;
        }

        private void AppendAimAndRetreat(
            Animator attacker,
            int level,
            AttackTuningConfig.LevelTuning beat,
            Vector2 homeAnchored,
            Vector2 hitAnchored,
            bool invertAim)
        {
            PlayClip(attacker, Clip(level, "start"));
            var dir = hitAnchored - homeAnchored;
            var angle = AimAngleZ(dir);
            if (invertAim)
            {
                angle += 180f;
            }

            var aim = new Vector3(0f, 0f, angle);
            _seq.Append(_flight
                .DOAnchorPos(RetreatPoint(homeAnchored, dir, beat.RetreatDistance), beat.StartDuration)
                .SetEase(Ease.OutQuad));
            _seq.Join(_flight.DOLocalRotate(aim, beat.StartDuration).SetEase(Ease.InOutQuad));
        }

        private void AppendReturnHome(Vector2 homeAnchored, float backDur)
        {
            _seq.Append(_flight.DOAnchorPos(homeAnchored, backDur).SetEase(Ease.OutQuad));
            _seq.Join(_flight.DOLocalRotate(Vector3.zero, backDur).SetEase(Ease.OutCubic));
        }

        /// <summary>
        /// 冲撞命中那一刻起受击卡朝攻击方的反方向弹开，再回原位。位移做在卡根节点上，
        /// hit 片段驱动的是它下面的 PlayerRoot，两者不抢同一个 transform。
        /// 在命中回调里启动，不插入主时间轴，避免闪避时还被击退，也不改攻击序列时长。
        /// </summary>
        private void InsertHitKnockback(
            AttackTuningConfig.LevelTuning beat,
            RectTransform hitRect,
            Vector3 attackerWorldPos,
            Vector3 targetWorldPos)
        {
            if (hitRect == null || beat == null)
            {
                return;
            }

            var home = hitRect.anchoredPosition;
            var existing = _knockRects.IndexOf(hitRect);
            if (existing >= 0)
            {
                var prev = existing < _knockSeqs.Count ? _knockSeqs[existing] : null;
                if (prev != null && prev.IsActive())
                {
                    prev.Kill();
                }

                home = _knockHomes[existing];
                hitRect.anchoredPosition = home;
            }
            else
            {
                _knockRects.Add(hitRect);
                _knockHomes.Add(home);
                _knockSeqs.Add(null);
                existing = _knockRects.Count - 1;
            }

            var knockDir = KnockbackDir(hitRect, targetWorldPos - attackerWorldPos);
            var knockAnchored = KnockbackPoint(home, knockDir, beat.HitKnockbackDistance);
            var seq = DOTween.Sequence()
                .Append(hitRect.DOAnchorPos(knockAnchored, beat.HitKnockbackDuration).SetEase(Ease.OutQuad))
                .Append(hitRect.DOAnchorPos(home, beat.HitRecoverDuration).SetEase(Ease.OutQuad))
                .SetLink(hitRect.gameObject)
                .SetTarget(hitRect);
            seq.timeScale = _playTimeScale;
            _knockSeqs[existing] = seq;
        }

        /// <summary>攻击方指向受击方的世界方向换算到受击卡父节点的局部方向。</summary>
        private static Vector2 KnockbackDir(RectTransform hitRect, Vector3 worldDir)
        {
            var parent = hitRect.parent;
            return parent != null ? (Vector2)parent.InverseTransformDirection(worldDir) : (Vector2)worldDir;
        }

        private Vector2 WorldToHudAnchored(Vector3 world)
        {
            var parent = _flight != null ? _flight.parent as RectTransform : _hud as RectTransform;
            if (parent == null)
            {
                return world;
            }

            var canvas = parent.GetComponentInParent<Canvas>();
            Camera cam = null;
            if (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay)
            {
                cam = canvas.worldCamera;
            }

            var screen = RectTransformUtility.WorldToScreenPoint(cam, world);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, screen, cam, out var local);
            return local;
        }

        private static float AimAngleZ(Vector2 dir)
        {
            if (dir.sqrMagnitude < 0.0001f)
            {
                return 0f;
            }

            return Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg - 90f;
        }

        private static Vector2 RetreatPoint(Vector2 from, Vector2 dir, float distance)
        {
            if (dir.sqrMagnitude < 0.0001f)
            {
                return from;
            }

            return from - dir.normalized * distance;
        }

        private static Vector2 KnockbackPoint(Vector2 from, Vector2 attackDir, float distance)
        {
            if (attackDir.sqrMagnitude < 0.0001f)
            {
                return from;
            }

            return from + attackDir.normalized * distance;
        }

        private static string Clip(int level, string phase)
        {
            return $"ani_atk_lv{level:D2}_{phase}";
        }

        private static float ClipLength(Animator animator, string clipName)
        {
            if (animator == null || string.IsNullOrEmpty(clipName))
            {
                return 0f;
            }

            var controller = animator.runtimeAnimatorController;
            if (controller == null)
            {
                return 0f;
            }

            var clips = controller.animationClips;
            for (var i = 0; i < clips.Length; i++)
            {
                var clip = clips[i];
                if (clip != null && clip.name == clipName)
                {
                    return clip.length;
                }
            }

            var info = animator.GetCurrentAnimatorStateInfo(0);
            return info.IsName(clipName) ? info.length : 0f;
        }

        private static void PlayClip(Animator animator, string clipName)
        {
            if (animator == null || string.IsNullOrEmpty(clipName))
            {
                return;
            }

            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            animator.enabled = true;
            if (!animator.isInitialized)
            {
                animator.Rebind();
                animator.Update(0f);
            }

            // 强制从 0 重播，避免仍停在 default 时 Play 同层无反应。
            animator.Play(clipName, 0, 0f);
            animator.Update(0f);
        }
    }
}
