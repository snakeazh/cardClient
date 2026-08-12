using Framework.UI.Core;
using Framework.UI.Dialog;
using Framework.UI.View;

namespace App.UI
{
    public sealed class HomeViewModel : ViewModelBase
    {
        private readonly IDialogService _dialogs;

        public HomeViewModel(IDialogService dialogs)
        {
            _dialogs = dialogs;
            Title = new ObservableProperty<string>("Card Client");
            Status = new ObservableProperty<string>("MVVM UI framework ready.");
            ShowDialogCommand = new RelayCommand(ShowDialog);
        }

        public ObservableProperty<string> Title { get; }
        public ObservableProperty<string> Status { get; }
        public IRelayCommand ShowDialogCommand { get; }

        private async void ShowDialog()
        {
            var result = await _dialogs.ConfirmAsync("Hello", "Framework dialog works.", DialogButtons.OkCancel);
            Status.Value = result == DialogResult.Ok ? "Confirmed." : "Cancelled.";
        }
    }
}
