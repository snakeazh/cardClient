using Framework.UI.Dialog;
using Framework.UI.Navigation;
using Framework.UI.View;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace App.UI.Dialog
{
    /// <summary>
    /// Confirm dialog view. Nodes are resolved via UIReference / UIBind.
    /// </summary>
    [AutoScreen(AppScreenIds.ConfirmDialog, UILayer.Popup)]
    public sealed class ConfirmDialogView : ViewBase<ConfirmDialogViewModel>
    {
        public const string ResourcesPath = "UI/ConfirmDialog";

        protected override void OnBind()
        {
            Binding.BindText(UI.Get<TMP_Text>("Title"), ViewModel.Title);
            Binding.BindText(UI.Get<TMP_Text>("Message"), ViewModel.Message);
            Binding.BindText(UI.Get<TMP_Text>("OkLabel"), ViewModel.OkLabel);
            Binding.BindText(UI.Get<TMP_Text>("CancelLabel"), ViewModel.CancelLabel);
            Binding.BindText(UI.Get<TMP_Text>("YesLabel"), ViewModel.YesLabel);
            Binding.BindText(UI.Get<TMP_Text>("NoLabel"), ViewModel.NoLabel);

            var okButton = UI.Get<Button>("OkButton");
            var cancelButton = UI.Get<Button>("CancelButton");
            var yesButton = UI.Get<Button>("YesButton");
            var noButton = UI.Get<Button>("NoButton");

            Binding.BindActive(okButton.gameObject, ViewModel.ShowOk);
            Binding.BindActive(cancelButton.gameObject, ViewModel.ShowCancel);
            Binding.BindActive(yesButton.gameObject, ViewModel.ShowYes);
            Binding.BindActive(noButton.gameObject, ViewModel.ShowNo);
            Binding.BindCommand(okButton, ViewModel.OkCommand);
            Binding.BindCommand(cancelButton, ViewModel.CancelCommand);
            Binding.BindCommand(yesButton, ViewModel.YesCommand);
            Binding.BindCommand(noButton, ViewModel.NoCommand);
        }
    }
}

