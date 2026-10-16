using System.Collections.Generic;
using System.Threading.Tasks;
using App.Bootstrap;
using App.Guide;
using Framework.UI.View;
using UnityEngine;
using UnityEngine.UI;

namespace App.UI
{
    /// <summary>
    /// 底部导航栏（已停用）：入口改由 Home 预制体按钮承担（图鉴/天赋/设置）。
    /// 刻意保留 [AutoScreen] 移除状态——特性一旦存在，ScreenAutoRegistrar 会自动注册
    /// 本 View 与 NavigationViewModel，任何残留的 EnsureShown() 调用都会把底栏拉回
    /// （PvpPageViewModel 曾因合并带回该调用导致底栏偶发重现）。
    /// 恢复方法：加回特性 [AutoScreen(AppScreenIds.Navigation, UILayer.Navigation,
    /// ResResourcePaths.MainInterfaceBottom)] 并补 AppBootstrap 单例注册即可。
    /// </summary>
    public sealed class NavigationView : ViewBase<NavigationViewModel>
    {
        private GuideTargetRegistry _guideTargets;
        private readonly List<string> _guideTargetIds = new List<string>(2);

        protected override void OnBind()
        {
            var adventure = UI.Get<Toggle>("AdventureBtn");
            var collect = UI.Get<Toggle>("CollectBtn");
            Binding.BindToggle(adventure, ViewModel.AdventureOn);
            Binding.BindToggle(collect, ViewModel.CollectOn);
            Binding.Add(ViewModel.AdventureOn.Subscribe(on =>
            {
                if (on)
                {
                    ViewModel.ShowHome();
                }
            }, emitCurrent: false));
            Binding.Add(ViewModel.CollectOn.Subscribe(on =>
            {
                if (on)
                {
                    ViewModel.ShowIllustratedBook();
                }
            }, emitCurrent: false));

            if (UI.TryGet<Toggle>("TalentBtn", out var talent))
            {
                Binding.BindToggle(talent, ViewModel.TalentOn);
                Binding.Add(ViewModel.TalentOn.Subscribe(on =>
                {
                    if (on)
                    {
                        ViewModel.ShowTalent();
                    }
                }, emitCurrent: false));
                RegisterGuideTalentBtn(talent);
            }
        }

        protected override async Task OnViewClose()
        {
            UnregisterGuideTargets();
            await base.OnViewClose();
        }

        private void RegisterGuideTalentBtn(Toggle talent)
        {
            if (talent == null || !AppServices.IsReady)
            {
                return;
            }

            UnregisterGuideTargets();
            _guideTargets = AppServices.Resolve<GuideTargetRegistry>();
            _guideTargets.RegisterUi(GuideTargetIds.TalentBtn, (RectTransform)talent.transform);
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
    }
}
