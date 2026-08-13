using System;
using System.Threading.Tasks;
using App.Resources;
using Framework.UI.Core;
using Framework.UI.Navigation;
using Framework.UI.View;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace App.UI
{
    /// <summary>
    /// 对局 HUD（原 GameTableController.BuildHud / 文本刷新逻辑全部在这里）。
    /// 牌桌世界表现由 <see cref="GameBoardController"/> 负责。
    /// </summary>
    [AutoScreen(AppScreenIds.GameUI, UILayer.Page, ResResourcePaths.GameUI)]
    public sealed class GameUIView : ViewBase<GameTableViewModel>
    {


        protected override void OnBind()
        {
         
        }

        protected override Task OnViewOpen()
        {
 
            return Task.CompletedTask;
        }

        protected override Task OnViewClose()
        {

            return Task.CompletedTask;
        }


    }
}
