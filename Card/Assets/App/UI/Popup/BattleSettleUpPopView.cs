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
    /// 入场走 PaperRevealAnim 纸面逐格展开（见 App/UI/Effects/PaperRevealAnim.cs）。
    /// </summary>
    [AutoScreen(AppScreenIds.BattleSettleUpPop, UILayer.Popup, ResResourcePaths.BattleSettleUpPop)]
    public sealed class BattleSettleUpPopView : ViewBase<BattleSettleUpPopViewModel>
    {
        // BG 底部预留 = 背景图九宫格下边框厚度（LevelSettlementBaseFrame spriteBorder.w = 116）
        public float BgBottomEdge = 116f;

        private readonly List<GameObject> _roundRows = new List<GameObject>();
        private readonly List<PaperRevealStep> _entranceSteps = new List<PaperRevealStep>();
        private GameObject _roundTemplate;
        private RectTransform _bgRect;
        private RectTransform _titleRect;
        private RectTransform _contentRect;
        private PaperRevealAnim _entrance;
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
            EnsureEntrance();
            if (_entrance == null)
            {
                return;
            }

            CollectEntranceSteps();
            _entrance.Play(_entranceSteps);
        }

        // 按 Content 的兄弟顺序（即布局显示顺序）收集入场显现步骤；
        // Summery / WithDrawBtn 的 OnShown 在纸面开到其区域时启动各自数字的滚动。
        private void CollectEntranceSteps()
        {
            _entranceSteps.Clear();
            for (var i = 0; i < _contentRect.childCount; i++)
            {
                var child = _contentRect.GetChild(i);
                if (!child.gameObject.activeSelf)
                {
                    continue;
                }

                var step = new PaperRevealStep
                {
                    Rect = child as RectTransform,
                    // 行判定用名字前缀（RefreshRoundList 命名 Round_N），不依赖列表引用比对——
                    // 同帧待销毁旧行、池化复用时序都可能让 Contains 判否，行会被误当静态块一起播。
                    IsRow = child.name.StartsWith("Round_") || _roundRows.Contains(child.gameObject)
                };
                if (step.Rect == null)
                {
                    continue;
                }

                if (child.name == "Summery")
                {
                    step.OnShown = RevealSummaryNumbers;
                }
                else if (child.name == "WithDrawBtn" && _rollWithdraw != null)
                {
                    step.OnShown = _rollWithdraw.Reveal;
                }

                _entranceSteps.Add(step);
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

        // 中途关闭/复用前的复位：杀掉动画并还原到最终状态，避免残留半透明或错位。
        private void KillEntrance()
        {
            _entrance?.Kill();
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

        // 入场动画逐段展开期间 PaperRevealAnim 内部抑制 BG 托底，播完自动恢复，
        // 这里每帧把托底 tick 交给它即可。
        private void LateUpdate()
        {
            EnsureEntrance();
            _entrance?.TickFitBgHeight();
        }

        private void EnsureEntrance()
        {
            EnsureFitNodes();
            if (_entrance == null && _bgRect != null && _contentRect != null)
            {
                _entrance = new PaperRevealAnim(_bgRect, _titleRect, _contentRect, BgBottomEdge);
            }
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
