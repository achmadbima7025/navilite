using System.Windows.Input;

namespace NaviLite.Desktop.ViewModels;

public sealed class AsyncCommand(Func<Task> action, Func<bool> canExecute) : ICommand
{
    private bool _running;
    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => !_running && canExecute();

    public async void Execute(object? parameter)
    {
        if (!CanExecute(parameter)) return;
        _running = true;
        Refresh();
        try { await action(); }
        finally { _running = false; Refresh(); }
    }

    public void Refresh() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
