using System;
using App.Game;
using DG.Tweening;
using UnityEngine;

namespace App.UI
{
    /// <summary>
    /// 攻击演出：PlayerRoot 播 clip，位移由 DOTween 驱动；mask / hptext 由 GameUI 绑定。
    /// </summary>
    public sealed class AttackCutscene
    {
        private const string DefaultClip = "ani_default";
        private const float HpHideDelay = 0.85f;
        private const float RetreatDur = 0.18f;

        private static readonly float[] StartDur = { 0.43f, 0.90f, 1.25f };
        private static readonly float[] MoveDur = { 0.18f, 0.18f, 0.18f };
        private static readonly float[] EndDur = { 0.25f, 0.25f, 0.33f };
        private static readonly float[] BackDur = { 0.45f, 0.45f, 0.55f };
        private static readonly float[] RetreatDist = { 40f, 56f, 72f };

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

        public void Bind(Transform hud, PlayerItem player, PlayerItem[] enemies)
        {
            _hud = hud;
            BindPlayer(player);

            var count = enemies != null ? Math.Min(enemies.Length, _enemyRoots.Length) : 0;
            for (var i = 0; i < _enemyRoots.Length; i++)
            {
                _enemyRoots[i] = null;
                _enemyAnims[i] = null;
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

        public void PlayIncoming(int visualSlot, int level, Action onHit, Action onReturned, Action onDone)
        {
            Kill();
            level = Mathf.Clamp(level, 1, 3);
            var enemyRoot = visualSlot >= 0 && visualSlot < _enemyRoots.Length ? _enemyRoots[visualSlot] : null;
            var enemyAnim = visualSlot >= 0 && visualSlot < _enemyAnims.Length ? _enemyAnims[visualSlot] : null;
            if (_playerRoot == null || enemyRoot == null)
            {
                onHit?.Invoke();
                onReturned?.Invoke();
                onDone?.Invoke();
                return;
            }

            var token = ++_playToken;
            var idx = level - 1;
            var startDur = StartDur[idx];
            var moveDur = MoveDur[idx];
            var endDur = EndDur[idx];
            var backDur = BackDur[idx];
            _incomingRoot = enemyRoot;
            _incomingHome = enemyRoot.parent;
            _incomingHomeAnchored = enemyRoot.anchoredPosition;
            var homePos = enemyRoot.position;
            var hitPos = _playerRoot.position;

            AttachRootToFlight(_incomingRoot, homePos);
            var homeAnchored = _flight.anchoredPosition;
            var hitAnchored = WorldToHudAnchored(hitPos);

            _seq = DOTween.Sequence();
            AppendAimAndRetreat(enemyAnim, level, startDur, homeAnchored, hitAnchored, idx, invertAim: true);
            _seq.AppendCallback(() =>
            {
                if (token != _playToken)
                {
                    return;
                }

                PlayClip(enemyAnim, Clip(level, "move"));
            });
            _seq.Append(_flight.DOAnchorPos(hitAnchored, moveDur).SetEase(Ease.InQuad));
            _seq.AppendCallback(() =>
            {
                if (token != _playToken)
                {
                    return;
                }

                PlayClip(enemyAnim, Clip(level, "end"));
                PlayClip(_playerAnim, Clip(level, "hit"));
                onHit?.Invoke();
            });
            _seq.AppendInterval(endDur);
            _seq.AppendCallback(() =>
            {
                if (token != _playToken)
                {
                    return;
                }

                PlayClip(enemyAnim, Clip(level, "back"));
                PlayClip(_playerAnim, DefaultClip);
            });
            AppendReturnHome(homeAnchored, backDur);
            _seq.AppendCallback(() =>
            {
                if (token != _playToken)
                {
                    return;
                }

                RestoreIncoming();
                PlayClip(enemyAnim, DefaultClip);
                onReturned?.Invoke();
            });
            _seq.AppendInterval(HpHideDelay);
            _seq.OnComplete(() =>
            {
                if (token != _playToken)
                {
                    return;
                }

                onDone?.Invoke();
            });
        }

        public void Play(int visualSlot, int level, Action onHit, Action onReturned, Action onDone)
        {
            Kill();
            level = Mathf.Clamp(level, 1, 3);
            if (_playerRoot == null || visualSlot < 0 || visualSlot >= _enemyRoots.Length ||
                _enemyRoots[visualSlot] == null)
            {
                onHit?.Invoke();
                onReturned?.Invoke();
                onDone?.Invoke();
                return;
            }

            var token = ++_playToken;
            var idx = level - 1;
            var startDur = StartDur[idx];
            var moveDur = MoveDur[idx];
            var endDur = EndDur[idx];
            var backDur = BackDur[idx];
            var targetAnim = _enemyAnims[visualSlot];
            var homeParent = _playerHome != null ? _playerHome : _playerRoot.parent;
            _playerHomeAnchored = _playerRoot.anchoredPosition;
            var homePos = _playerRoot.position;
            var hitPos = _enemyRoots[visualSlot].position;

            AttachRootToFlight(_playerRoot, homePos);
            var homeAnchored = _flight.anchoredPosition;
            var hitAnchored = WorldToHudAnchored(hitPos);

            _seq = DOTween.Sequence();
            AppendAimAndRetreat(_playerAnim, level, startDur, homeAnchored, hitAnchored, idx, invertAim: false);
            _seq.AppendCallback(() =>
            {
                if (token != _playToken)
                {
                    return;
                }

                PlayClip(_playerAnim, Clip(level, "move"));
            });
            _seq.Append(_flight.DOAnchorPos(hitAnchored, moveDur).SetEase(Ease.InQuad));
            _seq.AppendCallback(() =>
            {
                if (token != _playToken)
                {
                    return;
                }

                PlayClip(_playerAnim, Clip(level, "end"));
                PlayClip(targetAnim, Clip(level, "hit"));
                onHit?.Invoke();
            });
            _seq.AppendInterval(endDur);
            _seq.AppendCallback(() =>
            {
                if (token != _playToken)
                {
                    return;
                }

                PlayClip(_playerAnim, Clip(level, "back"));
                PlayClip(targetAnim, DefaultClip);
            });
            AppendReturnHome(homeAnchored, backDur);
            _seq.AppendCallback(() =>
            {
                if (token != _playToken)
                {
                    return;
                }

                RestoreRoot(homeParent);
                PlayClip(_playerAnim, DefaultClip);
                onReturned?.Invoke();
            });
            _seq.AppendInterval(HpHideDelay);
            _seq.OnComplete(() =>
            {
                if (token != _playToken)
                {
                    return;
                }

                onDone?.Invoke();
            });
        }

        public void Dispose()
        {
            Kill();
            RestoreRoot(_playerHome);
            RestoreIncoming();
            PlayClip(_playerAnim, DefaultClip);
            DestroyFlight();
        }

        private void BindPlayer(PlayerItem player)
        {
            _playerRoot = player != null ? player.RootRect : null;
            _playerAnim = player != null ? player.RootAnimator : null;
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
            RestoreRoot(_playerHome);
            RestoreIncoming();
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
            float startDur,
            Vector2 homeAnchored,
            Vector2 hitAnchored,
            int idx,
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
            _seq.Append(_flight.DOLocalRotate(aim, startDur).SetEase(Ease.OutCubic));
            _seq.Append(_flight.DOAnchorPos(RetreatPoint(homeAnchored, dir, RetreatDist[idx]*4), RetreatDur)
                .SetEase(Ease.OutQuad));
        }

        private void AppendReturnHome(Vector2 homeAnchored, float backDur)
        {
            _seq.Append(_flight.DOAnchorPos(homeAnchored, backDur).SetEase(Ease.OutQuad));
            _seq.Join(_flight.DOLocalRotate(Vector3.zero, backDur).SetEase(Ease.OutCubic));
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

        private static string Clip(int level, string phase)
        {
            return $"ani_atk_lv{level:D2}_{phase}";
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
