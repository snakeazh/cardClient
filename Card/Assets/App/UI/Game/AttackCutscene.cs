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
        /// <summary>角色特效所在世界平面 Z（与主相机对位用）。</summary>
        private const float FxPlaneZ = 0f;

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
        private AttackTuningConfig.LevelTuning _impactBeat;
        private Vector3 _impactAttackerPos;
        private AudioClip _impactSfx;
        private bool _disposed;
        /// <summary>特效攻击受击：不播卡牌击退抖动（主目标与溅射）。</summary>
        private bool _skillHitNoKnockback;
        private readonly Dictionary<string, GameObject> _heroFxPrefabs = new Dictionary<string, GameObject>(4);
        private readonly List<string> _heroFxOwnedKeys = new List<string>(4);
        private readonly List<GameObject> _skillFxSpawned = new List<GameObject>(4);
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

        public void PlayIncoming(
            int visualSlot,
            int level,
            Func<bool> onHit,
            Action onCollisionDone,
            Action onReturned,
            Action onDone)
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

                PlayImpact(enemyAnim, _playerAnim, level, onHit, beat, _playerCardRect, homePos, hitPos);
            });
            _seq.AppendInterval(beat.HitHoldDuration);
            _seq.AppendCallback(() =>
            {
                if (token != _playToken)
                {
                    return;
                }

                PlayClip(enemyAnim, Clip(level, "back"));
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

            PlayClip(victim, Clip(_impactLevel, "hit"));
            if (_skillHitNoKnockback)
            {
                return;
            }

            var hitPos = _enemyRoots[visualSlot] != null ? _enemyRoots[visualSlot].position : Vector3.zero;
            InsertHitKnockback(_impactBeat, _enemyCardRects[visualSlot], _impactAttackerPos, hitPos);
        }

        /// <summary>
        /// 玩家出刀：释放特效 → 命中瞬间同时：结算 + 受击特效 + 受击动画 + 伤害字。
        /// 预制体未预载时返回 false，由调用方回退近战。
        /// </summary>
        /// <param name="onHit">命中瞬间结算（扣血/溶解），不要在这里出伤害字。</param>
        /// <param name="onHitDamageText">与受击动画同帧显示伤害数字。</param>
        public bool PlayHeroSkill(
            int visualSlot,
            int level,
            HeroEffectsConfig attackFx,
            HeroEffectsConfig hitFx,
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
            var wait = Mathf.Max(0f, attackFx.Time);
            var t0 = Time.realtimeSinceStartup;
            float Elapsed() => Time.realtimeSinceStartup - t0;

            PlayClip(_playerAnim, DefaultClip);

            _seq = DOTween.Sequence();
            _seq.timeScale = _playTimeScale;

            var projectile = IsProjectile(attackFx);
            var rolePoint = IsRolePoint(attackFx);
            AppLog.Info(
                LogChannel.UI,
                $"[HeroSkill][t={Elapsed():0.000}] 开始 slot={visualSlot} lv={level} " +
                $"attack={attackFx.Effects} attackTime={wait:0.###} projectile={projectile} rolepoint={rolePoint} " +
                $"hit={hitFx.Effects} hitTime={hitFx.Time:0.###}(仅参考/不作出字延迟) hpHold={tuning.HpTextHoldDuration:0.###}");

            // 释放特效：投射物飞向目标；非投射物按 rolepoint 落点后等 Time。
            if (projectile)
            {
                var castGo = SpawnSkillFx(attackPrefab, homeUi, life: 0f);
                AppLog.Info(
                    LogChannel.UI,
                    $"[HeroSkill][t={Elapsed():0.000}] 阶段1-释放(投射物) 生成={castGo != null} 飞行={Mathf.Max(0.01f, wait):0.###}s");
                if (castGo != null)
                {
                    var hitFxPos = ResolveFxWorldPos(attackPrefab, hitUi);
                    FaceToward(castGo.transform, castGo.transform.position, hitFxPos);
                    var fly = Mathf.Max(0.01f, wait);
                    _seq.Append(castGo.transform.DOMove(hitFxPos, fly).SetEase(Ease.InQuad));
                }
                else
                {
                    _seq.AppendInterval(wait);
                }
            }
            else
            {
                var castUi = rolePoint ? homeUi : hitUi;
                SpawnSkillFx(attackPrefab, castUi, life: 0f);
                AppLog.Info(
                    LogChannel.UI,
                    $"[HeroSkill][t={Elapsed():0.000}] 阶段1-释放(定点) pos={(rolePoint ? "人物" : "目标")} 等待 attackTime={wait:0.###}s");
                _seq.AppendInterval(wait);
            }

            // 命中瞬间：清释放特效 → 结算 + 受击特效 + 受击动画 + 伤害字（不等 Hit.Time）。
            _seq.AppendCallback(() =>
            {
                if (token != _playToken)
                {
                    return;
                }

                AppLog.Info(LogChannel.UI, $"[HeroSkill][t={Elapsed():0.000}] 阶段2-命中开始 清释放特效");
                ClearSkillFx();
                var missed = PlayHeroSkillImpact(targetAnim, level, onHit, beat, homeUi);
                AppLog.Info(
                    LogChannel.UI,
                    $"[HeroSkill][t={Elapsed():0.000}] 阶段2a-结算完成 missed={missed}（受击动画={(missed ? "MISS" : "hit")}）");
                if (!missed)
                {
                    SpawnHitFx(hitPrefab, hitFx, homeUi, hitUi);
                    AppLog.Info(
                        LogChannel.UI,
                        $"[HeroSkill][t={Elapsed():0.000}] 阶段2b-受击特效 hit={hitFx.Effects}");
                }

                AppLog.Info(LogChannel.UI, $"[HeroSkill][t={Elapsed():0.000}] 阶段2c-出伤害字");
                onHitDamageText?.Invoke();
                AppLog.Info(LogChannel.UI, $"[HeroSkill][t={Elapsed():0.000}] 阶段2d-collisionDone/returned");
                onCollisionDone?.Invoke();
                RestoreHitTarget();
                onReturned?.Invoke();
                AppLog.Info(
                    LogChannel.UI,
                    $"[HeroSkill][t={Elapsed():0.000}] 阶段3-伤害字停留 hpHold={tuning.HpTextHoldDuration:0.###}s");
            });
            _seq.AppendInterval(tuning.HpTextHoldDuration);
            _seq.AppendCallback(() =>
            {
                if (token != _playToken)
                {
                    return;
                }

                AppLog.Info(LogChannel.UI, $"[HeroSkill][t={Elapsed():0.000}] 阶段4-清命中特效");
                ClearSkillFx();
            });
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
        /// 进桌预载当前英雄 Attack/Hit 预制体（本局人物固定）。
        /// 出刀只读缓存，不再临时 Load；WebGL 禁止同步加载。
        /// </summary>
        public async Task PreloadHeroEffectsAsync(IResourceService resources, HeroConfig hero)
        {
            if (resources == null || !HeroMechanics.TryResolveEffects(hero, out var attack, out var hit))
            {
                return;
            }

            _disposed = false;
            _heroFxResources = resources;
            await PreloadHeroFxPrefabAsync(resources, attack.Effects);
            if (_disposed)
            {
                return;
            }

            await PreloadHeroFxPrefabAsync(resources, hit.Effects);
            if (CanPlayHeroSkill(attack, hit))
            {
                AppLog.Info(
                    LogChannel.Assets,
                    $"[AttackCutscene] 英雄特效已预载 attack={attack.Effects} hit={hit.Effects}");
            }
        }

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

        /// <summary>特效攻击命中：结算；MISS 播闪避；命中播受击动画（与特效/伤害字同帧）。无击退。</summary>
        private bool PlayHeroSkillImpact(
            Animator victim,
            int level,
            Func<bool> onHit,
            AttackTuningConfig.LevelTuning beat,
            Vector3 attackerWorldPos)
        {
            _impactLevel = level;
            _impactBeat = beat;
            _impactAttackerPos = attackerWorldPos;
            PlayImpactSfx();
            var missed = onHit != null && onHit();
            if (missed)
            {
                PlayClip(victim, MissClip);
                HoldVictimClipUntilDone(victim, MissClip);
                return true;
            }

            var hitClip = Clip(level, "hit");
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

        private void SpawnHitFx(
            GameObject prefab,
            HeroEffectsConfig hitFx,
            Vector3 attackerUi,
            Vector3 targetUi)
        {
            if (prefab == null || hitFx == null)
            {
                return;
            }

            if (IsProjectile(hitFx))
            {
                var go = SpawnSkillFx(prefab, attackerUi, life: 0f);
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

            // 非投射物：rolepoint=1 落在人物点，否则落在目标；存活到下次清理。
            var spawnUi = IsRolePoint(hitFx) ? attackerUi : targetUi;
            SpawnSkillFx(prefab, spawnUi, life: 0f);
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
            if (prefab == null)
            {
                return null;
            }

            var fxWorldPos = ResolveFxWorldPos(prefab, uiWorldPos);
            // 挂世界根，保留预制体 Layer（UI / Default），不再强行改 Default。
            var go = UnityEngine.Object.Instantiate(prefab);
            go.name = prefab.name;
            go.transform.SetParent(null, false);
            go.transform.position = fxWorldPos;
            go.transform.rotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;
            go.SetActive(true);
            if (IsUiLayer(prefab))
            {
                UiFx.ApplySorting(go, 230);
            }

            RestartSkillAnimators(go);
            UiFx.RestartParticles(go);
            UiFx.ClearTrails(go);
            _skillFxSpawned.Add(go);
            AppLog.Info(
                LogChannel.UI,
                $"[AttackCutscene] SpawnSkillFx name={go.name} layer={go.layer} pos={fxWorldPos} life={life}");

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

            _skillFxSpawned.Remove(go);
            go.transform.DOKill();
            UnityEngine.Object.Destroy(go);
        }

        private async Task PreloadHeroFxPrefabAsync(IResourceService resources, string effectsName)
        {
            var key = ResResourcePaths.HeroEffect(effectsName);
            if (resources == null || string.IsNullOrEmpty(key) || _heroFxPrefabs.ContainsKey(key))
            {
                return;
            }

            if (resources.TryGetCached<GameObject>(key, out var cached) && cached != null)
            {
                _heroFxPrefabs[key] = cached;
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
