using PcCleaner.Core;

namespace PcCleaner.Modules.Startup;

/// <param name="NeedsPermission">macOS has not let the app control System Events (Автоматизация).</param>
public sealed record LoginItemsResult(IReadOnlyList<LoginItem> Items, string? Error, bool NeedsPermission = false);

/// <summary>
/// Apps opened at login, through System Events (macOS asks once for permission, so this runs only on a button press).
/// Names and paths reach AppleScript as arguments, never as part of the script text.
/// </summary>
public sealed class LoginItemsService
{
    internal static readonly string[] ListScript =
    {
        "set out to \"\"",
        "tell application \"System Events\"",
        "repeat with anItem in login items",
        "try",
        "set out to out & (name of anItem) & tab & (path of anItem) & tab & ((hidden of anItem) as text) & linefeed",
        "end try",
        "end repeat",
        "end tell",
        "return out",
    };

    internal static readonly string[] RemoveScript =
    {
        "on run argv",
        "tell application \"System Events\" to delete (every login item whose name is (item 2 of argv))",
        "end run",
    };

    internal static readonly string[] AddScript =
    {
        "on run argv",
        "tell application \"System Events\" to make login item at end with properties {path:(item 2 of argv), hidden:((item 3 of argv) is \"true\")}",
        "end run",
    };

    // The first argument is ours: whatever the user's text starts with, osascript never reads it as one of its options.
    private const string Marker = "login-item";

    private readonly ICommandRunner _runner;
    private readonly BackupStore _backups;

    public LoginItemsService(ICommandRunner runner, BackupStore backups)
    {
        _runner = runner;
        _backups = backups;
    }

    public static IReadOnlyList<string> Arguments(IReadOnlyList<string> script, params string[] values)
        => script.SelectMany(line => new[] { "-e", line }).Concat(values.Length == 0 ? Array.Empty<string>() : values.Prepend(Marker)).ToList();

    public async Task<LoginItemsResult> ListAsync(CancellationToken ct)
    {
        var result = await RunAsync(Arguments(ListScript), ct);
        if (!result.Ok)
            return Failed(result, "Не удалось прочитать объекты входа");
        return new LoginItemsResult(Parse(result.StdOut), null);
    }

    /// <summary>Removes the item from the list (the app itself stays); its name, path and «скрывать» go to the backups first.</summary>
    public async Task<StartupResult> RemoveAsync(LoginItem item, CancellationToken ct)
    {
        try
        {
            _backups.SaveLoginItem(item.Name, item.Path, item.Hidden);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new StartupResult(false, $"Не удалось сохранить резервную копию — ничего не изменено: {ErrorText.For(ex)}");
        }
        var result = await RunAsync(Arguments(RemoveScript, item.Name), ct);
        if (!result.Ok)
            return new StartupResult(false, Failed(result, $"Не удалось убрать «{item.Name}» из объектов входа").Error!);
        Log.Action($"Объект входа «{item.Name}» ({item.Path}) убран");
        return new StartupResult(true, $"«{item.Name}» больше не открывается при входе (вернуть можно в «Настройках»).");
    }

    public async Task<StartupResult> AddAsync(string path, bool hidden, CancellationToken ct)
    {
        var name = Path.GetFileNameWithoutExtension(path.TrimEnd('/'));
        if (!path.StartsWith('/'))
            return new StartupResult(false, $"Нужен полный путь к программе, а не «{path}».");
        var result = await RunAsync(Arguments(AddScript, path, hidden ? "true" : "false"), ct);
        if (!result.Ok)
            return new StartupResult(false, Failed(result, $"Не удалось добавить «{name}» в объекты входа").Error!);
        Log.Action($"Объект входа «{name}» ({path}) добавлен");
        return new StartupResult(true, $"«{name}» снова открывается при входе.");
    }

    /// <summary>Lines of "name&lt;tab&gt;path&lt;tab&gt;hidden".</summary>
    public static IReadOnlyList<LoginItem> Parse(string text)
        => text.Split('\n')
            .Select(line => line.TrimEnd('\r').Split('\t'))
            .Where(parts => parts.Length == 3 && parts[0].Length > 0)
            .Select(parts => new LoginItem(parts[0], parts[1].Length > 1 ? parts[1].TrimEnd('/') : parts[1], parts[2] == "true"))
            .ToList();

    private Task<CommandResult> RunAsync(IReadOnlyList<string> args, CancellationToken ct)
        => _runner.RunAsync("/usr/bin/osascript", args, TimeSpan.FromSeconds(60), ct);

    private static LoginItemsResult Failed(CommandResult result, string what)
    {
        if (result.StdErr.Contains("-1743", StringComparison.Ordinal) || result.StdErr.Contains("-10004", StringComparison.Ordinal))
        {
            return new LoginItemsResult(Array.Empty<LoginItem>(),
                "Нет разрешения управлять «System Events». Разрешите в Системных настройках → Конфиденциальность и безопасность → Автоматизация.", true);
        }
        if (result.TimedOut)
            return new LoginItemsResult(Array.Empty<LoginItem>(), what + ": System Events не ответили (возможно, macOS ждёт ответа на запрос разрешения).");
        var reason = result.StdErr.Trim().Split('\n')[0].Trim();
        return new LoginItemsResult(Array.Empty<LoginItem>(), reason.Length == 0 ? what + "." : $"{what}: {reason}");
    }
}
