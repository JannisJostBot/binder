using System.Windows.Input;

namespace GameShelf.ViewModels;

/// <summary>A command that runs an action; bound with <c>Command="{x:Bind ViewModel.SomeCommand}"</c>.</summary>
public sealed class RelayCommand(Action execute, Func<bool>? canExecute = null) : ICommand
{
    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => canExecute?.Invoke() ?? true;

    public void Execute(object? parameter)
    {
        if (CanExecute(parameter)) execute();
    }

    /// <summary>Call when what <c>canExecute</c> reads has changed, so bound buttons enable or disable.</summary>
    public void NotifyCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
