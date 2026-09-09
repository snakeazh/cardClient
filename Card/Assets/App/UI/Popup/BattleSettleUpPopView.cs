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

        // 入场动画节奏：面板自下方滑入淡现 → 标题回落 → 内容块自上而下逐块串行淡入
        // （上一块播完才播下一块），BG 高度按各块高度逐段增长（跟随 Scroll View 逐渐展开），
        // 全部播完后恢复 FitBgHeight 托底。
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
            Binding.Add(ViewModel.RoundRevision.Subscribe(_ =>
            {
                RefreshRoundList();
                PlayEntranceOnce();
            }));
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

            var cursor = BlocksDelay;
            var grownTo = initialY;
            var bgWidth = _bgRect.sizeDelta.x;
            for (var i = 0; i < _revealSteps.Count; i++)
            {
                var step = _revealSteps[i];
                step.Group.alpha = 0f;
                step.Group.blocksRaycasts = false;
                _blockGroups.Add(step.Group);
                var at = cursor;
                cursor += BlockFadeDuration;
                var group = step.Group;
                // 入场途中双倍提现会重建行列表，行对象销毁时联动杀掉 tween，避免写已销毁的 CanvasGroup。
                _entranceSeq.Insert(at, group.DOFade(1f, BlockFadeDuration).SetEase(Ease.OutQuad)
                    .OnComplete(() => group.blocksRaycasts = true)
                    .SetLink(group.gameObject, LinkBehaviour.KillOnDestroy));
                grownTo += step.Height + (i > 0 ? spacing : 0f);
                var growTo = grownTo;
                _entranceSeq.Insert(at, _bgRect.DOSizeDelta(new Vector2(bgWidth, growTo), BlockFadeDuration).SetEase(Ease.OutQuad));
                if (step.OnShown != null)
                {
                    _entranceSeq.InsertCallback(at, step.OnShown);
                }
            }

            _entranceSeq.OnComplete(() => _suppressBgFit = false);
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
                    Height = rect != null ? rect.rect.height : 0f
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

            _bgRect.sizeDelta = new Vector2(_bgRect.sizeDelta.x, target);
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
        /// 内容块的一个显现步骤：块淡入一档，BG 同时长高该块占的高度。
        /// </summary>
        private sealed class RevealStep
        {
            public CanvasGroup Group;

            public float Height;

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
