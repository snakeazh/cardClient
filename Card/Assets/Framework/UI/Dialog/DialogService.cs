using System;
using System.Threading.Tasks;
using Framework.UI.Core;
using Framework.UI.DI;
using Framework.UI.Navigation;
using Framework.UI.View;

namespace Framework.UI.Dialog
{
    public enum DialogButtons
    {
        Ok = 0,
        OkCancel = 1,
        YesNo = 2
    }

    public enum DialogResult
    {
        None = 0,
        Ok = 1,
        Cancel = 2,
        Yes = 3,
        No = 4
    }

    public sealed class ConfirmDialogArgs
    {
        public ConfirmDialogArgs(string title, string message, DialogButtons buttons = DialogButtons.OkCancel)
        {
            Title = title ?? string.Empty;
            Message = message ?? string.Empty;
            Buttons = buttons;
        }

        public string Title { get; }
        public string Message { get; }
        public DialogButtons Buttons { get; }
    }

    public interface IDialogService
    {
        Task<DialogResult> ConfirmAsync(string title, string message, DialogButtons buttons = DialogButtons.OkCancel);
        Task<TResult> ShowCustomAsync<TVm, TResult>(TVm viewModel, object args = null) where TVm : ViewModelBase;
        Task CloseWithResult<TResult>(TResult result);
    }

    public sealed class DialogService : IDialogService
    {
        public static readonly ScreenId ConfirmScreenId = new ScreenId("Framework.ConfirmDialog");

        private readonly IUINavigator _navigator;
        private readonly ServiceContainer _container;
        private TaskCompletionSource<DialogResult> _confirmTcs;
        private TaskCompletionSource<object> _customTcs;
        private ViewModelBase _activeDialogViewModel;

        public DialogService(IUINavigator navigator, ServiceContainer container)
        {
            _navigator = navigator ?? throw new ArgumentNullException(nameof(navigator));
            _container = container ?? throw new ArgumentNullException(nameof(container));
        }

        public async Task<DialogResult> ConfirmAsync(
            string title,
            string message,
            DialogButtons buttons = DialogButtons.OkCancel)
        {
            if (_confirmTcs != null)
            {
                throw new InvalidOperationException("A confirm dialog is already open.");
            }

            _confirmTcs = new TaskCompletionSource<DialogResult>();
            var viewModel = _container.Resolve<ConfirmDialogViewModel>();
            _activeDialogViewModel = viewModel;
            await _navigator.Open(
                ConfirmScreenId,
                viewModel,
                new ConfirmDialogArgs(title, message, buttons));
            var result = await _confirmTcs.Task;
            _confirmTcs = null;
            _activeDialogViewModel = null;
            return result;
        }

        public async Task<TResult> ShowCustomAsync<TVm, TResult>(TVm viewModel, object args = null)
            where TVm : ViewModelBase
        {
            if (viewModel == null)
            {
                throw new ArgumentNullException(nameof(viewModel));
            }

            if (_customTcs != null)
            {
                throw new InvalidOperationException("A custom dialog is already awaiting a result.");
            }

            _customTcs = new TaskCompletionSource<object>();
            _activeDialogViewModel = viewModel;
            await _navigator.Open(viewModel, args);
            var result = await _customTcs.Task;
            _customTcs = null;
            _activeDialogViewModel = null;
            return (TResult)result;
        }

        public Task CloseWithResult<TResult>(TResult result)
        {
            return CompleteCustom(result);
        }

        internal Task CompleteConfirm(DialogResult result)
        {
            return CompleteConfirmCore(result);
        }

        private async Task CompleteConfirmCore(DialogResult result)
        {
            if (_activeDialogViewModel != null)
            {
                await _navigator.Close(_activeDialogViewModel);
            }
            else
            {
                await _navigator.Close(UILayer.Popup);
            }

            _confirmTcs?.TrySetResult(result);
        }

        private async Task CompleteCustom(object result)
        {
            if (_activeDialogViewModel != null)
            {
                await _navigator.Close(_activeDialogViewModel);
            }
            else
            {
                await _navigator.Close(UILayer.Popup);
            }

            _customTcs?.TrySetResult(result);
        }
    }

    public sealed class ConfirmDialogViewModel : ViewModelBase
    {
        private readonly DialogService _dialogs;

        public ConfirmDialogViewModel(DialogService dialogs)
        {
            _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
            Title = new ObservableProperty<string>();
            Message = new ObservableProperty<string>();
            ShowOk = new ObservableProperty<bool>(true);
            ShowCancel = new ObservableProperty<bool>(true);
            ShowYes = new ObservableProperty<bool>(false);
            ShowNo = new ObservableProperty<bool>(false);
            OkLabel = new ObservableProperty<string>("OK");
            CancelLabel = new ObservableProperty<string>("Cancel");
            YesLabel = new ObservableProperty<string>("Yes");
            NoLabel = new ObservableProperty<string>("No");

            OkCommand = new RelayCommand(() => { _ = _dialogs.CompleteConfirm(DialogResult.Ok); });
            CancelCommand = new RelayCommand(() => { _ = _dialogs.CompleteConfirm(DialogResult.Cancel); });
            YesCommand = new RelayCommand(() => { _ = _dialogs.CompleteConfirm(DialogResult.Yes); });
            NoCommand = new RelayCommand(() => { _ = _dialogs.CompleteConfirm(DialogResult.No); });
        }

        public ObservableProperty<string> Title { get; }
        public ObservableProperty<string> Message { get; }
        public ObservableProperty<bool> ShowOk { get; }
        public ObservableProperty<bool> ShowCancel { get; }
        public ObservableProperty<bool> ShowYes { get; }
        public ObservableProperty<bool> ShowNo { get; }
        public ObservableProperty<string> OkLabel { get; }
        public ObservableProperty<string> CancelLabel { get; }
        public ObservableProperty<string> YesLabel { get; }
        public ObservableProperty<string> NoLabel { get; }

        public IRelayCommand OkCommand { get; }
        public IRelayCommand CancelCommand { get; }
        public IRelayCommand YesCommand { get; }
        public IRelayCommand NoCommand { get; }

        protected override Task OnOpen(object args)
        {
            var request = args as ConfirmDialogArgs ?? new ConfirmDialogArgs("Confirm", string.Empty);
            Title.Value = request.Title;
            Message.Value = request.Message;

            switch (request.Buttons)
            {
                case DialogButtons.Ok:
                    ShowOk.Value = true;
                    ShowCancel.Value = false;
                    ShowYes.Value = false;
                    ShowNo.Value = false;
                    OkLabel.Value = "OK";
                    break;
                case DialogButtons.YesNo:
                    ShowOk.Value = false;
                    ShowCancel.Value = false;
                    ShowYes.Value = true;
                    ShowNo.Value = true;
                    break;
                default:
                    ShowOk.Value = true;
                    ShowCancel.Value = true;
                    ShowYes.Value = false;
                    ShowNo.Value = false;
                    OkLabel.Value = "OK";
                    CancelLabel.Value = "Cancel";
                    break;
            }

            return Task.CompletedTask;
        }
    }
}
