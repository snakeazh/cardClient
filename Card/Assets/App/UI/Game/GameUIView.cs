using System.Threading.Tasks;
using App.Resources;
using Framework.UI.Navigation;
using Framework.UI.View;
using UnityEngine;
using UnityEngine.UI;

namespace App.UI
{
    /// <summary>
    /// 对局 HUD，加载方式与 HomeView 相同（AutoScreen + UI/GameUI）。
    /// 打开时实例化 GameHud 世界牌桌，关闭时销毁。
    /// </summary>
    [AutoScreen(AppScreenIds.GameUI, UILayer.Page, ResResourcePaths.GameUI)]
    public sealed class GameUIView : ViewBase<GameTableViewModel>
    {
        private GameObject _gameHud;
        private GameBoardController _board;

        protected override void OnBind()
        {
            var playerInfo = transform.Find("PlayerInfo");
            if (playerInfo == null)
            {
                return;
            }

            Binding.BindText(FindUiText(playerInfo, "chip"), ViewModel.PlayerChips);
            Binding.BindText(FindUiText(playerInfo, "Text (2)"), ViewModel.PlayerBet);
            Binding.BindText(FindUiText(playerInfo, "state"), ViewModel.PlayerState);
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
        }

        protected override Task OnViewClose()
        {
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

        private static Text FindUiText(Transform root, string name)
        {
            var child = root.Find(name);
            return child != null ? child.GetComponent<Text>() : null;
        }
    }
}
