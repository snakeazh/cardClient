using System;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

namespace App.UI
{
    /// <summary>
    /// 纸面逐格展开入场动画的一个内容块步骤（按 Content 布局顺序传入）。
    /// </summary>
    public sealed class PaperRevealStep
    {
        /// <summary>块根节点，须为 Content 的直接子节点（ inactive 的子节点由宿主自行跳过）。</summary>
        public RectTransform Rect;

        /// <summary>
        /// 行块标记：true 进串行链逐行播（上一行播完才播下一行），false 为静态块开局同帧齐现。
        /// 淡入时刻与空间位置是两个维度——布局上位于行下方的静态块开局虽已淡入，
        /// 仍会被 Viewport 蒙罩挡到行链播完才真正露出。
        /// </summary>
        public bool IsRow;

        /// <summary>纸面开到该块区域时触发（行链播完；无行时为静态块淡入完），如启动数字滚动。</summary>
        public Action OnShown;
    }

    /// <summary>
    /// 纸面逐格展开入场动画（纯运行时代码，预制体不用改）。适用于
    /// BG(九宫格纸底) → Title → Scroll View → Viewport(蒙罩) → Content(VerticalLayoutGroup 自上而下)
    /// 结构的弹窗。节奏：面板自下方滑入淡现 → 标题回落 → 静态块开局齐现（BG 只长到首行前
    /// 静态块的底部）→ 行块逐行串行、BG 随行逐格展开 → 行链播完一次补长到剩余区域（OnShown 同刻
    /// 触发）。播完后宿主每帧 LateUpdate 调 <see cref="TickFitBgHeight"/> 托底 BG 高度。
    /// 约束：BG 若挂 TallScreenFitScale 类组件会写 localScale，面板不能做缩放动画；Content 由
    /// 布局组驱动，子块只做淡入不做位移；按钮不缩放（ButtonAnim 首次按下缓存 localScale 作静止姿态）。
    /// </summary>
    public sealed class PaperRevealAnim
    {
        /// <summary>
        /// BG 是否随内容逐格长高（自适应高度的纸底弹窗用）。固定尺寸纸底——汇总区/按钮摆在
        /// 滚动区外、BG 高度由美术手摆——应设 false：不做任何 sizeDelta 动画与托底，
        /// 只保留面板滑入/标题回落/块淡入/行链串行。
        /// </summary>
        public bool GrowBg = true;

        /// <summary>面板自下方滑入的距离（px）。</summary>
        public float PanelSlideDistance = 60f;

        /// <summary>面板淡入时长（秒）。</summary>
        public float PanelFadeDuration = 0.3f;

        /// <summary>面板滑入时长（秒）。</summary>
        public float PanelSlideDuration = 0.4f;

        /// <summary>标题回落相对面板入场的延迟（秒）。</summary>
        public float TitleDelay = 0.15f;

        /// <summary>标题自上方落下的距离（px）。</summary>
        public float TitleDropDistance = 30f;

        /// <summary>标题淡入时长（秒）。</summary>
        public float TitleFadeDuration = 0.25f;

        /// <summary>标题落下时长（秒，OutBack 回弹）。</summary>
        public float TitleDropDuration = 0.35f;

        /// <summary>静态块开局淡入的延迟（秒）。</summary>
        public float BlocksDelay = 0.32f;

        /// <summary>单块淡入时长（秒），也是行链的步进间隔。</summary>
        public float BlockFadeDuration = 0.32f;

        /// <summary>
        /// 播完后 BG 高度托底纠正的补间时长：入场中数字滚动改 TMP 文本会触发布局重算，
        /// Content 高度可能与入场测量值有偏差，平滑补齐差值，避免一帧写死造成突兀跳变。
        /// </summary>
        public float BgFitTweenDuration = 0.25f;

        private readonly RectTransform _bg;
        private readonly RectTransform _title;
        private readonly RectTransform _content;
        private readonly float _bottomEdge;
        private readonly List<CanvasGroup> _playedGroups = new List<CanvasGroup>();
        private CanvasGroup _bgGroup;
        private CanvasGroup _titleGroup;
        private Vector2 _bgRestPos;
        private Vector2 _titleRestPos;
        private float _fullTargetY;
        private bool _suppressBgFit;
        private Sequence _entranceSeq;
        private Tween _bgFitTween;

        /// <summary>是否正在播放入场动画。</summary>
        public bool IsPlaying => _entranceSeq != null && _entranceSeq.IsActive();

        /// <param name="bg">纸底 BG（居中锚点/轴心，动画只改其 alpha/anchoredPosition/sizeDelta）。</param>
        /// <param name="title">标题横幅；无标题的界面可传 null。</param>
        /// <param name="content">滚动区 Content（VLG 驱动、顶锚，蒙罩从上往下揭）。</param>
        /// <param name="bottomEdge">BG 底部预留高度 = 背景图九宫格下边框厚度。</param>
        public PaperRevealAnim(RectTransform bg, RectTransform title, RectTransform content, float bottomEdge)
        {
            _bg = bg;
            _title = title;
            _content = content;
            _bottomEdge = bottomEdge;
        }

        /// <summary>
        /// 播放入场动画。steps 须按 Content 的兄弟顺序（即布局显示顺序）传入；块可能是宿主当帧
        /// 刚克隆的，布局还没刷过，Play 内部会先强制重建再量各块高度。重复调用会先杀掉旧序列。
        /// </summary>
        public void Play(IReadOnlyList<PaperRevealStep> steps)
        {
            if (_bg == null || _content == null)
            {
                return;
            }

            KillSequence();
            LayoutRebuilder.ForceRebuildLayoutImmediate(_content);

            var count = steps.Count;
            var groups = new CanvasGroup[count];
            var heights = new float[count];
            for (var i = 0; i < count; i++)
            {
                groups[i] = GetOrAddCanvasGroup(steps[i].Rect);
                heights[i] = steps[i].Rect != null ? steps[i].Rect.rect.height : 0f;
            }

            var vlg = _content.GetComponent<VerticalLayoutGroup>();
            var spacing = vlg != null ? vlg.spacing : 0f;
            var contentHeight = _title != null ? _title.rect.height + _content.rect.height : _content.rect.height;
            _fullTargetY = contentHeight + _bottomEdge;
            var expandTotal = 0f;
            for (var i = 0; i < count; i++)
            {
                expandTotal += heights[i] + (i > 0 ? spacing : 0f);
            }

            // 入场期间接管 BG 高度：初始只留标题区，随后逐段长高，播完回到托底逻辑。
            // GrowBg=false（固定尺寸纸底）时完全不动 sizeDelta。
            var initialY = GrowBg ? Mathf.Max(0f, _fullTargetY - expandTotal) : 0f;
            _suppressBgFit = true;
            _playedGroups.Clear();

            _bgGroup = GetOrAddCanvasGroup(_bg);
            _bgRestPos = _bg.anchoredPosition;
            _bgGroup.alpha = 0f;
            _bgGroup.blocksRaycasts = false;
            _bg.anchoredPosition = _bgRestPos + new Vector2(0f, -PanelSlideDistance);
            if (GrowBg)
            {
                _bg.sizeDelta = new Vector2(_bg.sizeDelta.x, initialY);
            }

            _entranceSeq = DOTween.Sequence().SetUpdate(true);
            _entranceSeq.Insert(0f, _bgGroup.DOFade(1f, PanelFadeDuration).SetEase(Ease.OutQuad));
            _entranceSeq.Insert(0f, _bg.DOAnchorPos(_bgRestPos, PanelSlideDuration).SetEase(Ease.OutCubic));

            if (_title != null)
            {
                _titleGroup = GetOrAddCanvasGroup(_title);
                _titleRestPos = _title.anchoredPosition;
                _titleGroup.alpha = 0f;
                _title.anchoredPosition = _titleRestPos + new Vector2(0f, TitleDropDistance);
                _entranceSeq.Insert(TitleDelay, _titleGroup.DOFade(1f, TitleFadeDuration).SetEase(Ease.OutQuad));
                _entranceSeq.Insert(TitleDelay, _title.DOAnchorPos(_titleRestPos, TitleDropDuration).SetEase(Ease.OutBack));
            }

            var bgWidth = _bg.sizeDelta.x;
            var revealsAtRowEnd = new List<Action>();

            // GrowTarget = 恰好露出该块底部时的 BG 高度（布局顺序累计，与各块淡入时刻解耦）：
            // Viewport 蒙罩从上往下揭，可见范围只取决于"到此为止的块总高"。
            var growTargets = new float[count];
            var cumulative = initialY;
            for (var i = 0; i < count; i++)
            {
                cumulative += heights[i] + (i > 0 ? spacing : 0f);
                growTargets[i] = cumulative;
            }

            var firstRowIndex = -1;
            for (var i = 0; i < count; i++)
            {
                if (steps[i].IsRow)
                {
                    firstRowIndex = i;
                    break;
                }
            }

            // 静态块开局同帧一起淡入；行下方各块此刻仍被蒙罩挡着，行链播完纸面开到那里才露出，
            // 因此它们的 OnShown 排在行链末尾触发。
            for (var i = 0; i < count; i++)
            {
                if (steps[i].IsRow || groups[i] == null)
                {
                    continue;
                }

                var group = groups[i];
                group.alpha = 0f;
                group.blocksRaycasts = false;
                _playedGroups.Add(group);
                _entranceSeq.Insert(BlocksDelay, group.DOFade(1f, BlockFadeDuration).SetEase(Ease.OutQuad)
                    .OnComplete(() => group.blocksRaycasts = true)
                    .SetLink(group.gameObject, LinkBehaviour.KillOnDestroy));
                if (steps[i].OnShown != null)
                {
                    revealsAtRowEnd.Add(steps[i].OnShown);
                }
            }

            // 开局长高的只有行上方静态块。行下方静态块的高度此刻计入会把行区纸面过早掀开成
            // 大段空白，须等行链播完再长；无行时全部静态块都算行上方，开局一次长满。
            var grownTo = initialY;
            if (firstRowIndex < 0)
            {
                grownTo = count > 0 ? growTargets[count - 1] : initialY;
            }
            else if (firstRowIndex > 0)
            {
                grownTo = growTargets[firstRowIndex - 1];
            }

            if (GrowBg && grownTo > initialY)
            {
                _entranceSeq.Insert(BlocksDelay, _bg.DOSizeDelta(new Vector2(bgWidth, grownTo), BlockFadeDuration).SetEase(Ease.OutQuad));
            }
            // 行块逐行串行：上一行播完下一行才开始，BG 随每行逐格展开。
            var cursor = BlocksDelay + BlockFadeDuration;
            for (var i = 0; i < count; i++)
            {
                if (!steps[i].IsRow || groups[i] == null)
                {
                    continue;
                }

                var group = groups[i];
                group.alpha = 0f;
                group.blocksRaycasts = false;
                _playedGroups.Add(group);
                var at = cursor;
                cursor += BlockFadeDuration;
                // 播放途中宿主可能重建行列表（如双倍提现）销毁行对象，SetLink 联动杀掉 tween，
                // 避免写已销毁的 CanvasGroup。
                _entranceSeq.Insert(at, group.DOFade(1f, BlockFadeDuration).SetEase(Ease.OutQuad)
                    .OnComplete(() => group.blocksRaycasts = true)
                    .SetLink(group.gameObject, LinkBehaviour.KillOnDestroy));
                if (GrowBg)
                {
                    grownTo = growTargets[i];
                    _entranceSeq.Insert(at, _bg.DOSizeDelta(new Vector2(bgWidth, grownTo), BlockFadeDuration).SetEase(Ease.OutQuad));
                }
            }

            // 行链播完，BG 一次补长到行下方静态块区域，与 OnShown 同刻；无行时开局已一次长满，不补。
            var fullGrownTarget = count > 0 ? growTargets[count - 1] : initialY;
            if (GrowBg && fullGrownTarget > grownTo + 0.01f)
            {
                _entranceSeq.Insert(cursor, _bg.DOSizeDelta(new Vector2(bgWidth, fullGrownTarget), BlockFadeDuration).SetEase(Ease.OutQuad));
            }

            // 纸面正好开到汇总/按钮区域（无行时为静态块淡入完），此刻触发各块的 OnShown。
            for (var i = 0; i < revealsAtRowEnd.Count; i++)
            {
                _entranceSeq.InsertCallback(cursor, new TweenCallback(revealsAtRowEnd[i]));
            }

            // BG 的 blocksRaycasts=false 会让整个子树（含按钮）对射线透明，正常播完也必须恢复，
            // 否则按钮收不到指针事件、点纸面反而触发宿主的蒙层关窗。
            _entranceSeq.OnComplete(() =>
            {
                if (_bgGroup != null)
                {
                    _bgGroup.blocksRaycasts = true;
                }

                _suppressBgFit = false;
            });
        }

        /// <summary>
        /// 中断动画并把 BG/标题/各块复位到播完状态（避免残留半透明或错位）；视图关闭/池化复用前必须调用。
        /// </summary>
        public void Kill()
        {
            KillSequence();
            _suppressBgFit = false;
            if (_bgFitTween != null && _bgFitTween.IsActive())
            {
                _bgFitTween.Kill();
            }

            _bgFitTween = null;
            if (_bg != null && _fullTargetY > 0f)
            {
                // 滑入位移总要复位；sizeDelta 只在自适应纸底（GrowBg）下被动画动过才复位，
                // 固定尺寸纸底不能写（会覆盖美术手摆高度）。
                _bg.anchoredPosition = _bgRestPos;
                if (GrowBg)
                {
                    _bg.sizeDelta = new Vector2(_bg.sizeDelta.x, _fullTargetY);
                }
            }

            if (_bgGroup != null)
            {
                _bgGroup.alpha = 1f;
                _bgGroup.blocksRaycasts = true;
            }

            if (_titleGroup != null)
            {
                _titleGroup.alpha = 1f;
            }

            if (_title != null && _fullTargetY > 0f)
            {
                _title.anchoredPosition = _titleRestPos;
            }

            for (var i = 0; i < _playedGroups.Count; i++)
            {
                if (_playedGroups[i] != null)
                {
                    _playedGroups[i].alpha = 1f;
                    _playedGroups[i].blocksRaycasts = true;
                }
            }

            _playedGroups.Clear();
        }

        /// <summary>
        /// BG 高度托底，宿主在 LateUpdate 每帧调用：BG 高度 = 标题高 + Content 高 + 底部边框。
        /// 入场动画逐段展开期间内部抑制（交给动画驱动），播完自动恢复；Content 高度变化
        /// （数字滚动改文本触发重算）走短补间平滑补齐。
        /// </summary>
        public void TickFitBgHeight()
        {
            if (!GrowBg)
            {
                // 固定尺寸纸底：高度由美术手摆，不做托底。
                return;
            }

            if (_suppressBgFit)
            {
                // 入场动画正在逐段展开 BG 高度，交给动画驱动，播完自动恢复。
                return;
            }

            if (_bg == null || _content == null)
            {
                return;
            }

            var target = (_title != null ? _title.rect.height : 0f) + _content.rect.height + _bottomEdge;
            if (Mathf.Abs(target - _bg.rect.height) <= 0.5f)
            {
                return;
            }

            if (_bgFitTween != null && _bgFitTween.IsActive())
            {
                // 已在补间中，等它到位后下一帧再校验，避免反复重启。
                return;
            }

            _bgFitTween = _bg.DOSizeDelta(new Vector2(_bg.sizeDelta.x, target), BgFitTweenDuration)
                .SetEase(Ease.OutQuad)
                .SetUpdate(true)
                .SetLink(_bg.gameObject, LinkBehaviour.KillOnDestroy);
        }

        private void KillSequence()
        {
            if (_entranceSeq != null)
            {
                if (_entranceSeq.IsActive())
                {
                    _entranceSeq.Kill();
                }

                _entranceSeq = null;
            }
        }

        private static CanvasGroup GetOrAddCanvasGroup(RectTransform rect)
        {
            if (rect == null)
            {
                return null;
            }

            var group = rect.GetComponent<CanvasGroup>();
            return group != null ? group : rect.gameObject.AddComponent<CanvasGroup>();
        }
    }
}
