using System.Threading.Tasks;
using App.Game;
using App.Resources;
using Framework.UI.Core;
using Framework.UI.Navigation;
using Framework.UI.View;
using UnityEngine;
using UnityEngine.UI;

namespace App.UI
{
    /// <summary>
    /// 对局 HUD。player1/2/3 放置生成的 PlayerInfo；horBtns 按阶段显示按钮。
    /// </summary>
    [AutoScreen(AppScreenIds.GameUI, UILayer.Page, ResResourcePaths.GameUI)]
    public sealed class GameUIView : ViewBase<GameTableViewModel>
    {
        private static readonly string[] EnemySlotKeys = { "player2", "player3", "player1" };

        private GameObject _gameHud;
        private GameBoardController _board;
        private Transform _btns;
        private Transform _shopContent;

        protected override void OnBind()
        {
            BindPlayerInfo();
            SpawnEnemyInfos();
            BindPhaseButtons();
            EnsureHint();
            ViewModel.Refresh();
        }

        protected override async Task OnViewOpen()
        {
            var prefab = await ViewModel.Resources.LoadAsync<GameObject>(ResResourcePaths.GameHud);
            _gameHud = Instantiate(prefab);
            _gameHud.name = "GameHud";

            _board = _gameHud.GetComponent<GameBoardController>();
            if (_board == null)
            {
                _board = _gameHud.AddComponent<GameBoardController>();
            }

            _board.Attach(ViewModel);
            ViewModel.Session.Changed += OnSessionChanged;
        }

        protected override Task OnViewClose()
        {
            if (ViewModel != null)
            {
                ViewModel.Session.Changed -= OnSessionChanged;
            }

            if (_board != null)
            {
                _board.Detach();
                _board = null;
            }

            if (_gameHud != null)
            {
                Destroy(_gameHud);
                _gameHud = null;
            }

            return Task.CompletedTask;
        }

        private void OnSessionChanged()
        {
            RefreshShop();
        }

        private void BindPlayerInfo()
        {
            var playerInfo = transform.Find("PlayerInfo");
            if (playerInfo == null)
            {
                return;
            }

            Binding.BindText(FindUiText(playerInfo, "chip"), ViewModel.PlayerChips);
            Binding.BindText(FindUiText(playerInfo, "Text (2)"), ViewModel.PlayerBet);
            Binding.BindText(FindUiText(playerInfo, "state"), ViewModel.PlayerState);

            var roundInfo = transform.Find("roundInfo");
            if (roundInfo != null)
            {
                Binding.BindText(roundInfo.GetComponent<Text>(), ViewModel.RoundInfo);
            }
        }

        private void SpawnEnemyInfos()
        {
            var template = transform.Find("PlayerInfo");
            if (template == null)
            {
                return;
            }

            for (var i = 0; i < EnemySlotKeys.Length; i++)
            {
                var slot = ResolveSlot(EnemySlotKeys[i]);
                if (slot == null)
                {
                    continue;
                }

                var clone = Instantiate(template.gameObject, slot);
                clone.name = "PlayerInfo";
                clone.SetActive(true);
                var bind = clone.GetComponent<Framework.UI.Binding.UIBind>();
                if (bind != null)
                {
                    Destroy(bind);
                }

                var rt = clone.GetComponent<RectTransform>();
                if (rt != null)
                {
                    rt.anchorMin = new Vector2(0.5f, 0.5f);
                    rt.anchorMax = new Vector2(0.5f, 0.5f);
                    rt.pivot = new Vector2(0.5f, 0.5f);
                    rt.anchoredPosition = Vector2.zero;
                    rt.localScale = Vector3.one;
                }

                Binding.BindActive(clone, ViewModel.ShowEnemy[i]);
                Binding.BindText(FindUiText(clone.transform, "chip"), ViewModel.EnemyChips[i]);
                Binding.BindText(FindUiText(clone.transform, "Text (2)"), ViewModel.EnemyBet[i]);
                Binding.BindText(FindUiText(clone.transform, "state"), ViewModel.EnemyState[i]);
                BindEnemyAttack(clone, i);
            }
        }

        private void BindEnemyAttack(GameObject clone, int slot)
        {
            var image = clone.GetComponent<Image>();
            if (image == null)
            {
                image = clone.AddComponent<Image>();
                image.color = new Color(1f, 1f, 1f, 0.01f);
            }

            var button = clone.GetComponent<Button>();
            if (button == null)
            {
                button = clone.AddComponent<Button>();
            }

            button.targetGraphic = image;
            button.transition = Selectable.Transition.None;
            Binding.BindCommand(button, ViewModel.AttackCommands[slot]);
        }

        private void BindPhaseButtons()
        {
            _btns = transform.Find("horBtns") ?? transform.Find("btns");
            if (_btns == null)
            {
                return;
            }

            BindBtn("BlindBtn", ViewModel.BlindBetCommand, ViewModel.ShowActions);
            BindBtn("LookBtn", ViewModel.LookCommand, ViewModel.ShowLook);
            BindBtn("RaiseBtn", ViewModel.RaiseCommand, ViewModel.ShowActions);
            BindBtn("FoldBtn", ViewModel.FoldCommand, ViewModel.ShowActions);
            BindBtn("CompareBtn", ViewModel.OpenCommand, ViewModel.ShowActions);
            BindBtn("PeekBtn", ViewModel.RubCommand, ViewModel.ShowRub);
            BindBtn("CancelBtn", ViewModel.SkipRubCommand, ViewModel.ShowRub);
            BindBtn("NextRoundBtn", ViewModel.ContinueCommand, ViewModel.ShowContinue);
            SetBtnLabel("CompareBtn", "比牌");
            SetBtnLabel("PeekBtn", "搓牌");
            SetBtnLabel("CancelBtn", "取消");
            SetBtnLabel("NextRoundBtn", "下一局");

            var template = FindBtn("BlindBtn");
            if (template == null)
            {
                return;
            }

            EnsureBtn(template, "ExtraRubBtn", "广告+1搓牌", ViewModel.ExtraRubAdCommand, ViewModel.ShowShop);
            EnsureBtn(template, "DoubleGoldBtn", "广告双倍金币", ViewModel.DoubleGoldAdCommand, ViewModel.ShowShop);
            EnsureBtn(template, "LeaveShopBtn", "离开商店", ViewModel.LeaveShopCommand, ViewModel.ShowShop);
            EnsureBtn(template, "LoanBtn", "看广告借贷", ViewModel.LoanCommand, ViewModel.ShowFail);
            EnsureBtn(template, "ReviveBtn", "看广告复活", ViewModel.ReviveCommand, ViewModel.ShowFail);
            EnsureBtn(template, "RestartBtn", "重开本关", ViewModel.RestartCommand, ViewModel.ShowFail);
        }

        private void BindBtn(string name, IRelayCommand command, ObservableProperty<bool> visible)
        {
            var button = FindBtn(name);
            if (button == null)
            {
                return;
            }

            Binding.BindCommand(button, command);
            Binding.BindActive(button.gameObject, visible);
        }

        private void EnsureBtn(Button template, string name, string label, IRelayCommand command,
            ObservableProperty<bool> visible)
        {
            var existing = _btns.Find(name);
            Button button;
            if (existing != null)
            {
                button = existing.GetComponent<Button>();
            }
            else
            {
                var go = Instantiate(template.gameObject, _btns);
                go.name = name;
                var bind = go.GetComponent<Framework.UI.Binding.UIBind>();
                if (bind != null)
                {
                    Destroy(bind);
                }

                var text = go.GetComponentInChildren<Text>();
                if (text != null)
                {
                    text.text = label;
                }

                button = go.GetComponent<Button>();
            }

            if (button == null)
            {
                return;
            }

            Binding.BindCommand(button, command);
            Binding.BindActive(button.gameObject, visible);
        }

        private void RefreshShop()
        {
            if (ViewModel == null || ViewModel.Session.Phase != GamePhase.Shop)
            {
                if (_shopContent != null)
                {
                    _shopContent.gameObject.SetActive(false);
                }

                return;
            }

            if (_shopContent == null)
            {
                var go = new GameObject("ShopItems", typeof(RectTransform));
                go.transform.SetParent(transform, false);
                var rt = go.GetComponent<RectTransform>();
                rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
                rt.anchoredPosition = new Vector2(0f, 80f);
                rt.sizeDelta = new Vector2(920f, 900f);
                _shopContent = go.transform;
            }

            _shopContent.gameObject.SetActive(true);
            for (var i = _shopContent.childCount - 1; i >= 0; i--)
            {
                Destroy(_shopContent.GetChild(i).gameObject);
            }

            var template = FindBtn("BlindBtn");
            var catalog = GameBalance.Catalog;
            for (var i = 0; i < catalog.Count; i++)
            {
                var item = catalog[i];
                GameObject go;
                if (template != null)
                {
                    go = Instantiate(template.gameObject, _shopContent);
                    var bind = go.GetComponent<Framework.UI.Binding.UIBind>();
                    if (bind != null)
                    {
                        Destroy(bind);
                    }
                }
                else
                {
                    go = new GameObject(item.Id, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image),
                        typeof(Button));
                    go.transform.SetParent(_shopContent, false);
                }

                go.name = item.Id;
                go.SetActive(true);
                var rt = go.GetComponent<RectTransform>();
                if (rt != null)
                {
                    rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
                    rt.anchoredPosition = new Vector2(0f, 360f - i * 72f);
                    rt.sizeDelta = new Vector2(860f, 64f);
                }

                var label = go.GetComponentInChildren<Text>();
                if (label != null)
                {
                    label.text = $"{item.Name}  {item.Price}金  {item.Effect}";
                    label.fontSize = 22;
                }

                var button = go.GetComponent<Button>();
                var id = item.Id;
                if (button != null)
                {
                    button.onClick.RemoveAllListeners();
                    button.onClick.AddListener(() => ViewModel.Session.Buy(id));
                    button.interactable = true;
                }
            }
        }

        private void EnsureHint()
        {
            var existing = transform.Find("duelHint");
            Text text;
            if (existing != null)
            {
                text = existing.GetComponent<Text>();
            }
            else
            {
                var go = new GameObject("duelHint", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
                go.transform.SetParent(transform, false);
                var rt = go.GetComponent<RectTransform>();
                rt.anchorMin = new Vector2(0.5f, 0.5f);
                rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.anchoredPosition = new Vector2(0f, 210f);
                rt.sizeDelta = new Vector2(980f, 120f);
                text = go.GetComponent<Text>();
                var sample = transform.Find("roundInfo")?.GetComponent<Text>() ??
                             GetComponentInChildren<Text>(true);
                text.font = sample != null
                    ? sample.font
                    : UnityEngine.Resources.GetBuiltinResource<Font>("Arial.ttf");
                text.fontSize = 36;
                text.alignment = TextAnchor.MiddleCenter;
                text.color = new Color(1f, 0.93f, 0.55f, 1f);
                text.horizontalOverflow = HorizontalWrapMode.Wrap;
                text.verticalOverflow = VerticalWrapMode.Overflow;
                text.raycastTarget = false;
            }

            if (text != null)
            {
                Binding.BindText(text, ViewModel.Hint);
            }
        }

        private void SetBtnLabel(string name, string label)
        {
            var button = FindBtn(name);
            if (button == null)
            {
                return;
            }

            var text = button.GetComponentInChildren<Text>();
            if (text != null)
            {
                text.text = label;
            }
        }

        private Transform ResolveSlot(string key)
        {
            if (UI != null && UI.TryGet<Component>(key, out var component) && component != null)
            {
                return component.transform;
            }

            return FindDeep(transform, key);
        }

        private Button FindBtn(string name)
        {
            var node = _btns != null ? _btns.Find(name) : null;
            if (node == null)
            {
                node = FindDeep(transform, name);
            }

            return node != null ? node.GetComponent<Button>() : null;
        }

        private static void HideChild(Transform root, string name)
        {
            var child = root.Find(name);
            if (child != null)
            {
                child.gameObject.SetActive(false);
            }
        }

        private static Text FindUiText(Transform root, string name)
        {
            var child = root.Find(name);
            if (child == null)
            {
                child = FindDeep(root, name);
            }

            return child != null ? child.GetComponent<Text>() : null;
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
    }
}
