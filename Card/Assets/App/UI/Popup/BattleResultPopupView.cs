using System.Collections.Generic;
using System.Threading.Tasks;
using App.Resources;
using Framework.UI.Core;
using Framework.UI.Navigation;
using Framework.UI.View;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace App.UI.Popup
{
    /// <summary>
    /// 闯关结算弹窗。成功 / 失败显示不同立绘与角标；失败时 AgainBtn 广告复活。
    /// Content 里克隆 Text1 模板逐行显示每一关获得的金币（LevelText=第N关、DamageText=金币，
    /// DotText 点线装饰不动），最底部 SummeryArea.coinNum 显示总数。
    /// 入场走 PaperRevealAnim（GrowBg=false：本预制体 BG/滚动区是固定尺寸，汇总区与按钮
    /// 摆在滚动区外，不随内容长高）。
    /// </summary>
    [AutoScreen(AppScreenIds.BattleResultPopup, UILayer.Popup, ResResourcePaths.BattleResultPopup)]
    public sealed class BattleResultPopupView : ViewBase<BattleResultPopupViewModel>
    {
        // BG 高度模型（美术手调基准，与预制体一致）：1 行时 BG=439 / Scroll View=199，
        // 之后每多一行 BG 加一个行高（行高取 Text1 模板实测值 59.5）。
        // 子节点锚点配合：Title 挂 BG 顶边、SummeryArea/两按钮挂 BG 底边；
        // Scroll View 是拉伸锚点（(0,0)-(1,1)+负 sizeDelta，实高恒=BG高−240），
        // BG 居中轴心长高时它自动跟随、顶底间隙不变，代码只写 BG 不写它。宽度固定不变。
        public float BgBaseHeight = 439f;

        private readonly List<GameObject> _stageRows = new List<GameObject>();
        private readonly List<PaperRevealStep> _entranceSteps = new List<PaperRevealStep>();
        private GameObject _stageTemplate;
        private RectTransform _bgRect;
        private RectTransform _titleRect;
        private RectTransform _contentRect;
        private PaperRevealAnim _entrance;
        private bool _entrancePlayed;

        protected override void OnBind()
        {
            _entrancePlayed = false;
            Binding.BindActive(UI.GetGameObject("logosuccess2"), ViewModel.ShowSuccess);
            Binding.BindActive(UI.GetGameObject("logofail2"), ViewModel.ShowFail);
            Binding.BindText(GetNode<TMP_Text>("coinNum"), ViewModel.CoinNum);

            Binding.BindCommand(GetNode<Button>("BackBtn"), ViewModel.BackCommand);

            var againButton = GetNode<Button>("AgainBtn");
            Binding.BindCommand(againButton, ViewModel.AgainCommand);
            Binding.BindActive(againButton.gameObject, ViewModel.ShowAgain);

            // 同 BattleSettleUpPop：OnBind 先于 VM.OnOpen，StageRows 此时还是空的，
            // 须订阅版本号等 Refresh 建行后再播入场动画（动画要枚举行，顺序不能反）；
            // emitCurrent:false 防止回放当前值 0 当场空播一次。
            Binding.Add(ViewModel.StageRevision.Subscribe(_ =>
            {
                RefreshStageList();
                PlayEntranceOnce();
            }, emitCurrent: false));
        }

        protected override Task OnViewClose()
        {
            _entrance?.Kill();
            ClearStageRows();
            return Task.CompletedTask;
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
        // Stage_N 行进串行链，Head 表头/分隔线等静态块开局齐现。
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
                    // 行判定用名字前缀（RefreshStageList 命名 Stage_N），不依赖列表引用比对。
                    IsRow = child.name.StartsWith("Stage_") || _stageRows.Contains(child.gameObject)
                };
                if (step.Rect == null)
                {
                    continue;
                }

                _entranceSteps.Add(step);
            }
        }

        private void RefreshStageList()
        {
            EnsureTemplate();
            ClearStageRows();
            if (_stageTemplate == null)
            {
                return;
            }

            var rows = ViewModel.StageRows;
            for (var i = 0; i < rows.Count; i++)
            {
                var row = Instantiate(_stageTemplate, _stageTemplate.transform.parent);
                row.SetActive(true);
                row.name = $"Stage_{rows[i].Stage}";
                row.transform.SetSiblingIndex(_stageTemplate.transform.GetSiblingIndex() + 1 + i);
                ApplyStageRow(row, rows[i]);
                _stageRows.Add(row);
            }
        }

        private static void ApplyStageRow(GameObject row, ResultStageRow data)
        {
            var levelText = FindChildText(row.transform, "LevelText");
            if (levelText != null)
            {
                levelText.text = $"第{data.Stage}关";
            }

            var goldText = FindChildText(row.transform, "DamageText");
            if (goldText != null)
            {
                goldText.text = data.Gold.ToString();
            }
        }

        private static TMP_Text FindChildText(Transform root, string childName)
        {
            var child = root.Find(childName);
            return child != null ? child.GetComponent<TMP_Text>() : null;
        }

        private void EnsureTemplate()
        {
            if (_stageTemplate != null)
            {
                return;
            }

            _stageTemplate = FindDeep(transform, "Text1")?.gameObject;
            if (_stageTemplate != null)
            {
                _stageTemplate.SetActive(false);
            }
        }

        private void EnsureEntrance()
        {
            EnsureFitNodes();
            if (_entrance == null && _bgRect != null && _contentRect != null)
            {
                EnsureContentFitter();
                _entrance = new PaperRevealAnim(_bgRect, _titleRect, _contentRect, bottomEdge: 0f)
                {
                    // 固定尺寸纸底：BG/滚动区高度由美术手摆，只做滑入/回落/淡入/行链。
                    GrowBg = false
                };
            }
        }

        private void EnsureContentFitter()
        {
            if (_contentRect.GetComponent<ContentSizeFitter>() != null)
            {
                return;
            }

            // 预制体 Content 是手摆高度（300），行数不定时多出的行会被滚动范围裁掉，
            // 运行时补自适配让 Content 跟着行数长，ScrollRect 才能滚到全部行。
            var fitter = _contentRect.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
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

        private void ClearStageRows()
        {
            for (var i = 0; i < _stageRows.Count; i++)
            {
                if (_stageRows[i] != null)
                {
                    Destroy(_stageRows[i]);
                }
            }

            _stageRows.Clear();
        }

        // BG 高度自适应：extra =（行数−1）×行高；BG=439+extra。Scroll View 是拉伸锚点
        // ((0,0)-(1,1)+负 sizeDelta −240)，实际高度恒 = BG 高度 + sizeDelta.y，BG 长高时
        // 自动跟随（=199+extra、顶底间隙不变）；这里绝不能按绝对高度写它的 sizeDelta——
        // 拉伸锚点下那会算成 BG高+目标高 的双重叠加，滚动区溢出纸面。
        // 行数取已克隆的 Stage_N 行，行高实测 Text1 模板
        // （非激活子节点不受布局驱动，rect 保持编辑器值）。
        private void LateUpdate()
        {
            FitBgHeight();
        }

        private void FitBgHeight()
        {
            EnsureFitNodes();
            EnsureTemplate();
            if (_bgRect == null || _stageTemplate == null)
            {
                return;
            }

            var rowHeight = (_stageTemplate.transform as RectTransform).rect.height;
            var extra = Mathf.Max(0, _stageRows.Count - 1) * rowHeight;
            var bgTarget = BgBaseHeight + extra;
            if (Mathf.Abs(bgTarget - _bgRect.sizeDelta.y) <= 0.5f)
            {
                return;
            }

            _bgRect.sizeDelta = new Vector2(_bgRect.sizeDelta.x, bgTarget);
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
    }
}
