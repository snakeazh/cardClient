using App.Resources;
using Framework.UI.Navigation;
using Framework.UI.View;
using UnityEngine.UI;

namespace App.UI
{
    [AutoScreen(AppScreenIds.Navigation, UILayer.Navigation, ResResourcePaths.MainInterfaceBottom)]
    public sealed class NavigationView : ViewBase<NavigationViewModel>
    {
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
            }
        }
    }
}
