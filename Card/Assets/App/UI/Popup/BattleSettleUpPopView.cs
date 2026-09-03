using System.Collections.Generic;
using System.Threading.Tasks;
using App.Resources;
using Framework.UI.Navigation;
using Framework.UI.View;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace App.UI.Popup
{
    /// <summary>
    /// 关卡结算弹窗。节点通过 UIReference / UIBind 解析。
    /// </summary>
    [AutoScreen(AppScreenIds.BattleSettleUpPop, UILayer.Popup, ResResourcePaths.BattleSettleUpPop)]
    public sealed class BattleSettleUpPopView : ViewBase<BattleSettleUpPopViewModel>
    {
        private readonly List<GameObject> _roundRows = new List<GameObject>();
        private GameObject _roundTemplate;

        protected override void OnBind()
        {
            Binding.BindText(GetNode<TMP_Text>("CurScoreNum"), ViewModel.CurScoreNum);
            Binding.BindText(GetNode<TMP_Text>("TotalScoreNum"), ViewModel.TotalScoreNum);
            Binding.BindText(GetNode<TMP_Text>("CoinNum"), ViewModel.CoinNum);
            Binding.BindText(GetNode<TMP_Text>("Num"), ViewModel.WithdrawNum);
            Binding.BindText(GetNode<TMP_Text>("FormulaText"), ViewModel.FormulaText);
            Binding.BindCommand(GetNode<Button>("WithDrawBtn"), ViewModel.ContinueCommand);
            Binding.BindCommand(GetNode<Button>("DoubleBtn"), ViewModel.DoubleCommand);
            // OnBind 先于 VM.OnOpen 执行，此时 RoundRows 还是空的，
            // 须订阅版本号等 Refresh 后再重建行列表。
            Binding.Add(ViewModel.RoundRevision.Subscribe(_ => RefreshRoundList()));
            BindResourceBar();
            BindOverlayClose();
        }

        protected override Task OnViewClose()
        {
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

        private void BindResourceBar()
        {
            var bar = transform.Find("ResourceBar");
            if (bar == null)
            {
                bar = FindDeep(transform, "ResourceBar");
            }

            if (bar == null)
            {
                return;
            }

            bar.gameObject.SetActive(true);
            var top = bar.Find("TopArea") ?? FindDeep(bar, "TopArea") ?? bar;
            Transform goldItem = null;
            for (var i = 0; i < top.childCount; i++)
            {
                var child = top.GetChild(i);
                if (!child.name.StartsWith("ResourceItem"))
                {
                    continue;
                }

                if (goldItem == null)
                {
                    goldItem = child;
                    child.gameObject.SetActive(true);
                    continue;
                }

                child.gameObject.SetActive(false);
            }

            if (goldItem == null)
            {
                return;
            }

            var num = goldItem.Find("Num") ?? FindDeep(goldItem, "Num");
            var text = num != null ? num.GetComponent<TMP_Text>() : null;
            if (text != null)
            {
                Binding.BindRollingText(text, ViewModel.GoldText);
            }
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
