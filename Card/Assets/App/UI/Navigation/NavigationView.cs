using System.Collections.Generic;
using System.Threading.Tasks;
using App.Bootstrap;
using App.Guide;
using App.Resources;
using Framework.UI.Navigation;
using Framework.UI.View;
using UnityEngine;
using UnityEngine.UI;

namespace App.UI
{
    [AutoScreen(AppScreenIds.Navigation, UILayer.Navigation, ResResourcePaths.MainInterfaceBottom)]
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
