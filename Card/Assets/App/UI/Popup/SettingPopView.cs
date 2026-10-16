using App.Resources;
using Framework.UI.Core;
using Framework.UI.Navigation;
using Framework.UI.View;
using UnityEngine;
using UnityEngine.UI;

namespace App.UI.Popup
{
    /// <summary>
    /// 设置弹窗。UIBind 键：Mask / CloseBtn / MusicToggleBtn / SoundEffectToggleBtn（美术已配好）。
    /// 开关节点未挂 Button（原外部脚本引用已清），代码补挂，点击热区靠子节点 Background 的 Image 冒泡；
    /// 勾选态 = Background/Checkmark 子节点显隐。
    /// </summary>
    [AutoScreen(AppScreenIds.SettingPop, UILayer.Popup, ResResourcePaths.SettingPop)]
    public sealed class SettingPopView : ViewBase<SettingPopViewModel>
    {
        protected override void OnBind()
        {
            Binding.BindCommand(UI.Get<Button>("Mask"), ViewModel.CloseCommand);
            Binding.BindCommand(UI.Get<Button>("CloseBtn"), ViewModel.CloseCommand);
            BindToggle("MusicToggleBtn", ViewModel.MusicOn, ViewModel.ToggleMusicCommand);
            BindToggle("SoundEffectToggleBtn", ViewModel.SoundOn, ViewModel.ToggleSoundCommand);
        }

        private void BindToggle(string key, ObservableProperty<bool> on, IRelayCommand command)
        {
            var node = UI.GetGameObject(key);
            var button = node.GetComponent<Button>();
            if (button == null)
            {
                button = node.AddComponent<Button>();
                button.transition = Selectable.Transition.None;
            }

            Binding.BindCommand(button, command);

            var checkmark = node.transform.Find("Background/Checkmark");
            if (checkmark != null)
            {
                Binding.BindActive(checkmark.gameObject, on);
            }
        }
    }
}
