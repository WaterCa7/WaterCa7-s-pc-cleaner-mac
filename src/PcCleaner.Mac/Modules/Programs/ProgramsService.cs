using PcCleaner.Core;
using PcCleaner.Modules.Processes;

namespace PcCleaner.Modules.Programs;

/// <param name="Failed">Leftovers that could not be moved (with the reason).</param>
public sealed record UninstallResult(bool Ok, long FreedBytes, string Message, IReadOnlyList<string> Failed);

/// <summary>
/// «Программы»: the catalog, an app's leftovers, whether it runs, and removal — the app and the ticked leftovers go to the
/// Trash (an app owned by the system goes through Finder, which asks for the password itself).
/// </summary>
public sealed class ProgramsService
{
    private readonly ITrash _trash;
    private readonly ProcessesService _processes;
    private readonly string _home;
    private readonly Func<FdaState> _fda;

    public ProgramsService(AppCatalog catalog, ITrash trash, ProcessesService processes, string home, Func<FdaState> fda)
    {
        Catalog = catalog;
        _trash = trash;
        _processes = processes;
        _home = home;
        _fda = fda;
    }

    public AppCatalog Catalog { get; }

    public bool HasFullDiskAccess => _fda() == FdaState.Granted;

    public Task<LeftoverScan> LeftoversAsync(InstalledApp app, IReadOnlyList<InstalledApp> allApps, CancellationToken ct)
        => LeftoverFinder.FindAsync(app, _home, allApps, HasFullDiskAccess, ct);

    /// <summary>The user's own processes running from inside the app (its helpers included).</summary>
    public async Task<IReadOnlyList<ProcessEntry>> RunningAsync(InstalledApp app, CancellationToken ct)
    {
        var list = await _processes.ListAsync(ct);
        var prefix = app.Path.TrimEnd('/') + "/";
        return list.Items.Where(e => e.Info.Uid == _processes.OwnUid && e.Info.Executable.StartsWith(prefix, StringComparison.Ordinal))
            .ToList();
    }

    /// <summary>
    /// Stops the app's processes, the main one first (its helpers usually end with it; the rest are stopped after, and
    /// those already gone are skipped). True when none is left.
    /// </summary>
    public async Task<bool> QuitAsync(InstalledApp app, bool force, CancellationToken ct)
    {
        var running = await RunningAsync(app, ct);
        var pids = running.Select(e => e.Info.Pid).ToHashSet();
        foreach (var entry in running.OrderBy(e => pids.Contains(e.Info.Ppid) ? 1 : 0))
            await _processes.TerminateAsync(entry, force, ct);
        return (await RunningAsync(app, ct)).Count == 0;
    }

    /// <param name="appBytes">The app's size when it was measured (for the message), else null.</param>
    public async Task<UninstallResult> UninstallAsync(InstalledApp app, IReadOnlyList<Leftover> leftovers, long? appBytes, CancellationToken ct)
    {
        if (app.IsProtected)
            return new UninstallResult(false, 0, "Встроенные программы macOS и саму эту программу удалить нельзя.", Array.Empty<string>());

        var moved = await _trash.MoveAsync(app.Path, ct);
        if (!moved.Ok)
        {
            return new UninstallResult(false, 0, $"Не удалось переместить «{app.Name}» в Корзину: {moved.Error}. Остатки не тронуты.",
                Array.Empty<string>());
        }
        Log.Action($"Программа «{app.Name}» ({app.Path}) перемещена в Корзину");

        var freed = appBytes ?? 0;
        var count = 0;
        var failed = new List<string>();
        foreach (var leftover in leftovers.Where(l => l.Checked))
        {
            var outcome = await _trash.MoveAsync(leftover.Path, ct);
            if (outcome.Ok)
            {
                freed += leftover.Bytes;
                count++;
                Log.Action($"Остаток «{app.Name}» перемещён в Корзину: {leftover.Path}");
            }
            else
            {
                failed.Add($"{leftover.Path}: {outcome.Error}");
                Log.Warn($"Остаток «{app.Name}» не перемещён: {leftover.Path} — {outcome.Error}");
            }
        }

        var what = count == 0 ? "" : $" вместе с {count} {Format.Plural(count, "остатком", "остатками", "остатками")}";
        var size = freed > 0 ? $" ({Format.Bytes(freed)})" : "";
        var message = $"«{app.Name}» перемещена в Корзину{what}{size}. Вернуть можно из Корзины (Finder → «Вернуть»).";
        if (failed.Count > 0)
            message += $" Не удалось переместить: {failed.Count} — подробности в журнале.";
        return new UninstallResult(true, freed, message, failed);
    }
}
