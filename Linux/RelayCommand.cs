using System;
using System.Windows.Input;

namespace ULM.Linux
{
    /// <summary>
    /// Parameterloser ICommand für die Linux-GUI. Bewusst kein Wiederverwenden von
    /// Infrastructure/RelayCommand.cs — dieses nutzt System.Windows.Input.CommandManager
    /// (WPF-spezifisch, hier nicht verfügbar). CanExecuteChanged wird deshalb als reines
    /// C#-Event geführt statt an CommandManager.RequerySuggested zu hängen.
    /// </summary>
    public sealed class RelayCommand : ICommand
    {
        private readonly Action _execute;
        private readonly Func<bool>? _canExecute;

        public RelayCommand(Action execute, Func<bool>? canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public event EventHandler? CanExecuteChanged;

        public bool CanExecute(object? parameter) => _canExecute is null || _canExecute();

        public void Execute(object? parameter) => _execute();

        public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Parameterisierte Variante von <see cref="RelayCommand"/> — für Befehle, die pro
    /// Aufruf einen Wert aus dem XAML-`CommandParameter` brauchen (z.B. den Distro-Namen für den
    /// "⚡ schneller"-Button je Zeile, siehe LinuxMainViewModel.RequestFasterMirrorCommand).</summary>
    public sealed class RelayCommand<T> : ICommand
    {
        private readonly Action<T?> _execute;
        private readonly Func<T?, bool>? _canExecute;

        public RelayCommand(Action<T?> execute, Func<T?, bool>? canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public event EventHandler? CanExecuteChanged;

        public bool CanExecute(object? parameter) => _canExecute is null || _canExecute((T?)parameter);

        public void Execute(object? parameter) => _execute((T?)parameter);

        public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}
