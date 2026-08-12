using System;

namespace Framework.UI.Core
{
    public interface IRelayCommand
    {
        event Action CanExecuteChanged;
        bool CanExecute();
        void Execute();
        void RaiseCanExecuteChanged();
    }

    public interface IRelayCommand<in T>
    {
        event Action CanExecuteChanged;
        bool CanExecute(T parameter);
        void Execute(T parameter);
        void RaiseCanExecuteChanged();
    }

    public sealed class RelayCommand : IRelayCommand
    {
        private readonly Action _execute;
        private readonly Func<bool> _canExecute;

        public event Action CanExecuteChanged;

        public RelayCommand(Action execute, Func<bool> canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public bool CanExecute() => _canExecute == null || _canExecute();

        public void Execute()
        {
            if (CanExecute())
            {
                _execute();
            }
        }

        public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke();
    }

    public sealed class RelayCommand<T> : IRelayCommand<T>
    {
        private readonly Action<T> _execute;
        private readonly Func<T, bool> _canExecute;

        public event Action CanExecuteChanged;

        public RelayCommand(Action<T> execute, Func<T, bool> canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public bool CanExecute(T parameter) => _canExecute == null || _canExecute(parameter);

        public void Execute(T parameter)
        {
            if (CanExecute(parameter))
            {
                _execute(parameter);
            }
        }

        public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke();
    }
}
