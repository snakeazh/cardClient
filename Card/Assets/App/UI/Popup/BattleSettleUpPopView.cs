using System.Collections.Generic;
using System.Threading.Tasks;
using App.Resources;
using DG.Tweening;
using Framework.UI.Core;
using Framework.UI.Navigation;
using Framework.UI.View;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace App.UI.Popup
{
    /// <summary>
    /// 关卡结算弹窗。节点通过 UIReference / UIBind 解析。
    /// WithDrawBtn / DoubleBtn 在按钮处散落 coinitem，停留 0.5 秒后飞向 GameResourceBar 金币图标。
    /// </summary>
    [AutoScreen(AppScreenIds.BattleSettleUpPop, UILayer.Popup, ResResourcePaths.BattleSettleUpPop)]
    public sealed class BattleSettleUpPopView : ViewBase<BattleSettleUpPopViewModel>
    {
        // BG 底部预留 = 背景图九宫格下边框厚度（LevelSettlementBaseFrame spriteBorder.w = 116）
        public float BgBottomEdge = 116f;

        // 入场动画节奏：面板自下方滑入淡现 → 标题回落 → 静态块（表头/汇总/公式/按钮）开局一起淡入，
        // BG 只长到表头底部 → Round_N 行逐行串行淡入（上一行播完才播下一行），BG 随每行逐格展开 →
        // 行链播完 BG 一次补长到汇总/按钮区（数字滚动同刻触发）；全部播完后恢复 FitBgHeight 托底。
        // BG 长高量必须按布局顺序累计（Viewport 蒙罩从上往下揭，可见范围只看"到此为止的块总高"）：
        // 行下方静态块的高度提前计入，会把行区纸面过早掀开成大段空白。
        // BG 挂 TallScreenFitScale 会写 localScale，面板不做缩放动画；Content 由 VerticalLayoutGroup
        // 驱动，子块只做淡入不做位移；按钮不缩放（ButtonAnim 首次按下会缓存 localScale 当静止姿态）。
        private const float PanelSlideDistance = 60f;
        private const float PanelFadeDuration = 0.3f;
        private const float PanelSlideDuration = 0.4f;
        private const float TitleDelay = 0.15f;
        private const float TitleDropDistance = 30f;
        private const float TitleFadeDuration = 0.25f;
        private const float TitleDropDuration = 0.35f;
        private const float BlocksDelay = 0.32f;
        private const float BlockFadeDuration = 0.32f;

        // FitBgHeight 的高度纠正走短补间：入场期间数字滚动会改 TMP 文本触发布局重算，
        // Content 高度可能与入场测量值有偏差，BG 平滑补齐差值，避免一帧写死造成突兀跳变。
        private const float BgFitTweenDuration = 0.25f;

        private readonly List<GameObject> _roundRows = new List<GameObject>();
        private readonly List<CanvasGroup> _blockGroups = new List<CanvasGroup>();
        private readonly List<RevealStep> _revealSteps = new List<RevealStep>();
        private GameObject _roundTemplate;
        private RectTransform _bgRect;
        private RectTransform _titleRect;
        private RectTransform _contentRect;
        private CanvasGroup _bgGroup;
        private CanvasGroup _titleGroup;
        private Vector2 _bgRestPos;
        private Vector2 _titleRestPos;
        private float _bgFullTargetY;
        private bool _suppressBgFit;
        private Sequence _entranceSeq;
        private Tween _bgFitTween;
        private RollingNumber _rollMonsterCount;
        private RollingNumber _rollTotalDamage;
        private RollingNumber _rollReward;
        private RollingNumber _rollWithdraw;
        private bool _entrancePlayed;
        private Button _withdrawBtn;
        private Button _doubleBtn;
        private GameObject _coinPrefab;
        private Sequence _coinSeq;
        private int _playToken;

        protected override void OnBind()
        {
            _entrancePlayed = false;
            // 四个结算数字走滚动展示：绑定时归零待命，所属内容块入场显现时再滚到目标值。
            _rollMonsterCount = BindRollingNumber(GetNode<TMP_Text>("CurScoreNum"), ViewModel.CurScoreNum);
            _rollTotalDamage = BindRollingNumber(GetNode<TMP_Text>("TotalScoreNum"), ViewModel.TotalScoreNum);
            _rollReward = BindRollingNumber(GetNode<TMP_Text>("CoinNum"), ViewModel.CoinNum);
            _rollWithdraw = BindRollingNumber(GetNode<TMP_Text>("Num"), ViewModel.WithdrawNum);
            Binding.BindText(GetNode<TMP_Text>("FormulaText"), ViewModel.FormulaText);
            _withdrawBtn = GetNode<Button>("WithDrawBtn");
            _doubleBtn = GetNode<Button>("DoubleBtn");
            if (_withdrawBtn != null)
            {
                _withdrawBtn.onClick.AddListener(OnWithdrawClicked);
            }

            if (_doubleBtn != null)
            {
                _doubleBtn.onClick.AddListener(OnDoubleClicked);
            }

            Binding.BindInteractable(_withdrawBtn, ViewModel.ButtonsEnabled);
            Binding.BindInteractable(_doubleBtn, ViewModel.DoubleEnabled);
            // OnBind 先于 VM.OnOpen 执行，此时 RoundRows 还是空的，
            // 须订阅版本号等 Refresh 重建行列表后再开播入场动画（动画要枚举行，顺序不能反）。
            // emitCurrent:false：版本号默认会回放当前值 0，导致 OnBind 当场播一次无行入场动画、
            // _entrancePlayed 提前置位，OnOpen 建出的行接不进动画、alpha 全 1 同帧齐现。
            Binding.Add(ViewModel.RoundRevision.Subscribe(_ =>
            {
                RefreshRoundList();
                PlayEntranceOnce();
            }, emitCurrent: false));
            BindOverlayClose();
            _ = EnsureCoinPrefab();
        }

        protected override Task OnViewClose()
        {
            KillEntrance();
            KillCoinFx();
            if (_withdrawBtn != null)
            {
                _withdrawBtn.onClick.RemoveListener(OnWithdrawClicked);
            }

            if (_doubleBtn != null)
            {
                _doubleBtn.onClick.RemoveListener(OnDoubleClicked);
            }

            ClearRoundRows();
            return Task.CompletedTask;
        }

        private void BindOverlayClose()
        {
            var overlay = GetComponent<Button>();
            if (overlay == null)
            {
                overlay = gameObject.AddComponent<Button>();
                overlay.transition = Selectable.Transition.None;
            }

            Binding.BindCommand(overlay, ViewModel.ContinueCommand);
        }

        private RollingNumber BindRollingNumber(TMP_Text text, ObservableProperty<string> source)
        {
            var number = new RollingNumber(text);
            Binding.Add(number.Roller);
            Binding.Add(source.Subscribe(value =>
            {
                if (long.TryParse(value, out var parsed))
                {
                    number.SetTarget(parsed);
                }
                else
                {
                    number.Roller.SetRaw(value);
                }
            }));
            return number;
        }

        private void PlayEntranceOnce()
        {
            if (_entrancePlayed)
            {
                return;
            }

            _entrancePlayed = true;
            PlayEntrance();
        }

        private void PlayEntrance()
        {
            EnsureFitNodes();
            if (_bgRect == null || _contentRect == null)
            {
                return;
            }

            // 行是当帧刚克隆的，布局还没刷过，先强制重建才能量准各块高度。
            LayoutRebuilder.ForceRebuildLayoutImmediate(_contentRect);
            CollectRevealSteps(_revealSteps);

            // 入场期间接管 BG 高度：初始只留标题区，随后每播一块长高一段，播完回到全高。
            var vlg = _contentRect.GetComponent<VerticalLayoutGroup>();
            var spacing = vlg != null ? vlg.spacing : 0f;
            var contentHeight = _titleRect != null ? _titleRect.rect.height + _contentRect.rect.height : _contentRect.rect.height;
            _bgFullTargetY = contentHeight + BgBottomEdge;
            var expandTotal = 0f;
            for (var i = 0; i < _revealSteps.Count; i++)
            {
                expandTotal += _revealSteps[i].Height + (i > 0 ? spacing : 0f);
            }

            var initialY = Mathf.Max(0f, _bgFullTargetY - expandTotal);
            _suppressBgFit = true;

            _bgGroup = GetOrAddCanvasGroup(_bgRect);
            _bgRestPos = _bgRect.anchoredPosition;
            _bgGroup.alpha = 0f;
            _bgGroup.blocksRaycasts = false;
            _bgRect.anchoredPosition = _bgRestPos + new Vector2(0f, -PanelSlideDistance);
            _bgRect.sizeDelta = new Vector2(_bgRect.sizeDelta.x, initialY);

            _entranceSeq = DOTween.Sequence().SetUpdate(true);
            _entranceSeq.Insert(0f, _bgGroup.DOFade(1f, PanelFadeDuration).SetEase(Ease.OutQuad));
            _entranceSeq.Insert(0f, _bgRect.DOAnchorPos(_bgRestPos, PanelSlideDuration).SetEase(Ease.OutCubic));

            if (_titleRect != null)
            {
                _titleGroup = GetOrAddCanvasGroup(_titleRect);
                _titleRestPos = _titleRect.anchoredPosition;
                _titleGroup.alpha = 0f;
                _titleRect.anchoredPosition = _titleRestPos + new Vector2(0f, TitleDropDistance);
                _entranceSeq.Insert(TitleDelay, _titleGroup.DOFade(1f, TitleFadeDuration).SetEase(Ease.OutQuad));
                _entranceSeq.Insert(TitleDelay, _titleRect.DOAnchorPos(_titleRestPos, TitleDropDuration).SetEase(Ease.OutBack));
            }

            var bgWidth = _bgRect.sizeDelta.x;
            var revealsAtRowEnd = new List<DG.Tweening.TweenCallback>();

            // GrowTarget = 恰好露出该块底部时的 BG 高度（布局顺序累计，与各块淡入时刻解耦）。
            var cumulative = initialY;
            var firstStep = true;
            for (var i = 0; i < _revealSteps.Count; i++)
            {
                cumulative += _revealSteps[i].Height + (firstStep ? 0f : spacing);
                firstStep = false;
                _revealSteps[i].GrowTarget = cumulative;
            }

            var firstRowIndex = -1;
            for (var i = 0; i < _revealSteps.Count; i++)
            {
                if (_revealSteps[i].IsRow)
                {
                    firstRowIndex = i;
                    break;
                }
            }

            // 静态块（表头/汇总/公式/两个按钮）开局同帧一起淡入；行下方各块此刻仍被蒙罩挡着，
            // 到行链播完纸面开到那里才真正露出，因此数字滚动排在行链末尾。
            for (var i = 0; i < _revealSteps.Count; i++)
            {
                var step = _revealSteps[i];
                if (step.IsRow)
                {
                    continue;
                }

                step.Group.alpha = 0f;
                step.Group.blocksRaycasts = false;
                _blockGroups.Add(step.Group);
                var group = step.Group;
                _entranceSeq.Insert(BlocksDelay, group.DOFade(1f, BlockFadeDuration).SetEase(Ease.OutQuad)
                    .OnComplete(() => group.blocksRaycasts = true)
                    .SetLink(group.gameObject, LinkBehaviour.KillOnDestroy));
                if (step.OnShown != null)
                {
                    revealsAtRowEnd.Add(step.OnShown);
                }
            }

            // 开局长高的只有行上方静态块（表头）。汇总/公式/按钮在布局上位于行下方，高度此刻计入
            // 会把行区纸面过早掀开成大段空白，须等行链播完再长；无行时全部静态块都算行上方。
            var grownTo = initialY;
            if (firstRowIndex < 0)
            {
                grownTo = _revealSteps.Count > 0 ? _revealSteps[_revealSteps.Count - 1].GrowTarget : initialY;
            }
            else if (firstRowIndex > 0)
            {
                grownTo = _revealSteps[firstRowIndex - 1].GrowTarget;
            }

            if (grownTo > initialY)
            {
                _entranceSeq.Insert(BlocksDelay, _bgRect.DOSizeDelta(new Vector2(bgWidth, grownTo), BlockFadeDuration).SetEase(Ease.OutQuad));
            }

            // 只有 Round_N 行逐行串行：上一行播完下一行才开始，BG 随每行再长一段。
            var cursor = BlocksDelay + BlockFadeDuration;
            for (var i = 0; i < _revealSteps.Count; i++)
            {
                var step = _revealSteps[i];
                if (!step.IsRow)
                {
                    continue;
                }

                step.Group.alpha = 0f;
                step.Group.blocksRaycasts = false;
                _blockGroups.Add(step.Group);
                var group = step.Group;
                var at = cursor;
                cursor += BlockFadeDuration;
                // 入场途中双倍提现会重建行列表，行对象销毁时联动杀掉 tween，避免写已销毁的 CanvasGroup。
                _entranceSeq.Insert(at, group.DOFade(1f, BlockFadeDuration).SetEase(Ease.OutQuad)
                    .OnComplete(() => group.blocksRaycasts = true)
                    .SetLink(group.gameObject, LinkBehaviour.KillOnDestroy));
                grownTo = step.GrowTarget;
                _entranceSeq.Insert(at, _bgRect.DOSizeDelta(new Vector2(bgWidth, grownTo), BlockFadeDuration).SetEase(Ease.OutQuad));
            }

            // 行链播完，BG 一次补长到行下方静态块（汇总/公式/按钮）区域，与数字滚动同刻；
            // 无行时开局已一次长满，这里不补。
            var fullGrownTarget = _revealSteps.Count > 0 ? _revealSteps[_revealSteps.Count - 1].GrowTarget : initialY;
            if (fullGrownTarget > grownTo + 0.01f)
            {
                _entranceSeq.Insert(cursor, _bgRect.DOSizeDelta(new Vector2(bgWidth, fullGrownTarget), BlockFadeDuration).SetEase(Ease.OutQuad));
            }

            // 行链播完（无行时为静态块淡入完），纸面正好开到汇总/按钮区域，此刻开始数字滚动。
            for (var i = 0; i < revealsAtRowEnd.Count; i++)
            {
                _entranceSeq.InsertCallback(cursor, revealsAtRowEnd[i]);
            }

            // BG 的 blocksRaycasts=false 会让整个子树（含按钮）对射线透明，正常播完也必须恢复，
            // 否则按钮收不到指针事件、点纸面反而触发蒙层关窗。
            _entranceSeq.OnComplete(() =>
            {
                if (_bgGroup != null)
                {
                    _bgGroup.blocksRaycasts = true;
                }

                _suppressBgFit = false;
            });
        }

        // 按 Content 的兄弟顺序（即布局显示顺序）收集要逐块显现的块；
        // Summery / WithDrawBtn 显现的同时开始各自数字的滚动。
        private void CollectRevealSteps(List<RevealStep> steps)
        {
            steps.Clear();
            for (var i = 0; i < _contentRect.childCount; i++)
            {
                var child = _contentRect.GetChild(i);
                if (!child.gameObject.activeSelf)
                {
                    continue;
                }

                var rect = child as RectTransform;
                var group = GetOrAddCanvasGroup(rect);
                if (group == null)
                {
                    continue;
                }

                var step = new RevealStep
                {
                    Group = group,
                    Height = rect != null ? rect.rect.height : 0f,
                    // 行判定用名字前缀（RefreshRoundList 命名 Round_N），不依赖列表引用比对——
                    // 同帧待销毁旧行、池化复用时序都可能让 Contains 判否，行会被误当静态块一起播。
                    IsRow = child.name.StartsWith("Round_") || _roundRows.Contains(child.gameObject)
                };
                if (child.name == "Summery")
                {
                    step.OnShown = RevealSummaryNumbers;
                }
                else if (child.name == "WithDrawBtn" && _rollWithdraw != null)
                {
                    step.OnShown = _rollWithdraw.Reveal;
                }

                steps.Add(step);
            }
        }

        private void RevealSummaryNumbers()
        {
            if (_rollMonsterCount != null)
            {
                _rollMonsterCount.Reveal();
            }

            if (_rollTotalDamage != null)
            {
                _rollTotalDamage.Reveal();
            }

            if (_rollReward != null)
            {
                _rollReward.Reveal();
            }
        }

        // 中途关闭/复用前的复位：杀掉动画并还原到最终状态，残留半透明或错位。
        private void KillEntrance()
        {
            if (_entranceSeq != null)
            {
                if (_entranceSeq.IsActive())
                {
                    _entranceSeq.Kill();
                }

                _entranceSeq = null;
            }

            _suppressBgFit = false;
            if (_bgFitTween != null && _bgFitTween.IsActive())
            {
                _bgFitTween.Kill();
            }

            _bgFitTween = null;
            if (_bgRect != null && _bgFullTargetY > 0f)
            {
                _bgRect.sizeDelta = new Vector2(_bgRect.sizeDelta.x, _bgFullTargetY);
            }

            if (_bgGroup != null)
            {
                _bgGroup.alpha = 1f;
                _bgGroup.blocksRaycasts = true;
            }

            if (_bgRect != null)
            {
                _bgRect.anchoredPosition = _bgRestPos;
            }

            if (_titleGroup != null)
            {
                _titleGroup.alpha = 1f;
            }

            if (_titleRect != null)
            {
                _titleRect.anchoredPosition = _titleRestPos;
            }

            for (var i = 0; i < _blockGroups.Count; i++)
            {
                if (_blockGroups[i] != null)
                {
                    _blockGroups[i].alpha = 1f;
                    _blockGroups[i].blocksRaycasts = true;
                }
            }

            _blockGroups.Clear();
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

        private async Task EnsureCoinPrefab()
        {
            if (_coinPrefab != null || ViewModel?.Resources == null)
            {
                return;
            }

            if (ViewModel.Resources.TryGetCached<GameObject>(ResResourcePaths.CoinItem, out var cached) && cached != null)
            {
                _coinPrefab = cached;
                return;
            }

            _coinPrefab = await ViewModel.Resources.LoadAsync<GameObject>(ResResourcePaths.CoinItem);
        }

        private async void OnWithdrawClicked()
        {
            if (ViewModel == null || !ViewModel.ButtonsEnabled.Value)
            {
                return;
            }

            ViewModel.SetBusy(true);
            await EnsureCoinPrefab();
            if (ViewModel == null)
            {
                return;
            }

            var bar = GameResourceView.FindOpen()?.ViewModel;
            var amount = bar != null ? bar.HeldGold : 0;
            if (amount <= 0 || !TryPlayCoinFly(_withdrawBtn, () => ViewModel.CompleteWithdraw(bar)))
            {
                ViewModel.CompleteWithdraw(bar);
            }
        }

        private async void OnDoubleClicked()
        {
            if (ViewModel == null || !ViewModel.DoubleEnabled.Value)
            {
                return;
            }

            ViewModel.SetBusy(true);
            await EnsureCoinPrefab();
            if (ViewModel == null)
            {
                return;
            }

            var bar = GameResourceView.FindOpen()?.ViewModel;
            if (!ViewModel.TryBeginDouble(bar, out var extra))
            {
                ViewModel.SetBusy(false);
                return;
            }

            if (extra <= 0 || !TryPlayCoinFly(_doubleBtn, () => ViewModel.CompleteDouble(bar, extra)))
            {
                ViewModel.CompleteDouble(bar, extra);
                ViewModel.SetBusy(false);
            }
        }

        private bool TryPlayCoinFly(Button source, System.Action onArrived)
        {
            var from = source != null ? source.transform as RectTransform : null;
            var to = FindGoldIcon();
            var parent = ResolveFxParent();
            if (from == null || to == null || parent == null || _coinPrefab == null)
            {
                return false;
            }

            var token = ++_playToken;
            KillCoinFx(false);
            _coinSeq = CoinFlyFx.Play(
                _coinPrefab,
                parent,
                from.position,
                to.position,
                () =>
                {
                    if (token != _playToken || ViewModel == null)
                    {
                        return;
                    }

                    _coinSeq = null;
                    // CompleteWithdraw 会同步 Close，把 ViewModel 置空，必须先解除 busy。
                    ViewModel.SetBusy(false);
                    onArrived?.Invoke();
                });
            return _coinSeq != null;
        }

        private RectTransform ResolveFxParent()
        {
            var root = ViewModel?.Ui?.Root;
            if (root != null)
            {
                return root.GetLayer(UILayer.Resource);
            }

            return transform as RectTransform;
        }

        private static RectTransform FindGoldIcon()
        {
            var view = GameResourceView.FindOpen();
            return view != null ? GameResourceBarBinder.FindGoldIcon(view.transform) : null;
        }

        private void KillCoinFx(bool bumpToken = true)
        {
            if (bumpToken)
            {
                _playToken++;
            }

            if (_coinSeq != null && _coinSeq.IsActive())
            {
                _coinSeq.Kill();
            }

            _coinSeq = null;
        }

        private static Transform FindDeep(Transform root, string name)
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
                var found = FindDeep(root.GetChild(i), name);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        private T GetNode<T>(string key) where T : Component
        {
            return UI.GetGameObject(key).GetComponent<T>();
        }

        private void RefreshRoundList()
        {
            EnsureTemplate();
            ClearRoundRows();
            if (_roundTemplate == null)
            {
                return;
            }

            var rows = ViewModel.RoundRows;
            for (var i = 0; i < rows.Count; i++)
            {
                var row = Instantiate(_roundTemplate, _roundTemplate.transform.parent);
                row.SetActive(true);
                row.name = $"Round_{rows[i].Round}";
                row.transform.SetSiblingIndex(_roundTemplate.transform.GetSiblingIndex() + 1 + i);
                ApplyRoundRow(row, rows[i]);
                _roundRows.Add(row);
            }
        }

        // BG 高度 = Title 高度 + Content 高度 + 底部边框；BG 锚点/轴心居中，只改 sizeDelta 即保持居中。
        // Content 高度由 ContentSizeFitter 帧末刷新，绑定时序不定，故每帧检测变化后再写入。
        private void LateUpdate()
        {
            FitBgHeight();
        }

        private void FitBgHeight()
        {
            if (_suppressBgFit)
            {
                // 入场动画正在逐段展开 BG 高度，交给动画驱动，播完自动恢复。
                return;
            }

            EnsureFitNodes();
            if (_bgRect == null || _titleRect == null || _contentRect == null)
            {
                return;
            }

            var target = _titleRect.rect.height + _contentRect.rect.height + BgBottomEdge;
            if (Mathf.Abs(target - _bgRect.rect.height) <= 0.5f)
            {
                return;
            }

            if (_bgFitTween != null && _bgFitTween.IsActive())
            {
                // 已在补间中，等它到位后下一帧再校验，避免反复重启。
                return;
            }

            _bgFitTween = _bgRect.DOSizeDelta(new Vector2(_bgRect.sizeDelta.x, target), BgFitTweenDuration)
                .SetEase(Ease.OutQuad)
                .SetUpdate(true)
                .SetLink(gameObject, LinkBehaviour.KillOnDestroy);
        }

        private void EnsureFitNodes()
        {
            if (_bgRect == null)
            {
                _bgRect = FindDeep(transform, "BG") as RectTransform;
            }

            if (_titleRect == null && _bgRect != null)
            {
                _titleRect = FindDeep(_bgRect, "Title") as RectTransform;
            }

            if (_contentRect == null && _bgRect != null)
            {
                _contentRect = FindDeep(_bgRect, "Content") as RectTransform;
            }
        }

        private void EnsureTemplate()
        {
            if (_roundTemplate != null)
            {
                return;
            }

            _roundTemplate = UI.GetGameObject("Text 1");
            if (_roundTemplate != null)
            {
                _roundTemplate.SetActive(false);
            }
        }

        private static void ApplyRoundRow(GameObject row, SettleRoundRow data)
        {
            var roundText = FindChildText(row.transform, "Round");
            if (roundText != null)
            {
                roundText.text = data.Round.ToString();
            }

            var killsText = FindChildText(row.transform, "Kills");
            if (killsText != null)
            {
                killsText.text = data.Kills.ToString();
            }

            var scoreText = FindChildText(row.transform, "Score");
            if (scoreText != null)
            {
                scoreText.text = data.Damage.ToString();
            }
        }

        private static TMP_Text FindChildText(Transform root, string childName)
        {
            var child = root.Find(childName);
            return child != null ? child.GetComponent<TMP_Text>() : null;
        }

        private void ClearRoundRows()
        {
            for (var i = 0; i < _roundRows.Count; i++)
            {
                if (_roundRows[i] != null)
                {
                    Destroy(_roundRows[i]);
                }
            }

            _roundRows.Clear();
        }

        /// <summary>
        /// 内容块的一个显现步骤：块淡入一档，GrowTarget 为恰好露出该块底部时的 BG 高度
        /// （按布局顺序累计，淡入时刻与长高时机解耦）。IsRow 标记逐回合明细行（Round_N），
        /// 它们单独串行播放，其余静态块开局一起淡入。
        /// </summary>
        private sealed class RevealStep
        {
            public CanvasGroup Group;

            public float Height;

            public bool IsRow;

            public float GrowTarget;

            public DG.Tweening.TweenCallback OnShown;
        }

        /// <summary>
        /// 结算数字：绑定时归零待命，所属内容块显现时才从 0 滚到目标；
        /// 之后数值再变化（如双倍提现刷新）从当前显示值续滚。
        /// </summary>
        private sealed class RollingNumber
        {
            public readonly RollingText Roller;

            private long _target;
            private bool _revealed;

            public RollingNumber(TMP_Text text)
            {
                Roller = new RollingText(text, new RollingTextOptions
                {
                    Duration = 0.7f,
                    Ease = Ease.OutCubic
                });
                Roller.Set(0);
            }

            public void SetTarget(long target)
            {
                _target = target;
                if (_revealed)
                {
                    Roller.RollTo(_target);
                }
            }

            public void Reveal()
            {
                if (_revealed)
                {
                    return;
                }

                _revealed = true;
                Roller.RollTo(_target);
            }
        }
    }
}
