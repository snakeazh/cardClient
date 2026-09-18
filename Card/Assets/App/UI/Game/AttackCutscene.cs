using System;
using System.Collections.Generic;
using App.Game;
using DG.Tweening;
using UnityEngine;

namespace App.UI
{
    /// <summary>
    /// 攻击演出：PlayerRoot 播 clip，位移由 DOTween 驱动；mask / hptextdi 由 GameUI 绑定。
    /// </summary>
    public sealed class AttackCutscene
    {
        private const string DefaultClip = "ani_default";
        private const string MissClip = "ani_atk_lv03_miss";
        private const int DeathFxSortingOrder = 240;

        private Transform _hud;
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

        public void Bind(Transform hud, PlayerItem player, PlayerItem[] enemies)
        {
            _hud = hud;
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
            var hitPos = _enemyRoots[visualSlot] != null ? _enemyRoots[visualSlot].position : Vector3.zero;
            InsertHitKnockback(_impactBeat, _enemyCardRects[visualSlot], _impactAttackerPos, hitPos);
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
        private void PlayImpact(
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
            var missed = onHit != null && onHit();
            PlayClip(attacker, Clip(level, "end"));
            if (missed)
            {
                PlayClip(victim, MissClip);
                HoldMissUntilDone(victim);
                return;
            }

            PlayClip(victim, Clip(level, "hit"));
            InsertHitKnockback(beat, hitRect, attackerWorldPos, targetWorldPos);
        }

        private void HoldMissUntilDone(Animator victim)
        {
            if (victim == null)
            {
                return;
            }

            var length = ClipLength(victim, MissClip);
            if (length <= 0f)
            {
                PlayClip(victim, DefaultClip);
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
            SetAllAnimSpeed(1f);
            _playTimeScale = 1f;
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

            animator.Play(clipName, 0, 0f);
            animator.Update(0f);
        }
    }
}
