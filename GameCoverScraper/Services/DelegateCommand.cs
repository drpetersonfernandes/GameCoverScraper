using System.Windows.Input;

namespace GameCoverScraper.Services;

public class DelegateCommand : ICommand
{
    private readonly Action<object?> _execute;
    private readonly Func<object?, bool>? _canExecute;

    public DelegateCommand(Action<object?> execute, Func<object?, bool>? canExecute = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
        CommandManager.RequerySuggested += OnRequerySuggested;
    }

    private void OnRequerySuggested(object? sender, EventArgs e)
    {
        CanExecuteChanged?.Invoke(this, e);
    }

    public bool CanExecute(object? parameter)
    {
        return _canExecute?.Invoke(parameter) ?? true;
    }

    public void Execute(object? parameter)
    {
        _execute(parameter);
    }

    public event EventHandler? CanExecuteChanged;
}
