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
            Binding.BindCommand(GetNode<Button>("WithDrawBtn"), ViewModel.ContinueCommand);
            BindOverlayClose();
            RefreshRoundList();
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

            var scores = ViewModel.Session.StageRoundScores;
            for (var i = 0; i < scores.Count; i++)
            {
                var row = Instantiate(_roundTemplate, _roundTemplate.transform.parent);
                row.SetActive(true);
                row.name = $"Round_{i + 1}";
                row.transform.SetSiblingIndex(_roundTemplate.transform.GetSiblingIndex() + 1 + i);
                ApplyRoundRow(row, i + 1, scores[i]);
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

        private static void ApplyRoundRow(GameObject row, int round, int score)
        {
            var roundText = FindChildText(row.transform, "Round");
            if (roundText != null)
            {
                roundText.text = $"第{round}回合";
            }

            var scoreText = FindChildText(row.transform, "Score");
            if (scoreText != null)
            {
                scoreText.text = score.ToString();
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
