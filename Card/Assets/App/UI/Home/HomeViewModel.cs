using App.Game;
using Framework.UI;
using Framework.UI.Core;
using Framework.UI.View;

namespace App.UI
{
    public sealed class HomeViewModel : ViewModelBase
    {
        private readonly IUIManager _ui;
        private readonly GameSession _session;
        private readonly GameTableViewModel _tableVm;
        private readonly GameTableController _table;

        public HomeViewModel(
            IUIManager ui,
            GameSession session,
            GameTableViewModel tableVm,
            GameTableController table)
        {
            _ui = ui;
            _session = session;
            _tableVm = tableVm;
            _table = table;
            Title = new ObservableProperty<string>("炸金花：搓牌对决");
            Status = new ObservableProperty<string>("心理博弈 · 盲搓改命 · 关卡闯关");
            ShowDialogCommand = new RelayCommand(StartGame);
        }

        public ObservableProperty<string> Title { get; }
        public ObservableProperty<string> Status { get; }
        public IRelayCommand ShowDialogCommand { get; }

        private async void StartGame()
        {
            _session.StartNewRun();
            _table.Attach(_tableVm);
            await _ui.Close(this);
        }
    }
}
