using System.Windows.Input;

namespace PcCleaner.Core;

/// <summary>
/// Base of the app's commands. WPF re-queried CanExecute through CommandManager; Avalonia has no such thing, so every
/// command registers itself and <see cref="Refresh"/> raises CanExecuteChanged on all of them that are still alive.
/// </summary>
public abstract class CommandBase : ObservableObject, ICommand
{
    private static readonly object RegistryLock = new();
    private static readonly List<WeakReference<CommandBase>> Registry = new();

    protected CommandBase()
    {
        lock (RegistryLock)
        {
            if (Registry.Count > 256)
                Registry.RemoveAll(r => !r.TryGetTarget(out _));
            Registry.Add(new WeakReference<CommandBase>(this));
        }
    }

    public event EventHandler? CanExecuteChanged;

    public abstract bool CanExecute(object? parameter);

    public abstract void Execute(object? parameter);

    public void RaiseCanExecuteChanged() => Ui.Post(() => CanExecuteChanged?.Invoke(this, EventArgs.Empty));

    /// <summary>Re-evaluates CanExecute of every command (after IsBusy and similar state changes).</summary>
    public static void Refresh()
    {
        List<CommandBase> alive;
        lock (RegistryLock)
        {
            alive = new List<CommandBase>(Registry.Count);
            foreach (var reference in Registry)
            {
                if (reference.TryGetTarget(out var command))
                    alive.Add(command);
            }
        }
        foreach (var command in alive)
            command.RaiseCanExecuteChanged();
    }

    protected static void Report(Exception ex, string what)
    {
        Log.Error(what, ex);
        Dialogs.Error("Ошибка", ErrorText.Sentence(ex));
    }
}

public sealed class RelayCommand : CommandBase
{
    private readonly Action<object?> _execute;
    private readonly Func<object?, bool>? _canExecute;

    public RelayCommand(Action execute, Func<bool>? canExecute = null)
        : this(_ => execute(), canExecute is null ? null : _ => canExecute())
    {
    }

    public RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    public override bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;

    public override void Execute(object? parameter)
    {
        try
        {
            _execute(parameter);
        }
        catch (Exception ex)
        {
            Report(ex, "Ошибка выполнения команды");
        }
    }
}

/// <summary>Async command that blocks re-entry while running and reports exceptions instead of crashing the app.</summary>
public sealed class AsyncRelayCommand : CommandBase
{
    private readonly Func<object?, Task> _execute;
    private readonly Func<object?, bool>? _canExecute;
    private bool _isRunning;

    public AsyncRelayCommand(Func<Task> execute, Func<bool>? canExecute = null)
        : this(_ => execute(), canExecute is null ? null : _ => canExecute())
    {
    }

    public AsyncRelayCommand(Func<object?, Task> execute, Func<object?, bool>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    public bool IsRunning
    {
        get => _isRunning;
        private set
        {
            if (Set(ref _isRunning, value))
                RaiseCanExecuteChanged();
        }
    }

    public override bool CanExecute(object? parameter) => !IsRunning && (_canExecute?.Invoke(parameter) ?? true);

    public override async void Execute(object? parameter) => await ExecuteAsync(parameter);

    public async Task ExecuteAsync(object? parameter = null)
    {
        if (IsRunning)
            return;
        IsRunning = true;
        try
        {
            await _execute(parameter);
        }
        catch (OperationCanceledException)
        {
            Log.Info("Операция отменена");
        }
        catch (Exception ex)
        {
            Report(ex, "Ошибка выполнения операции");
        }
        finally
        {
            IsRunning = false;
        }
    }
}
