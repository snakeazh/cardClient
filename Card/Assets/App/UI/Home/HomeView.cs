using System.Collections.Generic;
using System.Threading.Tasks;
using App.Bootstrap;
using App.Game;
using App.Guide;
using App.Resources;
using DG.Tweening;
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
            Binding.BindCommand(UI.GetGameObject("startBtn").GetComponent<Button>(), ViewModel.StartCommand);
            BindTTRewardItem();
            // 插图屋（IllustrateBtn）→ 图鉴怪物栏；圣物室（RealicItemBtn）→ 图鉴圣物栏
            Binding.BindCommand(UI.GetGameObject("IllustrateBtn").GetComponent<Button>(), ViewModel.OpenIllustrateCommand);
            Binding.BindCommand(UI.GetGameObject("RealicItemBtn").GetComponent<Button>(), ViewModel.OpenRelicBookCommand);
            var talentBtn = UI.GetGameObject("TalentBtn");
            Binding.BindCommand(talentBtn.GetComponent<Button>(), ViewModel.OpenTalentCommand);
            RegisterGuideTalentBtn(talentBtn);
            BindSettingBtn();
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

        /// <summary>设置入口（SettingBtn）。</summary>
        private void BindSettingBtn()
        {
            var setting = UI.GetGameObject("SettingBtn");
            if (setting == null)
            {
                return;
            }

            var button = setting.GetComponent<Button>();
            if (button == null)
            {
                button = setting.AddComponent<Button>();
                button.transition = Selectable.Transition.None;
            }

            Binding.BindCommand(button, ViewModel.OpenSettingCommand);
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
            PlayPresentFade();
        }

        /// <summary>
        /// 页面首次露出时整体淡入，避免从加载画面/上一页硬切闪现，从暗到亮柔和浮现。
        /// 注意：LevelUI 是 Popup，关闭它不会隐藏 Home，这条淡入只在 Home 真正入栈时才会播。
        /// </summary>
        private void PlayPresentFade()
        {
            var group = GetComponent<CanvasGroup>();
            if (group == null)
            {
                group = gameObject.AddComponent<CanvasGroup>();
            }

            group.DOKill();
            group.alpha = 0f;
            group.blocksRaycasts = false;
            group.DOFade(1f, 0.18f)
                .SetEase(Ease.OutQuad)
                .SetUpdate(true)
                .OnComplete(() => group.blocksRaycasts = true)
                .SetLink(gameObject, LinkBehaviour.KillOnDestroy);
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
