using System;
using DG.Tweening;
using UnityEngine;

namespace App.UI
{
    /// <summary>
        /// 攻击演出只负责 card_icon 位移；mask / hptext 由 GameUI 绑定驱动。
    /// </summary>
    public sealed class AttackCutscene
    {
        private const float DashDuration = 0.38f;
        private const float HitHold = 0.18f;
        private const float ReturnDuration = 0.32f;
        private const float HpHideDelay = 0.85f;

        private Transform _root;
        private RectTransform _playerIcon;
        private Transform _playerIconHome;
        private Vector3 _playerIconHomePos;
        private Vector2 _playerIconHomeAnchored;
        private readonly RectTransform[] _enemyIcons = new RectTransform[3];
        private Sequence _seq;
        private int _playToken;

        public void Bind(Transform root, Transform playerInfo, GameObject[] enemyInfos)
        {
            _root = root;
            _playerIcon = FindChild(playerInfo, "card_icon") as RectTransform;
            if (_playerIcon != null)
            {
                _playerIconHome = _playerIcon.parent;
                _playerIconHomePos = _playerIcon.position;
                _playerIconHomeAnchored = _playerIcon.anchoredPosition;
            }

            var count = enemyInfos != null ? Math.Min(enemyInfos.Length, _enemyIcons.Length) : 0;
            for (var i = 0; i < count; i++)
            {
                var info = enemyInfos[i] != null ? enemyInfos[i].transform : null;
                _enemyIcons[i] = FindChild(info, "card_icon") as RectTransform;
            }
        }

        public Vector3 HitPosition(int visualSlot)
        {
            if (visualSlot < 0 || visualSlot >= _enemyIcons.Length || _enemyIcons[visualSlot] == null)
            {
                return Vector3.zero;
            }

            return _enemyIcons[visualSlot].position;
        }

        public void Play(int visualSlot, Action onHit, Action onReturned, Action onDone)
        {
            Kill();
            if (_playerIcon == null || visualSlot < 0 || visualSlot >= _enemyIcons.Length ||
                _enemyIcons[visualSlot] == null)
            {
                onHit?.Invoke();
                onReturned?.Invoke();
                onDone?.Invoke();
                return;
            }

            var token = ++_playToken;
            _playerIconHomePos = _playerIcon.position;
            _playerIconHomeAnchored = _playerIcon.anchoredPosition;
            var homeParent = _playerIconHome != null ? _playerIconHome : _playerIcon.parent;
            var hitPos = _enemyIcons[visualSlot].position;

            _playerIcon.SetParent(_root, true);
            _playerIcon.SetAsLastSibling();

            _seq = DOTween.Sequence();
            _seq.Append(_playerIcon.DOMove(hitPos, DashDuration).SetEase(Ease.InQuad));
            _seq.AppendCallback(() =>
            {
                if (token != _playToken)
                {
                    return;
                }

                _playerIcon.DOPunchScale(Vector3.one * 0.12f, 0.2f, 8, 0.6f);
                onHit?.Invoke();
            });
            _seq.AppendInterval(HitHold);
            _seq.Append(_playerIcon.DOMove(_playerIconHomePos, ReturnDuration).SetEase(Ease.OutQuad));
            _seq.AppendCallback(() =>
            {
                if (token != _playToken)
                {
                    return;
                }

                RestoreIcon(homeParent);
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
            RestoreIcon(_playerIconHome);
        }

        private void Kill()
        {
            _playToken++;
            _seq?.Kill();
            _seq = null;
        }

        private void RestoreIcon(Transform home)
        {
            if (_playerIcon == null || home == null)
            {
                return;
            }

            _playerIcon.SetParent(home, false);
            _playerIcon.anchoredPosition = _playerIconHomeAnchored;
            _playerIcon.localScale = Vector3.one;
            _playerIcon.localRotation = Quaternion.identity;
        }

        private static Transform FindChild(Transform root, string name)
        {
            if (root == null)
            {
                return null;
            }

            if (root.name == name)
            {
                return root;
            }

            for (var i = 0; i < root.childCount; i++)
            {
                var found = FindChild(root.GetChild(i), name);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }
    }
}
