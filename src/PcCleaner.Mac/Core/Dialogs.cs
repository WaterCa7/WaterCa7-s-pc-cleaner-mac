namespace PcCleaner.Core;

public enum DialogKind
{
    /// <summary>A question before a change to the system.</summary>
    Warning,

    /// <summary>A neutral question after which nothing on the system changes («Открыть отчёт?»).</summary>
    Question,

    Info,
    Error,
}

public sealed record DialogRequest(string Title, string Message, DialogKind Kind);

/// <summary>
/// Message boxes behind replaceable handlers so tests and screenshot mode never block or confirm anything.
/// Every change to the system MUST be confirmed through <see cref="ConfirmAsync"/> first.
/// Until the app installs its windows (App.axaml.cs) the handlers are silent: every question is answered «Нет».
/// </summary>
public static class Dialogs
{
    private static Func<DialogRequest, Task<bool>> _confirmHandler = SilentConfirm;
    private static Func<DialogRequest, Task> _messageHandler = SilentMessage;

    public static Func<DialogRequest, Task<bool>> ConfirmHandler
    {
        get => _confirmHandler;
        set => _confirmHandler = value ?? SilentConfirm;
    }

    public static Func<DialogRequest, Task> MessageHandler
    {
        get => _messageHandler;
        set => _messageHandler = value ?? SilentMessage;
    }

    /// <summary>Yes/No question about a change (warning icon). True only if the user explicitly pressed «Да».</summary>
    public static Task<bool> ConfirmAsync(string title, string message)
        => Ui.InvokeAsync(() => ConfirmHandler(new DialogRequest(title, message, DialogKind.Warning)));

    /// <summary>Neutral Yes/No question where «Да» changes nothing on the system (question icon).</summary>
    public static Task<bool> AskAsync(string title, string message)
        => Ui.InvokeAsync(() => ConfirmHandler(new DialogRequest(title, message, DialogKind.Question)));

    public static Task InfoAsync(string title, string message)
        => Ui.InvokeAsync(() => MessageHandler(new DialogRequest(title, message, DialogKind.Info)));

    public static Task ErrorAsync(string title, string message)
        => Ui.InvokeAsync(() => MessageHandler(new DialogRequest(title, message, DialogKind.Error)));

    /// <summary>Shows an error without waiting for it to be closed (command error paths).</summary>
    public static void Error(string title, string message) => _ = ErrorAsync(title, message);

    /// <summary>Non-interactive mode: every question is declined, nothing is shown, errors go to the log.</summary>
    public static void UseSilentMode()
    {
        ConfirmHandler = SilentConfirm;
        MessageHandler = SilentMessage;
    }

    private static Task<bool> SilentConfirm(DialogRequest request) => Task.FromResult(false);

    private static Task SilentMessage(DialogRequest request)
    {
        if (request.Kind == DialogKind.Error)
            Log.Warn($"[диалог] {request.Title}: {request.Message}");
        return Task.CompletedTask;
    }
}
