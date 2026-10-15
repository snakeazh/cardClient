using System.Collections.Generic;
using System.Threading.Tasks;
using App.Bootstrap;
using App.Game;
using App.Guide;
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
        private GuideTargetRegistry _guideTargets;
        private readonly List<string> _guideTargetIds = new List<string>(1);

        protected override void OnBind()
        {
            _playerItem = GetComponentInChildren<PlayerItem>(true);
            Binding.BindText(UI.GetGameObject("StaminaNum").GetComponent<TMP_Text>(), ViewModel.StaminaText);
            Binding.BindText(UI.GetGameObject("pvpValue").GetComponent<TMP_Text>(), ViewModel.PvpValueText);
            var pvpTime = UI.GetGameObject("pvpTime");
            Binding.BindText(pvpTime.GetComponent<TMP_Text>(), ViewModel.PvpTimeText);
            Binding.BindActive(pvpTime, ViewModel.ShowPvpTime);
            Binding.BindCommand(UI.GetGameObject("startBtn").GetComponent<Button>(), ViewModel.StartCommand);
            BindTTRewardItem();
            Binding.BindCommand(UI.GetGameObject("IllustrateBtn").GetComponent<Button>(), ViewModel.OpenIllustrateCommand);
            var talentBtn = UI.GetGameObject("TalentBtn");
            Binding.BindCommand(talentBtn.GetComponent<Button>(), ViewModel.OpenTalentCommand);
            RegisterGuideTalentBtn(talentBtn);
            Binding.BindCommand(UI.GetGameObject("pvpBtn").GetComponent<Button>(), ViewModel.PvpCommand);
            BindHero();
        }

        /// <summary>抖音侧边栏奖励入口；微信小游戏无此能力，直接隐藏。</summary>
        private void BindTTRewardItem()
        {
            var item = UI.GetGameObject("TTRewardItem");
#if WEIXINMINIGAME || PLATFORM_WEIXINMINIGAME
            item.SetActive(false);
#else
            Binding.BindCommand(item.GetComponent<Button>(), ViewModel.OpenTTRewardCommand);
#endif
        }

        protected override Task OnViewOpen()
        {
            BindHero();
            return Task.CompletedTask;
        }

        protected override async Task OnViewClose()
        {
            UnregisterGuideTargets();
            await base.OnViewClose();
        }

        /// <summary>
        /// 引导目标 Navigation.TalentBtn 原由底部导航栏注册，导航栏停用后
        /// 由 Home 的 TalentBtn 接管（GuideStepConfig.TargetId 未变）。
        /// </summary>
        private void RegisterGuideTalentBtn(GameObject talentBtn)
        {
            if (talentBtn == null || !AppServices.IsReady)
            {
                return;
            }

            _guideTargets = AppServices.Resolve<GuideTargetRegistry>();
            _guideTargets.RegisterUi(GuideTargetIds.TalentBtn, (RectTransform)talentBtn.transform);
            _guideTargetIds.Add(GuideTargetIds.TalentBtn);
        }

        private void UnregisterGuideTargets()
        {
            if (_guideTargets != null && _guideTargetIds.Count > 0)
            {
                _guideTargets.UnregisterAll(_guideTargetIds);
            }

            _guideTargetIds.Clear();
        }

        public override void OnPresented()
        {
            ViewModel.OnPresented();
        }

        private void BindHero()
        {
            if (_playerItem == null)
            {
                return;
            }

            var hero = ViewModel.Hero;
            _playerItem.ApplyTheme();
            if (hero == null)
            {
                _playerItem.SetName(string.Empty);
                _playerItem.SetHp(0);
                _playerItem.SetAttack(0);
                _playerItem.SetPortrait(null);
                return;
            }

            var stats = ViewModel.PanelStats;
            _playerItem.SetName(hero.Name);
            _playerItem.SetHp(stats.Hp);
            _playerItem.SetAttack(stats.Attack);
            _playerItem.SetPortrait(PortraitLoader.GetRole(hero.Icon));
        }
    }
}
