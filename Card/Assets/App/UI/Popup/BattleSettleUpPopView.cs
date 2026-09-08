using System.Collections.Generic;
using System.Threading.Tasks;
using App.Resources;
using DG.Tweening;
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

        private readonly List<GameObject> _roundRows = new List<GameObject>();
        private GameObject _roundTemplate;
        private RectTransform _bgRect;
        private RectTransform _titleRect;
        private RectTransform _contentRect;
        private Button _withdrawBtn;
        private Button _doubleBtn;
        private GameObject _coinPrefab;
        private Sequence _coinSeq;
        private int _playToken;

        protected override void OnBind()
        {
            Binding.BindText(GetNode<TMP_Text>("CurScoreNum"), ViewModel.CurScoreNum);
            Binding.BindText(GetNode<TMP_Text>("TotalScoreNum"), ViewModel.TotalScoreNum);
            Binding.BindText(GetNode<TMP_Text>("CoinNum"), ViewModel.CoinNum);
            Binding.BindText(GetNode<TMP_Text>("Num"), ViewModel.WithdrawNum);
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
            // 须订阅版本号等 Refresh 后再重建行列表。
            Binding.Add(ViewModel.RoundRevision.Subscribe(_ => RefreshRoundList()));
            BindOverlayClose();
            _ = EnsureCoinPrefab();
        }

        protected override Task OnViewClose()
        {
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
                    onArrived?.Invoke();
                    ViewModel.SetBusy(false);
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
            if (_bgRect == null)
            {
                _bgRect = FindDeep(transform, "BG") as RectTransform;
                if (_bgRect == null)
                {
                    return;
                }
            }

            if (_titleRect == null)
            {
                _titleRect = FindDeep(_bgRect, "Title") as RectTransform;
                if (_titleRect == null)
                {
                    return;
                }
            }

            if (_contentRect == null)
            {
                _contentRect = FindDeep(_bgRect, "Content") as RectTransform;
                if (_contentRect == null)
                {
                    return;
                }
            }

            var target = _titleRect.rect.height + _contentRect.rect.height + BgBottomEdge;
            if (Mathf.Abs(target - _bgRect.rect.height) <= 0.5f)
            {
                return;
            }

            _bgRect.sizeDelta = new Vector2(_bgRect.sizeDelta.x, target);
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
    }
}
