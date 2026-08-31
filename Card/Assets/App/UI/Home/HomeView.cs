using System.Threading.Tasks;
using App.Game;
using App.Resources;
using Framework.UI.Navigation;
using Framework.UI.View;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace App.UI
{
    /// <summary>
    /// Home page view. Nodes are resolved via UIReference / UIBind.
    /// </summary>
    [AutoScreen(AppScreenIds.Home, UILayer.Page, ResResourcePaths.Home)]
    public sealed class HomeView : ViewBase<HomeViewModel>
    {
        private PlayerItem _playerItem;

        protected override void OnBind()
        {
            _playerItem = GetComponentInChildren<PlayerItem>(true);
            Binding.BindText(UI.GetGameObject("LastStageInfo").GetComponent<TMP_Text>(), ViewModel.LastStageInfo);
            Binding.BindCommand(UI.GetGameObject("startBtn").GetComponent<Button>(), ViewModel.StartCommand);
            BindHero();
        }

        protected override Task OnViewOpen()
        {
            BindHero();
            return Task.CompletedTask;
        }

        private void BindHero()
        {
            if (_playerItem == null)
            {
                return;
            }

            var hero = ViewModel.Hero;
            _playerItem.ApplyTheme(false);
            if (hero == null)
            {
                _playerItem.SetName(string.Empty);
                _playerItem.SetHp(0);
                _playerItem.SetAttack(0);
                _playerItem.SetState(string.Empty);
                _playerItem.SetPortrait(null);
                return;
            }

            _playerItem.SetName(hero.Name);
            _playerItem.SetHp(hero.Hp);
            _playerItem.SetAttack(hero.HeroDamage);
            _playerItem.SetState(string.Empty);
            _playerItem.SetPortrait(PortraitLoader.GetRole(hero.Icon));
        }
    }
}
