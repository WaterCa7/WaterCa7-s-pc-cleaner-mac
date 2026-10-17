using Avalonia.Threading;

namespace PcCleaner.Core;

/// <summary>
/// UI-thread helpers. App attaches the UI dispatcher at startup; until then (unit tests without an app) every action
/// runs right away on the calling thread.
/// </summary>
public static class Ui
{
    private static Dispatcher? _dispatcher;

    public static void Attach(Dispatcher? dispatcher) => _dispatcher = dispatcher;

    public static void Post(Action action)
    {
        var d = _dispatcher;
        if (d is null || d.CheckAccess())
            action();
        else
            d.Post(action);
    }

    public static void Invoke(Action action)
    {
        var d = _dispatcher;
        if (d is null || d.CheckAccess())
            action();
        else
            d.Invoke(action);
    }

    public static T Invoke<T>(Func<T> func)
    {
        var d = _dispatcher;
        return d is null || d.CheckAccess() ? func() : d.Invoke(func);
    }

    public static Task InvokeAsync(Func<Task> func)
    {
        var d = _dispatcher;
        return d is null || d.CheckAccess() ? func() : d.InvokeAsync(func);
    }

    public static Task<T> InvokeAsync<T>(Func<Task<T>> func)
    {
        var d = _dispatcher;
        return d is null || d.CheckAccess() ? func() : d.InvokeAsync(func);
    }
}
