namespace PcCleaner.Core;

/// <summary>How moving to the Trash went; the error is Russian (or macOS's own localized text).</summary>
public sealed record TrashOutcome(bool Ok, string? Error);

/// <summary>Moves files the user may want back to the Trash (never deletes them).</summary>
public interface ITrash
{
    Task<TrashOutcome> MoveAsync(string path, CancellationToken ct);
}

/// <summary>
/// NSFileManager first (Finder's «Вернуть» works); an item the user may not change (e.g. an app installed by the system)
/// goes through Finder, which asks for the administrator password itself. The path reaches AppleScript as an argument,
/// never as part of the script text.
/// </summary>
public sealed class MacTrash : ITrash
{
    internal static readonly string[] FinderScript =
    {
        "-e", "on run argv",
        "-e", "set target to (POSIX file (item 1 of argv)) as alias",
        "-e", "tell application \"Finder\" to delete target",
        "-e", "end run",
    };

    private readonly ICommandRunner _runner;
    private readonly Func<string, NativeTrashResult> _native;

    public MacTrash(ICommandRunner runner, Func<string, NativeTrashResult>? native = null)
    {
        _runner = runner;
        _native = native ?? MacNative.TrashItem;
    }

    public async Task<TrashOutcome> MoveAsync(string path, CancellationToken ct)
    {
        if (!Path.IsPathRooted(path))
            throw new ArgumentException($"Нужен полный путь, а не «{path}»", nameof(path));
        var native = _native(path);
        if (native.Ok)
        {
            Log.Action($"В Корзину: {path}");
            return new TrashOutcome(true, null);
        }
        if (native.PermissionDenied)
        {
            var finder = await _runner.RunAsync("/usr/bin/osascript", FinderScript.Append(path).ToArray(), TimeSpan.FromMinutes(2), ct);
            if (finder.Ok)
            {
                Log.Action($"В Корзину (через Finder): {path}");
                return new TrashOutcome(true, null);
            }
            var reason = finder.StdErr.Contains("-128", StringComparison.Ordinal)
                ? "отменено — ничего не изменено"
                : "Finder не смог переместить объект в Корзину";
            Log.Warn($"В Корзину: {path} — {reason} ({finder.StdErr.Trim()})");
            return new TrashOutcome(false, reason);
        }
        var error = native.Error ?? "не удалось переместить в Корзину";
        Log.Warn($"В Корзину: {path} — {error}");
        return new TrashOutcome(false, error);
    }
}
