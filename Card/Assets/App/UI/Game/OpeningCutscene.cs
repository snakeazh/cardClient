using System;
using App.Game;
using DG.Tweening;
using UnityEngine;

namespace App.UI
{
    /// <summary>
    /// 开局对白 + VS：敌人 BottomDialog → 玩家 TopDialog → VS 飞入渐隐。
    /// </summary>
    public sealed class OpeningCutscene
    {
        public const string EnemyLine = "来得正好。";
        public const string PlayerLine = "放马过来。";

        private const float DialogHold = 1.1f;
        private const float VsFlyDuration = 0.35f;
        private const float VsHold = 0.4f;
        private const float VsFade = 0.3f;
        private const float VsStartScale = 2.2f;
        private const float VsStartY = 80f;

        private RectTransform _vs;
        private CanvasGroup _vsGroup;
        private Vector2 _vsHome;
        private Sequence _seq;
        private int _playToken;
        private PlayerItem _enemy;
        private PlayerItem _player;

        public void Bind(RectTransform vs)
        {
            Kill();
            _vs = vs;
            if (_vs == null)
            {
                _vsGroup = null;
                return;
            }

            _vsHome = _vs.anchoredPosition;
            _vsGroup = _vs.GetComponent<CanvasGroup>();
            if (_vsGroup == null)
            {
                _vsGroup = _vs.gameObject.AddComponent<CanvasGroup>();
            }

            HideVs();
        }

        public void Play(PlayerItem enemy, PlayerItem player, GameObject link, Action onComplete)
        {
            Kill();
            _enemy = enemy;
            _player = player;
            var token = ++_playToken;
            var seq = DOTween.Sequence().SetUpdate(true);
            if (link != null)
            {
                seq.SetLink(link, LinkBehaviour.KillOnDestroy);
            }

            AppendDialog(seq, enemy, top: false, EnemyLine);
            AppendDialog(seq, player, top: true, PlayerLine);
            AppendVs(seq);

            seq.OnComplete(() =>
            {
                if (token != _playToken)
                {
                    return;
                }

                HideDialogs();
                HideVs();
                onComplete?.Invoke();
            });
            _seq = seq;
        }

        public void Kill()
        {
            _playToken++;
            if (_seq != null && _seq.IsActive())
            {
                _seq.Kill();
            }

            _seq = null;
            HideDialogs();
            ResetVsPose();
            HideVs();
        }

        public void Dispose()
        {
            Kill();
            _vs = null;
            _vsGroup = null;
            _enemy = null;
            _player = null;
        }

        private void AppendDialog(Sequence seq, PlayerItem item, bool top, string line)
        {
            if (item == null || !item.HasDialog(top))
            {
                return;
            }

            seq.AppendCallback(() => item.PlayDialog(top, line));
            seq.AppendInterval(PlayerItem.DialogPlayDuration(line) + DialogHold);
            seq.AppendCallback(() => item.HideDialog());
            seq.AppendInterval(PlayerItem.DialogHideDuration);
        }

        private void AppendVs(Sequence seq)
        {
            if (_vs == null)
            {
                return;
            }

            seq.AppendCallback(PrepareVs);
            seq.Append(_vs.DOAnchorPos(_vsHome, VsFlyDuration).SetEase(Ease.OutBack).SetUpdate(true));
            seq.Join(_vs.DOScale(1f, VsFlyDuration).SetEase(Ease.OutBack).SetUpdate(true));
            if (_vsGroup != null)
            {
                seq.Join(_vsGroup.DOFade(1f, VsFlyDuration * 0.5f).SetUpdate(true));
            }

            seq.AppendInterval(VsHold);
            if (_vsGroup != null)
            {
                seq.Append(_vsGroup.DOFade(0f, VsFade).SetUpdate(true));
            }

            seq.AppendCallback(HideVs);
        }

        private void PrepareVs()
        {
            if (_vs == null)
            {
                return;
            }

            _vs.gameObject.SetActive(true);
            _vs.anchoredPosition = _vsHome + new Vector2(0f, VsStartY);
            _vs.localScale = Vector3.one * VsStartScale;
            if (_vsGroup != null)
            {
                _vsGroup.alpha = 0f;
            }
        }

        private void HideVs()
        {
            if (_vs == null)
            {
                return;
            }

            _vs.gameObject.SetActive(false);
            ResetVsPose();
        }

        private void ResetVsPose()
        {
            if (_vs == null)
            {
                return;
            }

            _vs.anchoredPosition = _vsHome;
            _vs.localScale = Vector3.one;
            if (_vsGroup != null)
            {
                _vsGroup.alpha = 1f;
            }
        }

        private void HideDialogs()
        {
            _enemy?.HideDialogImmediate();
            _player?.HideDialogImmediate();
        }
    }
}
