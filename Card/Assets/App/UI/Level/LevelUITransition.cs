using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

namespace App.UI
{
    /// <summary>
    /// LevelUI 过渡动画的目标节点。由 <see cref="LevelUIView"/> 在 OnBind 时填好。
    /// </summary>
    internal sealed class LevelUITransitionTargets
    {
        public RectTransform Root;
        public RectTransform Hor;
        public RectTransform HeroCard;
        public RectTransform RoleInfo;
        public RectTransform StageInfo;
        public RectTransform StageNum;
        public RectTransform HeroPanel;
        public RectTransform LevelPanel;
        public RectTransform LastBtn;
        public RectTransform UseHeroBtn;
        public RectTransform UnlockHeroBtn;
        public RectTransform StartGameBtn;
        public List<RectTransform> HeroItems = new List<RectTransform>();
        public List<RectTransform> LevelItems = new List<RectTransform>();
    }

    /// <summary>
    /// LevelUI 的运行时过渡：弹窗整体入场/退场、选角/选难度切换、选中反馈。
    /// 纯运行时实现（CanvasGroup / 遮罩 / 内容容器都在运行时生成），不改预制体；只写 alpha、
    /// localScale、localRotation，以及手摆节点的 anchoredPosition。不要动 GridLayoutGroup 子项的
    /// anchoredPosition——布局重算会把它们覆盖掉。
    /// LevelUI 注册在 Popup 层、浮在 Home 上方，所以入场是"暗幕压暗 Home + 内容整体弹出"，
    /// 退场是"暗幕撤掉 + 内容整体收起"，而不是此前整页压黑再反序收牌。
    /// </summary>
    internal sealed class LevelUITransition
    {
        private const float CardStagger = 0.035f;
        private const float LevelCardStagger = 0.05f;
        private const float PanelFade = 0.20f;
        private const float StageInfoFade = 0.18f;
        private const float ButtonFade = 0.18f;

        // 弹窗壳（运行时生成）：遮罩压暗下面的 Home，内容容器只承载原有节点，两者独立动画。
        private const string MaskNodeName = "LevelUI_PopupMask";
        private const string BodyNodeName = "LevelUI_PopupBody";
        // 与其它 Popup（SettingPop / TTRewardPop 的 Mask）保持一致的压暗程度
        private const float MaskAlpha = 0.627451f;
        private const float MaskFadeIn = 0.20f;
        private const float MaskFadeOut = 0.16f;
        // 入场从略小一档弹出、退场收回略小一档（相对静止缩放的比例）
        private const float PopScaleFrom = 0.92f;
        private const float PopScaleTo = 0.96f;
        private const float PopInDuration = 0.26f;
        private const float PopOutDuration = 0.18f;
        private const float BodyFadeIn = 0.16f;

        private readonly LevelUITransitionTargets _t;
        private readonly Dictionary<RectTransform, CanvasGroup> _groups =
            new Dictionary<RectTransform, CanvasGroup>();

        private Sequence _entranceSeq;
        private Sequence _exitSeq;
        private Sequence _heroPanelSeq;
        private Sequence _levelPanelSeq;
        private Sequence _stageInfoSeq;
        private Sequence _heroChangedSeq;
        private CanvasGroup _rootGroup;
        private CanvasGroup _bodyGroup;
        private RectTransform _body;
        private Vector3 _bodyRestScale = Vector3.one;
        private Image _mask;

        private Vector3 _horRestScale = Vector3.one;
        private Vector2 _horRestPos;
        private Vector3 _cardRestScale = Vector3.one;
        private Vector2 _roleRestPos;
        private Vector2 _stageRestPos;
        private Vector3 _stageRestScale = Vector3.one;
        private Vector2 _heroPanelRestPos;
        private Vector2 _levelPanelRestPos;
        private Vector3 _stageNumRestScale = Vector3.one;

        public LevelUITransition(LevelUITransitionTargets targets)
        {
            _t = targets;
        }

        /// <summary>入场总时长（含卡片逐张发出的最后一张）。</summary>
        public float EntranceDuration => 0.65f;

        /// <summary>退场总时长；LevelUIView 在这个时长后再执行真正的关闭。</summary>
        public float ExitDuration => 0.22f;

        /// <summary>缓存静止姿态、建遮罩/内容容器与 CanvasGroup、按当前阶段摆好初始显隐。</summary>
        public void Prepare(bool showHero, bool showStage, bool showLevel)
        {
            CacheRest();
            EnsurePopupShell();
            EnsureGroups();

            _rootGroup = Group(_t.Root);
            if (_rootGroup != null)
            {
                _rootGroup.alpha = 1f;
                _rootGroup.blocksRaycasts = true;
                _rootGroup.interactable = true;
            }

            // 首次露出前保持在"暗幕透明 + 内容收起"的起点，交给 OnPresented 的入场动画，
            // 避免视图激活到入场之间渲染出一帧完整界面。
            SetMaskAlpha(0f);
            if (_body != null)
            {
                _body.localScale = _bodyRestScale * PopScaleFrom;
            }

            if (_bodyGroup != null)
            {
                _bodyGroup.alpha = 0f;
            }

            SetPanelImmediate(_t.HeroPanel, _heroPanelRestPos, showHero);
            SetPanelImmediate(_t.LevelPanel, _levelPanelRestPos, showLevel);
            SetStageImmediate(showStage);

            if (_t.Hor != null)
            {
                _t.Hor.localScale = _horRestScale;
                _t.Hor.anchoredPosition = _horRestPos;
            }

            if (_t.HeroCard != null)
            {
                _t.HeroCard.localScale = _cardRestScale;
                // 大卡的 CanvasGroup 也会被换英雄等分支改 alpha，入场前先补回可见。
                var cardGroup = Group(_t.HeroCard);
                if (cardGroup != null)
                {
                    cardGroup.alpha = 1f;
                }
            }

            if (_t.RoleInfo != null)
            {
                _t.RoleInfo.anchoredPosition = _roleRestPos;
            }

            if (_t.StageNum != null)
            {
                _t.StageNum.localScale = _stageNumRestScale;
            }

            ResetItems(_t.HeroItems);
            ResetItems(_t.LevelItems);
        }

        /// <summary>首次露出时播放入场。stagePhase 仅用于异常情况下的兜底。</summary>
        public void PlayEntrance(bool stagePhase)
        {
            KillSequences();
            EnsurePopupShell();

            // 弹窗起点：暗幕全透明、内容收小一档且不可见，由下面的序列一起放出来。
            SetMaskAlpha(0f);
            if (_body != null)
            {
                _body.localScale = _bodyRestScale * PopScaleFrom;
            }

            if (_bodyGroup != null)
            {
                _bodyGroup.alpha = 0f;
            }

            if (_t.Hor != null)
            {
                var group = Group(_t.Hor);
                if (group != null)
                {
                    group.alpha = 0f;
                }

                _t.Hor.anchoredPosition = _horRestPos + new Vector2(0f, -50f);
                _t.Hor.localScale = _horRestScale * 0.97f;
            }

            if (_t.HeroCard != null)
            {
                _t.HeroCard.localScale = _cardRestScale * 0.88f;
                var cardGroup = Group(_t.HeroCard);
                if (cardGroup != null)
                {
                    cardGroup.alpha = 1f;
                }
            }

            if (_t.RoleInfo != null)
            {
                _t.RoleInfo.anchoredPosition = _roleRestPos + new Vector2(24f, 0f);
                var roleGroup = Group(_t.RoleInfo);
                if (roleGroup != null)
                {
                    roleGroup.alpha = 1f;
                }
            }

            var lastGroup = Group(_t.LastBtn);
            if (lastGroup != null)
            {
                lastGroup.alpha = 0f;
            }

            RectTransform primary;
            if (stagePhase)
            {
                SetPanelImmediate(_t.HeroPanel, _heroPanelRestPos, false);
                SetStageImmediate(true);
                PrepareStageInfoStart();
                SetPanelImmediate(_t.LevelPanel, _levelPanelRestPos, true);
                PrepareLevelCardsStart();
                primary = _t.StartGameBtn != null && _t.StartGameBtn.gameObject.activeSelf
                    ? _t.StartGameBtn
                    : null;
            }
            else
            {
                SetStageImmediate(false);
                SetPanelImmediate(_t.LevelPanel, _levelPanelRestPos, false);
                SetPanelImmediate(_t.HeroPanel, _heroPanelRestPos, true);
                PrepareHeroPanelStart();
                primary = ActiveButton(_t.UseHeroBtn, _t.UnlockHeroBtn);
            }

            if (primary != null)
            {
                var primaryGroup = Group(primary);
                if (primaryGroup != null)
                {
                    primaryGroup.alpha = 0f;
                }
            }

            var seq = NewSequence(_t.Root);
            if (seq == null)
            {
                return;
            }

            _entranceSeq = seq;
            SetInputBlocked(true);

            // 弹窗壳先动：Home 上盖一层暗幕，内容整体从略小一档弹出并淡入。
            if (_mask != null)
            {
                seq.Insert(0f, _mask.DOFade(MaskAlpha, MaskFadeIn).SetEase(Ease.OutQuad));
            }

            if (_body != null)
            {
                seq.Insert(0f, _body.DOScale(_bodyRestScale, PopInDuration).SetEase(Ease.OutCubic));
            }

            if (_bodyGroup != null)
            {
                seq.Insert(0f, _bodyGroup.DOFade(1f, BodyFadeIn));
            }

            if (_t.Hor != null)
            {
                var group = Group(_t.Hor);
                if (group != null)
                {
                    seq.Join(group.DOFade(1f, 0.30f));
                }

                seq.Join(_t.Hor.DOAnchorPos(_horRestPos, 0.34f).SetEase(Ease.OutCubic));
                seq.Join(_t.Hor.DOScale(_horRestScale, 0.34f).SetEase(Ease.OutCubic));
            }

            if (_t.HeroCard != null)
            {
                seq.Insert(0.08f, _t.HeroCard.DOScale(_cardRestScale, 0.35f).SetEase(Ease.OutBack));
            }

            if (_t.RoleInfo != null)
            {
                seq.Insert(0.10f, _t.RoleInfo.DOAnchorPos(_roleRestPos, 0.30f).SetEase(Ease.OutCubic));
            }

            if (lastGroup != null)
            {
                seq.Insert(0.22f, lastGroup.DOFade(1f, 0.20f));
            }

            if (stagePhase)
            {
                AppendStageIn(seq, 0f);
            }
            else
            {
                AppendHeroPanelIn(seq, 0.16f);
            }

            if (primary != null)
            {
                var primaryGroup = Group(primary);
                if (primaryGroup != null)
                {
                    seq.Insert(stagePhase ? 0.10f : 0.26f, primaryGroup.DOFade(1f, 0.20f));
                }
            }
        }

        /// <summary>
        /// 浮在 Home 上方时的退场：暗幕淡掉露出 Home，内容整体收小一档并淡出。
        /// 不再走此前 Page 层的"逐张收牌 + 背景压黑"反序编排——那套是底下没有页面、
        /// 靠黑场衔接 Home 淡入时用的，现在 Home 一直在下面，收得越干脆越好。
        /// 真正的页面关闭由 View 在 <see cref="ExitDuration"/> 之后执行。
        /// </summary>
        public void PlayExit()
        {
            KillSequences();
            SetInputBlocked(true);

            var seq = NewSequence(_t.Root);
            if (seq == null)
            {
                return;
            }

            _exitSeq = seq;

            if (_mask != null)
            {
                seq.Insert(0f, _mask.DOFade(0f, MaskFadeOut).SetEase(Ease.InQuad));
            }

            if (_body != null)
            {
                seq.Insert(0f, _body.DOScale(_bodyRestScale * PopScaleTo, PopOutDuration).SetEase(Ease.InCubic));
            }

            if (_bodyGroup != null)
            {
                seq.Insert(0f, _bodyGroup.DOFade(0f, PopOutDuration).SetEase(Ease.InQuad));
            }
        }

        /// <summary>英雄卡列表显隐。show 时带快速逐张弹入；hide 时整组淡出后再失活。</summary>
        public void SetHeroPanel(bool show, bool animate)
        {
            var panel = _t.HeroPanel;
            if (panel == null)
            {
                return;
            }

            if (!animate)
            {
                SetPanelImmediate(panel, _heroPanelRestPos, show);
                return;
            }

            if (show && panel.gameObject.activeSelf)
            {
                // 上一次淡出还没播完就又要显示：停掉旧序列，直接补回可见。
                KillSeq(ref _heroPanelSeq);
                panel.DOKill();
                panel.anchoredPosition = _heroPanelRestPos;
                panel.localScale = Vector3.one;
                ResetItems(_t.HeroItems);
                var current = Group(panel);
                if (current != null)
                {
                    current.DOKill();
                    current.alpha = 1f;
                    current.blocksRaycasts = true;
                }

                PlayButtonLikePop(panel);
                return;
            }

            if (!show && !panel.gameObject.activeSelf)
            {
                return;
            }

            KillSeq(ref _heroPanelSeq);
            panel.DOKill();
            var group = Group(panel);

            if (show)
            {
                panel.gameObject.SetActive(true);
                panel.localScale = Vector3.one;
                if (group != null)
                {
                    group.alpha = 0f;
                    group.blocksRaycasts = true;
                }

                panel.anchoredPosition = _heroPanelRestPos + new Vector2(0f, -24f);
                var seq = NewSequence(panel);
                if (seq == null)
                {
                    return;
                }

                _heroPanelSeq = seq;
                if (group != null)
                {
                    seq.Insert(0f, group.DOFade(1f, 0.18f));
                }

                seq.Insert(0f, panel.DOAnchorPos(_heroPanelRestPos, 0.22f).SetEase(Ease.OutCubic));

                for (var i = 0; i < _t.HeroItems.Count; i++)
                {
                    var rt = _t.HeroItems[i];
                    if (rt == null)
                    {
                        continue;
                    }

                    var itemGroup = Group(rt);
                    if (itemGroup != null)
                    {
                        itemGroup.alpha = 0f;
                    }

                    rt.localScale = Vector3.one * 0.90f;
                    rt.localRotation = Quaternion.identity;

                    var delay = 0.04f + Mathf.Min(i, 6) * 0.03f;
                    if (itemGroup != null)
                    {
                        seq.Insert(delay, itemGroup.DOFade(1f, 0.16f));
                    }

                    seq.Insert(delay, rt.DOScale(Vector3.one, 0.16f).SetEase(Ease.OutBack));
                }

                return;
            }

            if (group != null)
            {
                group.blocksRaycasts = false;
            }

            var hideSeq = NewSequence(panel);
            if (hideSeq == null)
            {
                return;
            }

            _heroPanelSeq = hideSeq;
            if (group != null)
            {
                hideSeq.Insert(0f, group.DOFade(0f, 0.14f));
            }

            hideSeq.Insert(0f,
                panel.DOAnchorPos(_heroPanelRestPos + new Vector2(0f, -28f), 0.16f).SetEase(Ease.InCubic));
            hideSeq.OnComplete(() =>
            {
                if (panel == null)
                {
                    return;
                }

                panel.gameObject.SetActive(false);
                panel.anchoredPosition = _heroPanelRestPos;
                panel.localScale = Vector3.one;
                var restored = Group(panel);
                if (restored != null)
                {
                    restored.alpha = 1f;
                    restored.blocksRaycasts = true;
                }
            });
        }

        /// <summary>难度卡列表显隐。show 时纸面从下方滑入、卡片逐张弹出。</summary>
        public void SetLevelPanel(bool show, bool animate)
        {
            var panel = _t.LevelPanel;
            if (panel == null)
            {
                return;
            }

            if (!animate)
            {
                SetPanelImmediate(panel, _levelPanelRestPos, show);
                return;
            }

            if (show && panel.gameObject.activeSelf)
            {
                // 上一次淡出还没播完就又要显示：停掉旧序列，直接补回可见。
                KillSeq(ref _levelPanelSeq);
                panel.DOKill();
                panel.anchoredPosition = _levelPanelRestPos;
                panel.localScale = Vector3.one;
                ResetItems(_t.LevelItems);
                var current = Group(panel);
                if (current != null)
                {
                    current.DOKill();
                    current.alpha = 1f;
                    current.blocksRaycasts = true;
                }

                PlayButtonLikePop(panel);
                return;
            }

            if (!show && !panel.gameObject.activeSelf)
            {
                return;
            }

            KillSeq(ref _levelPanelSeq);
            panel.DOKill();
            var group = Group(panel);

            if (show)
            {
                panel.gameObject.SetActive(true);
                panel.localScale = Vector3.one;
                if (group != null)
                {
                    group.alpha = 0f;
                    group.blocksRaycasts = true;
                }

                panel.anchoredPosition = _levelPanelRestPos + new Vector2(0f, -36f);
                var seq = NewSequence(panel);
                if (seq == null)
                {
                    return;
                }

                _levelPanelSeq = seq;
                if (group != null)
                {
                    seq.Insert(0f, group.DOFade(1f, PanelFade));
                }

                seq.Insert(0f, panel.DOAnchorPos(_levelPanelRestPos, 0.22f).SetEase(Ease.OutCubic));

                for (var i = 0; i < _t.LevelItems.Count; i++)
                {
                    var rt = _t.LevelItems[i];
                    if (rt == null)
                    {
                        continue;
                    }

                    var itemGroup = Group(rt);
                    if (itemGroup != null)
                    {
                        itemGroup.alpha = 0f;
                    }

                    rt.localScale = Vector3.one * 0.82f;
                    rt.localRotation = Quaternion.identity;

                    var delay = 0.12f + Mathf.Min(i, 5) * LevelCardStagger;
                    if (itemGroup != null)
                    {
                        seq.Insert(delay, itemGroup.DOFade(1f, PanelFade));
                    }

                    seq.Insert(delay, rt.DOScale(Vector3.one, PanelFade).SetEase(Ease.OutBack));
                }

                return;
            }

            if (group != null)
            {
                group.blocksRaycasts = false;
            }

            var hideSeq = NewSequence(panel);
            if (hideSeq == null)
            {
                return;
            }

            _levelPanelSeq = hideSeq;
            if (group != null)
            {
                hideSeq.Insert(0f, group.DOFade(0f, 0.14f));
            }

            hideSeq.Insert(0f,
                panel.DOAnchorPos(_levelPanelRestPos + new Vector2(0f, -24f), 0.16f).SetEase(Ease.InCubic));
            hideSeq.OnComplete(() =>
            {
                if (panel == null)
                {
                    return;
                }

                panel.gameObject.SetActive(false);
                panel.anchoredPosition = _levelPanelRestPos;
                panel.localScale = Vector3.one;
                var restored = Group(panel);
                if (restored != null)
                {
                    restored.alpha = 1f;
                    restored.blocksRaycasts = true;
                }
            });
        }

        /// <summary>右下角难度/关卡信息的显隐；出现时带一点“盖章”回弹。</summary>
        public void SetStageInfo(bool show, bool animate)
        {
            var info = _t.StageInfo;
            if (info == null)
            {
                return;
            }

            if (!animate)
            {
                SetStageImmediate(show);
                return;
            }

            if (!show && !info.gameObject.activeSelf)
            {
                return;
            }

            KillSeq(ref _stageInfoSeq);
            info.DOKill();
            if (_t.StageNum != null)
            {
                _t.StageNum.DOKill();
                _t.StageNum.localScale = _stageNumRestScale;
            }

            var group = Group(info);

            if (show)
            {
                info.gameObject.SetActive(true);
                if (group != null)
                {
                    group.alpha = 0f;
                    group.blocksRaycasts = true;
                }

                info.anchoredPosition = _stageRestPos + new Vector2(0f, -12f);
                info.localScale = _stageRestScale * 0.92f;

                var seq = NewSequence(info);
                if (seq == null)
                {
                    return;
                }

                _stageInfoSeq = seq;
                if (group != null)
                {
                    seq.Insert(0.08f, group.DOFade(1f, StageInfoFade));
                }

                seq.Insert(0.08f, info.DOAnchorPos(_stageRestPos, 0.22f).SetEase(Ease.OutCubic));
                seq.Insert(0.08f, info.DOScale(_stageRestScale, 0.22f).SetEase(Ease.OutBack));
                return;
            }

            if (group != null)
            {
                group.blocksRaycasts = false;
            }

            var hideSeq = NewSequence(info);
            if (hideSeq == null)
            {
                return;
            }

            _stageInfoSeq = hideSeq;
            if (group != null)
            {
                hideSeq.Insert(0f, group.DOFade(0f, 0.12f));
            }

            hideSeq.Insert(0f, info.DOScale(_stageRestScale * 0.94f, 0.12f).SetEase(Ease.InQuad));
            hideSeq.OnComplete(() =>
            {
                if (info == null)
                {
                    return;
                }

                info.gameObject.SetActive(false);
                info.anchoredPosition = _stageRestPos;
                info.localScale = _stageRestScale;
                var restored = Group(info);
                if (restored != null)
                {
                    restored.alpha = 1f;
                    restored.blocksRaycasts = true;
                }
            });
        }

        /// <summary>按钮淡入。只改 alpha，不动 scale/position，避免 ButtonAnim 缓存到动画中间态。</summary>
        public void PlayButtonIn(RectTransform button)
        {
            if (button == null || !button.gameObject.activeInHierarchy)
            {
                return;
            }

            var group = Group(button);
            if (group == null)
            {
                return;
            }

            group.DOKill();
            group.alpha = 0f;
            group.DOFade(1f, ButtonFade)
                .SetUpdate(true)
                .SetLink(button.gameObject, LinkBehaviour.KillOnDestroy);
        }

        /// <summary>换英雄：大卡压一下、技能信息快速淡一下，掩掉文案瞬变。</summary>
        public void PlayHeroChanged()
        {
            if (_t.HeroCard != null)
            {
                _t.HeroCard.DOKill();
                _t.HeroCard.localScale = _cardRestScale;
                _t.HeroCard.DOPunchScale(new Vector3(-0.04f, -0.04f, 0f), 0.20f, 8, 0.8f)
                    .SetUpdate(true)
                    .SetLink(_t.HeroCard.gameObject, LinkBehaviour.KillOnDestroy);
            }

            if (_t.RoleInfo == null)
            {
                return;
            }

            var group = Group(_t.RoleInfo);
            if (group == null)
            {
                return;
            }

            KillSeq(ref _heroChangedSeq);
            group.DOKill();
            var seq = NewSequence(_t.RoleInfo);
            if (seq == null)
            {
                return;
            }

            _heroChangedSeq = seq;
            seq.Append(group.DOFade(0.45f, 0.06f));
            seq.Append(group.DOFade(1f, 0.10f));
        }

        /// <summary>换难度/关卡：进度标题轻弹一下。</summary>
        public void PlayStageChanged()
        {
            if (_t.StageNum == null || _t.StageInfo == null || !_t.StageInfo.gameObject.activeInHierarchy)
            {
                return;
            }

            _t.StageNum.DOKill();
            _t.StageNum.localScale = _stageNumRestScale;
            _t.StageNum.DOPunchScale(new Vector3(0.06f, 0.06f, 0f), 0.18f, 6, 0.7f)
                .SetUpdate(true)
                .SetLink(_t.StageNum.gameObject, LinkBehaviour.KillOnDestroy);
        }

        /// <summary>点未解锁难度：右下信息左右抖一下。</summary>
        public void PlayLockedShake()
        {
            if (_t.StageInfo == null || !_t.StageInfo.gameObject.activeInHierarchy)
            {
                return;
            }

            KillSeq(ref _stageInfoSeq);
            _t.StageInfo.DOKill();
            _t.StageInfo.localScale = _stageRestScale;
            _t.StageInfo.anchoredPosition = _stageRestPos;
            if (_t.StageNum != null)
            {
                _t.StageNum.DOKill();
                _t.StageNum.localScale = _stageNumRestScale;
            }

            var group = Group(_t.StageInfo);
            if (group != null)
            {
                group.DOKill();
                group.alpha = 1f;
                group.blocksRaycasts = true;
            }

            _t.StageInfo.DOShakeAnchorPos(0.18f, 10f, 10, 90f, false, true)
                .SetUpdate(true)
                .SetLink(_t.StageInfo.gameObject, LinkBehaviour.KillOnDestroy);
        }

        /// <summary>关闭前停掉所有补间并把池化卡牌恢复成静止态。</summary>
        public void Kill()
        {
            KillSequences();
            if (_rootGroup != null)
            {
                _rootGroup.blocksRaycasts = true;
                _rootGroup.interactable = true;
            }

            ResetItems(_t.HeroItems);
            ResetItems(_t.LevelItems);
        }

        /// <summary>
        /// 入场/退场期间挡住输入，避免动画中途点按钮/选卡。
        /// 用 CanvasGroup.interactable 而不是 blocksRaycasts：LevelUI 现在浮在 Home 上方，
        /// 若关掉射线接收，点击会穿透到下面的 Home 按钮上；所以整块界面始终接收射线
        /// （全屏暗幕兜底），只是动画期间暂时不可交互。
        /// </summary>
        public void SetInputBlocked(bool blocked)
        {
            if (_rootGroup == null)
            {
                return;
            }

            _rootGroup.blocksRaycasts = true;
            _rootGroup.interactable = !blocked;
        }

        private void CacheRest()
        {
            if (_t.Hor != null)
            {
                _horRestScale = _t.Hor.localScale;
                _horRestPos = _t.Hor.anchoredPosition;
            }

            if (_t.HeroCard != null)
            {
                _cardRestScale = _t.HeroCard.localScale;
            }

            if (_t.RoleInfo != null)
            {
                _roleRestPos = _t.RoleInfo.anchoredPosition;
            }

            if (_t.StageInfo != null)
            {
                _stageRestPos = _t.StageInfo.anchoredPosition;
                _stageRestScale = _t.StageInfo.localScale;
            }

            if (_t.HeroPanel != null)
            {
                _heroPanelRestPos = _t.HeroPanel.anchoredPosition;
            }

            if (_t.LevelPanel != null)
            {
                _levelPanelRestPos = _t.LevelPanel.anchoredPosition;
            }

            if (_t.StageNum != null)
            {
                _stageNumRestScale = _t.StageNum.localScale;
            }
        }

        /// <summary>
        /// 搭出"弹窗壳"，只创建一次、随界面一起销毁（预制体只有内容节点，不动预制体）：
        /// 1）内容容器：把根节点原有的子节点整体收进来，只对它做弹出/收起与整体淡入淡出，
        ///    这样遮罩不会被内容一起缩放；所有子节点引用都在 OnBind 早期取好，搬父节点不影响绑定。
        /// 2）全屏暗幕：压暗下面仍然可见的 Home，并把它挡在交互之外（打开时淡入、关闭时撤掉）。
        /// </summary>
        private void EnsurePopupShell()
        {
            if (_t.Root == null)
            {
                return;
            }

            if (_body == null)
            {
                _body = _t.Root.Find(BodyNodeName) as RectTransform;
            }

            if (_body == null)
            {
                var bodyGo = new GameObject(BodyNodeName, typeof(RectTransform));
                _body = (RectTransform)bodyGo.transform;
                _body.SetParent(_t.Root, false);
                _body.gameObject.layer = _t.Root.gameObject.layer;
                Stretch(_body);
                _body.SetAsFirstSibling();

                // 先收集再搬，边搬边遍历会让 childCount 变化而漏节点；顺序保持不变。
                var moving = new List<Transform>(_t.Root.childCount);
                for (var i = 0; i < _t.Root.childCount; i++)
                {
                    var child = _t.Root.GetChild(i);
                    if (child != _body)
                    {
                        moving.Add(child);
                    }
                }

                for (var i = 0; i < moving.Count; i++)
                {
                    moving[i].SetParent(_body, false);
                }

                // 只在创建时缓存静止缩放：入场会把容器收小一档，重复调用时不能再拿当前值当静止值。
                _bodyRestScale = _body.localScale;
            }

            if (_bodyGroup == null)
            {
                _bodyGroup = _body.GetComponent<CanvasGroup>();
                if (_bodyGroup == null)
                {
                    _bodyGroup = _body.gameObject.AddComponent<CanvasGroup>();
                }
            }

            if (_mask == null)
            {
                var maskT = _t.Root.Find(MaskNodeName);
                _mask = maskT != null ? maskT.GetComponent<Image>() : null;
                if (_mask == null)
                {
                    var maskGo = new GameObject(
                        MaskNodeName,
                        typeof(RectTransform),
                        typeof(CanvasRenderer),
                        typeof(Image));
                    var maskRect = (RectTransform)maskGo.transform;
                    maskRect.SetParent(_t.Root, false);
                    maskGo.layer = _t.Root.gameObject.layer;
                    Stretch(maskRect);
                    _mask = maskGo.GetComponent<Image>();
                    // 无 sprite 的 Image 画纯色块，和 SettingPop / TTRewardPop 的 Mask 一致；
                    // raycastTarget 常开：即使暗幕透明也要挡住 Home 的点击。
                    _mask.raycastTarget = true;
                    SetMaskAlpha(0f);
                }
            }

            // 暗幕永远画在内容容器下面。
            _mask.rectTransform.SetAsFirstSibling();
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.localScale = Vector3.one;
            rect.localRotation = Quaternion.identity;
        }

        private void SetMaskAlpha(float alpha)
        {
            if (_mask == null)
            {
                return;
            }

            var color = _mask.color;
            color.r = 0f;
            color.g = 0f;
            color.b = 0f;
            color.a = alpha;
            _mask.color = color;
        }

        private void EnsureGroups()
        {
            Group(_t.Root);
            Group(_t.Hor);
            Group(_t.RoleInfo);
            Group(_t.StageInfo);
            Group(_t.HeroPanel);
            Group(_t.LevelPanel);
            Group(_t.LastBtn);
            Group(_t.UseHeroBtn);
            Group(_t.UnlockHeroBtn);
            Group(_t.StartGameBtn);

            for (var i = 0; i < _t.HeroItems.Count; i++)
            {
                Group(_t.HeroItems[i]);
            }

            for (var i = 0; i < _t.LevelItems.Count; i++)
            {
                Group(_t.LevelItems[i]);
            }
        }

        private CanvasGroup Group(RectTransform rect)
        {
            if (rect == null)
            {
                return null;
            }

            if (_groups.TryGetValue(rect, out var cached) && cached != null)
            {
                return cached;
            }

            var group = rect.GetComponent<CanvasGroup>();
            if (group == null)
            {
                group = rect.gameObject.AddComponent<CanvasGroup>();
            }

            _groups[rect] = group;
            return group;
        }

        private void PrepareHeroPanelStart()
        {
            var panel = _t.HeroPanel;
            if (panel == null)
            {
                return;
            }

            panel.gameObject.SetActive(true);
            var group = Group(panel);
            if (group != null)
            {
                group.alpha = 0f;
                group.blocksRaycasts = true;
            }

            panel.anchoredPosition = _heroPanelRestPos + new Vector2(0f, -26f);

            for (var i = 0; i < _t.HeroItems.Count; i++)
            {
                var rt = _t.HeroItems[i];
                if (rt == null)
                {
                    continue;
                }

                var itemGroup = Group(rt);
                if (itemGroup != null)
                {
                    itemGroup.alpha = 0f;
                }

                rt.localScale = Vector3.one * 0.84f;
                var tilt = ((i % 4) - 1.5f) * 2f;
                rt.localRotation = Quaternion.Euler(0f, 0f, tilt);
            }
        }

        private void PrepareLevelCardsStart()
        {
            var panel = _t.LevelPanel;
            if (panel == null)
            {
                return;
            }

            panel.gameObject.SetActive(true);
            var group = Group(panel);
            if (group != null)
            {
                group.alpha = 0f;
                group.blocksRaycasts = true;
            }

            panel.anchoredPosition = _levelPanelRestPos + new Vector2(0f, -36f);

            for (var i = 0; i < _t.LevelItems.Count; i++)
            {
                var rt = _t.LevelItems[i];
                if (rt == null)
                {
                    continue;
                }

                var itemGroup = Group(rt);
                if (itemGroup != null)
                {
                    itemGroup.alpha = 0f;
                }

                rt.localScale = Vector3.one * 0.82f;
                rt.localRotation = Quaternion.identity;
            }
        }

        private void PrepareStageInfoStart()
        {
            var info = _t.StageInfo;
            if (info == null)
            {
                return;
            }

            info.gameObject.SetActive(true);
            var group = Group(info);
            if (group != null)
            {
                group.alpha = 0f;
                group.blocksRaycasts = true;
            }

            info.anchoredPosition = _stageRestPos + new Vector2(0f, -12f);
            info.localScale = _stageRestScale * 0.92f;
        }

        private void AppendHeroPanelIn(Sequence seq, float at)
        {
            var panel = _t.HeroPanel;
            if (panel == null)
            {
                return;
            }

            var group = Group(panel);
            if (group != null)
            {
                seq.Insert(at, group.DOFade(1f, 0.22f));
            }

            seq.Insert(at, panel.DOAnchorPos(_heroPanelRestPos, 0.28f).SetEase(Ease.OutCubic));

            for (var i = 0; i < _t.HeroItems.Count; i++)
            {
                var rt = _t.HeroItems[i];
                if (rt == null)
                {
                    continue;
                }

                var itemGroup = Group(rt);
                var delay = at + 0.04f + Mathf.Min(i, 5) * CardStagger;
                if (itemGroup != null)
                {
                    seq.Insert(delay, itemGroup.DOFade(1f, 0.22f));
                }

                seq.Insert(delay, rt.DOScale(Vector3.one, 0.22f).SetEase(Ease.OutBack));
                seq.Insert(delay, rt.DORotateQuaternion(Quaternion.identity, 0.22f).SetEase(Ease.OutCubic));
            }
        }

        private void AppendStageIn(Sequence seq, float at)
        {
            var panel = _t.LevelPanel;
            if (panel != null)
            {
                var group = Group(panel);
                if (group != null)
                {
                    seq.Insert(at + 0.06f, group.DOFade(1f, 0.22f));
                }

                seq.Insert(at + 0.06f, panel.DOAnchorPos(_levelPanelRestPos, 0.22f).SetEase(Ease.OutCubic));
            }

            for (var i = 0; i < _t.LevelItems.Count; i++)
            {
                var rt = _t.LevelItems[i];
                if (rt == null)
                {
                    continue;
                }

                var itemGroup = Group(rt);
                var delay = at + 0.12f + Mathf.Min(i, 5) * LevelCardStagger;
                if (itemGroup != null)
                {
                    seq.Insert(delay, itemGroup.DOFade(1f, PanelFade));
                }

                seq.Insert(delay, rt.DOScale(Vector3.one, PanelFade).SetEase(Ease.OutBack));
            }

            var info = _t.StageInfo;
            if (info == null)
            {
                return;
            }

            var infoGroup = Group(info);
            if (infoGroup != null)
            {
                seq.Insert(at + 0.10f, infoGroup.DOFade(1f, StageInfoFade));
            }

            seq.Insert(at + 0.10f, info.DOAnchorPos(_stageRestPos, 0.22f).SetEase(Ease.OutCubic));
            seq.Insert(at + 0.10f, info.DOScale(_stageRestScale, 0.22f).SetEase(Ease.OutBack));
        }

        private void SetPanelImmediate(RectTransform panel, Vector2 restPos, bool show)
        {
            if (panel == null)
            {
                return;
            }

            panel.gameObject.SetActive(show);
            panel.anchoredPosition = restPos;
            panel.localScale = Vector3.one;
            var group = Group(panel);
            if (group != null)
            {
                group.alpha = show ? 1f : 0f;
                group.blocksRaycasts = show;
            }
        }

        private void SetStageImmediate(bool show)
        {
            var info = _t.StageInfo;
            if (info == null)
            {
                return;
            }

            if (_t.StageNum != null)
            {
                _t.StageNum.DOKill();
                _t.StageNum.localScale = _stageNumRestScale;
            }

            info.gameObject.SetActive(show);
            info.anchoredPosition = _stageRestPos;
            info.localScale = _stageRestScale;
            var group = Group(info);
            if (group != null)
            {
                group.alpha = show ? 1f : 0f;
                group.blocksRaycasts = show;
            }
        }

        private void ResetItems(List<RectTransform> items)
        {
            if (items == null)
            {
                return;
            }

            for (var i = 0; i < items.Count; i++)
            {
                var rt = items[i];
                if (rt == null)
                {
                    continue;
                }

                rt.localScale = Vector3.one;
                rt.localRotation = Quaternion.identity;
                var group = Group(rt);
                if (group != null)
                {
                    group.alpha = 1f;
                    group.blocksRaycasts = true;
                }
            }
        }

        private void PlayButtonLikePop(RectTransform target)
        {
            // 已经在显示中：只做一次很轻的缩放回弹，不重播整段。
            target.DOKill();
            var restScale = target.localScale;
            target.DOScale(restScale * 0.98f, 0.06f)
                .SetEase(Ease.OutQuad)
                .SetUpdate(true)
                .SetLink(target.gameObject, LinkBehaviour.KillOnDestroy)
                .OnComplete(() =>
                {
                    target.DOScale(restScale, 0.10f)
                        .SetEase(Ease.OutBack)
                        .SetUpdate(true)
                        .SetLink(target.gameObject, LinkBehaviour.KillOnDestroy);
                });
        }

        private Sequence NewSequence(RectTransform owner)
        {
            if (owner == null)
            {
                return null;
            }

            return DOTween.Sequence()
                .SetUpdate(true)
                .SetLink(owner.gameObject, LinkBehaviour.KillOnDestroy);
        }

        private void KillSequences()
        {
            KillSeq(ref _entranceSeq);
            KillSeq(ref _exitSeq);
            KillSeq(ref _heroPanelSeq);
            KillSeq(ref _levelPanelSeq);
            KillSeq(ref _stageInfoSeq);
            KillSeq(ref _heroChangedSeq);

            foreach (var pair in _groups)
            {
                if (pair.Value != null)
                {
                    pair.Value.DOKill();
                }
            }
        }

        private static void KillSeq(ref Sequence seq)
        {
            if (seq != null)
            {
                if (seq.IsActive())
                {
                    seq.Kill();
                }

                seq = null;
            }
        }

        private static RectTransform ActiveButton(RectTransform first, RectTransform second)
        {
            if (first != null && first.gameObject.activeSelf)
            {
                return first;
            }

            if (second != null && second.gameObject.activeSelf)
            {
                return second;
            }

            return null;
        }
    }
}
