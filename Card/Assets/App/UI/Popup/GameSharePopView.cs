using App.Resources;
using CardShare.Contracts;
using Framework.UI.Navigation;
using Framework.UI.View;
using TMPro;
using UnityEngine.UI;

namespace App.UI.Popup
{
    [AutoScreen(AppScreenIds.GameSharePop, UILayer.Popup, ResResourcePaths.GameSharePop)]
    public sealed class GameSharePopView : ViewBase<GameSharePopViewModel>
    {
        protected override void OnBind()
        {
            UI.GetGameObject("PeekGood").SetActive(ViewModel.SkillId == PvpSkillIds.Rub);
            UI.GetGameObject("TiHuanGood").SetActive(ViewModel.SkillId == PvpSkillIds.Replace);
            UI.GetGameObject("ChaKanGood").SetActive(ViewModel.SkillId == PvpSkillIds.Peek);
            UI.GetGameObject("PeekGood").GetComponentInChildren<TMP_Text>(true).text = "补充次数";
            UI.GetGameObject("TiHuanGood").GetComponentInChildren<TMP_Text>(true).text = "补充次数";
            UI.GetGameObject("ChaKanGood").GetComponentInChildren<TMP_Text>(true).text = "补充次数";
            Binding.BindCommand(UI.GetGameObject("YesBtn").GetComponent<Button>(), ViewModel.WatchCommand);
            Binding.BindCommand(UI.GetGameObject("NoBtn").GetComponent<Button>(), ViewModel.CloseCommand);
            Binding.BindCommand(UI.GetGameObject("CloseBtn").GetComponent<Button>(), ViewModel.CloseCommand);
            Binding.BindCommand(UI.GetGameObject("Mask").GetComponent<Button>(), ViewModel.CloseCommand);
        }
    }
}
