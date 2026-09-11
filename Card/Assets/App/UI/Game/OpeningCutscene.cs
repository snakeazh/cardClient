using System;
using System.Collections.Generic;
using App.Game;
using DG.Tweening;
using UnityEngine;

namespace App.UI
{
    /// <summary>
    /// 开局对白 + VS：中间敌人先说、玩家回，其余从左到右各说一句、玩家各回一句，再 VS 飞入渐隐。
    /// </summary>
    public sealed class OpeningCutscene
    {
        public const string LethalLine = "受死吧。";
        public const string DodgeReplyLine = "就这";

        public readonly struct Talk
        {
            public readonly PlayerItem Enemy;
            public readonly string MonsterLine;
            public readonly string PlayerLine;

            public Talk(PlayerItem enemy, string monsterLine, string playerLine)
            {
                Enemy = enemy;
                MonsterLine = monsterLine;
                PlayerLine = playerLine;
            }
        }

        private const float DialogHold = 1.1f / PlayerItem.DialogSpeed;
        private const float VsFlyDuration = 0.35f;
        private const float VsHold = 0.4f;
        private const float VsFade = 0.3f;
        private const float VsStartScale = 2.2f;
        private const float VsStartY = 80f;

        private readonly List<PlayerItem> _speakers = new List<PlayerItem>(4);
        private RectTransform _vs;
        private CanvasGroup _vsGroup;
        private Vector2 _vsHome;
        private Sequence _seq;
        private int _playToken;
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

        public void Play(
            IReadOnlyList<Talk> talks,
            PlayerItem player,
            GameObject link,
            Action onComplete)
        {
            Kill();
            _player = player;
            RememberSpeakers(talks);
            var token = ++_playToken;
            var seq = DOTween.Sequence();
            if (link != null)
            {
                seq.SetLink(link, LinkBehaviour.KillOnDestroy);
            }

            if (talks != null)
            {
                for (var i = 0; i < talks.Count; i++)
                {
                    var talk = talks[i];
                    AppendDialog(seq, talk.Enemy, top: false, talk.MonsterLine);
                    AppendDialog(seq, player, top: true, talk.PlayerLine);
                }
            }

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

        /// <summary>单句对白：逐字 → 停留 → 收起。缺节点立刻完成，不卡住后续攻击。</summary>
        public void PlayLine(PlayerItem item, bool top, string line, GameObject link, Action onComplete)
        {
            Kill();
            _player = top ? item : null;
            if (!top && item != null)
            {
                _speakers.Add(item);
            }

            var token = ++_playToken;
            if (item == null || !item.HasDialog(top))
            {
                onComplete?.Invoke();
                return;
            }

            var seq = DOTween.Sequence();
            if (link != null)
            {
                seq.SetLink(link, LinkBehaviour.KillOnDestroy);
            }

            AppendDialog(seq, item, top, line ?? string.Empty);
            seq.OnComplete(() =>
            {
                if (token != _playToken)
                {
                    return;
                }

                item.HideDialogImmediate();
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
            _speakers.Clear();
            ResetVsPose();
            HideVs();
        }

        public void Dispose()
        {
            Kill();
            _vs = null;
            _vsGroup = null;
            _player = null;
            _speakers.Clear();
        }

        private void RememberSpeakers(IReadOnlyList<Talk> talks)
        {
            _speakers.Clear();
            if (talks == null)
            {
                return;
            }

            for (var i = 0; i < talks.Count; i++)
            {
                var enemy = talks[i].Enemy;
                if (enemy != null && !_speakers.Contains(enemy))
                {
                    _speakers.Add(enemy);
                }
            }
        }

        private void AppendDialog(Sequence seq, PlayerItem item, bool top, string line)
        {
            if (item == null || !item.HasDialog(top) || string.IsNullOrEmpty(line))
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
            seq.Append(_vs.DOAnchorPos(_vsHome, VsFlyDuration).SetEase(Ease.OutBack));
            seq.Join(_vs.DOScale(1f, VsFlyDuration).SetEase(Ease.OutBack));
            if (_vsGroup != null)
            {
                seq.Join(_vsGroup.DOFade(1f, VsFlyDuration * 0.5f));
            }

            seq.AppendInterval(VsHold);
            if (_vsGroup != null)
            {
                seq.Append(_vsGroup.DOFade(0f, VsFade));
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
            for (var i = 0; i < _speakers.Count; i++)
            {
                _speakers[i]?.HideDialogImmediate();
            }

            _player?.HideDialogImmediate();
        }
    }
}
