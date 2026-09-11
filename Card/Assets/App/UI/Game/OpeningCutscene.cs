using System;
using System.Collections.Generic;
using App.Game;
using DG.Tweening;
using UnityEngine;

namespace App.UI
{
    /// <summary>
    /// 开局对峙 + 对白 + VS：人物上移、怪物下移 → VS → 敌人 TopDialog / 人物 BottomDialog（中间先、其余左到右）→ 回位 → 发牌。
    /// </summary>
    public sealed class OpeningCutscene
    {
        public const string LethalLine = "受死吧。";
        public const string DodgeReplyLine = "就这";
        /// <summary>对峙：人物上移像素。</summary>
        public const float PlayerPoseOffset = 360f;
        /// <summary>对峙：怪物下移像素。</summary>
        public const float EnemyPoseOffset = 300f;

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

        private const float PoseOutDuration = 0.35f;
        private const float DialogHold = 1.1f / PlayerItem.DialogSpeed;
        private const float VsFlyDuration = 0.35f;
        private const float VsHold = 0.25f;
        private const float VsFade = 0.3f;
        private const float VsStartScale = 2.2f;
        private const float VsStartY = 80f;

        private readonly List<PlayerItem> _speakers = new List<PlayerItem>(4);
        private readonly List<RectTransform> _poseRects = new List<RectTransform>(4);
        private readonly List<Vector2> _poseHomes = new List<Vector2>(4);
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
            Action onComplete,
            Vector2? playerLayoutHome = null)
        {
            Kill();
            _player = player;
            RememberSpeakers(talks);
            CapturePoseHomes(player, playerLayoutHome);
            ApplyPoseIn();
            var token = ++_playToken;
            var seq = DOTween.Sequence();
            if (link != null)
            {
                seq.SetLink(link, LinkBehaviour.KillOnDestroy);
            }

            AppendVsEnter(seq);

            if (talks != null)
            {
                for (var i = 0; i < talks.Count; i++)
                {
                    var talk = talks[i];
                    AppendDialog(seq, talk.Enemy, top: true, talk.MonsterLine);
                    AppendDialog(seq, player, top: false, talk.PlayerLine);
                }
            }

            AppendVsExit(seq);
            AppendPoseOut(seq);

            seq.OnComplete(() =>
            {
                if (token != _playToken)
                {
                    return;
                }

                HideDialogs();
                HideVs();
                RestorePoseImmediate();
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
            RestorePoseImmediate();
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
            ClearPoseHomes();
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

        private void CapturePoseHomes(PlayerItem player, Vector2? playerLayoutHome)
        {
            ClearPoseHomes();
            var playerRt = ItemRect(player);
            if (playerRt != null)
            {
                // 已在对峙位时要用原位，不能把当前坐标当 home。
                var home = playerLayoutHome ??
                           (playerRt.anchoredPosition - new Vector2(0f, PlayerPoseOffset));
                RememberPose(player, home);
            }

            for (var i = 0; i < _speakers.Count; i++)
            {
                // 敌人挂在槽位下，布局原点恒为 (0,0)；开场可能已在 (0,-EnemyPoseOffset)。
                RememberPose(_speakers[i], Vector2.zero);
            }
        }

        private void RememberPose(PlayerItem item, Vector2? layoutHome)
        {
            var rt = ItemRect(item);
            if (rt == null || _poseRects.Contains(rt))
            {
                return;
            }

            _poseRects.Add(rt);
            _poseHomes.Add(layoutHome ?? rt.anchoredPosition);
        }

        private void ClearPoseHomes()
        {
            _poseRects.Clear();
            _poseHomes.Clear();
        }

        private static RectTransform ItemRect(PlayerItem item)
        {
            return item != null ? item.transform as RectTransform : null;
        }

        private void AppendPoseOut(Sequence seq)
        {
            if (_poseRects.Count == 0)
            {
                return;
            }

            seq.AppendCallback(ApplyPoseOut);
            seq.AppendInterval(PoseOutDuration);
        }

        private void ApplyPoseIn()
        {
            for (var i = 0; i < _poseRects.Count; i++)
            {
                var rt = _poseRects[i];
                if (rt == null)
                {
                    continue;
                }

                var home = _poseHomes[i];
                var offsetY = IsPlayerRect(rt) ? PlayerPoseOffset : -EnemyPoseOffset;
                rt.DOKill();
                rt.anchoredPosition = home + new Vector2(0f, offsetY);
            }
        }

        private void ApplyPoseOut()
        {
            for (var i = 0; i < _poseRects.Count; i++)
            {
                var rt = _poseRects[i];
                if (rt == null)
                {
                    continue;
                }

                rt.DOKill();
                rt.DOAnchorPos(_poseHomes[i], PoseOutDuration)
                    .SetEase(Ease.OutCubic)
                    .SetLink(rt.gameObject, LinkBehaviour.KillOnDestroy);
            }
        }

        private bool IsPlayerRect(RectTransform rt)
        {
            var playerRt = ItemRect(_player);
            return playerRt != null && rt == playerRt;
        }

        private void RestorePoseImmediate()
        {
            for (var i = 0; i < _poseRects.Count; i++)
            {
                var rt = _poseRects[i];
                if (rt == null)
                {
                    continue;
                }

                rt.DOKill();
                rt.anchoredPosition = _poseHomes[i];
            }

            ClearPoseHomes();
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

        private void AppendVsEnter(Sequence seq)
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
        }

        private void AppendVsExit(Sequence seq)
        {
            if (_vs == null)
            {
                return;
            }

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
