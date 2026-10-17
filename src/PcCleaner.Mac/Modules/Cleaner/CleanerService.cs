using PcCleaner.Core;

namespace PcCleaner.Modules.Cleaner;

/// <summary>What a cleaning did. <see cref="Message"/> is shown to the user and is never read as a failure when files went.</summary>
public sealed record CleanResult(long FreedBytes, int Deleted, int Skipped, bool Cancelled)
{
    public string Message
    {
        get
        {
            if (Deleted == 0 && Skipped == 0)
                return Cancelled ? "Очистка остановлена до начала — файлы не удалялись." : "Удалять уже нечего.";
            var text = $"Удалено {Format.Bytes(FreedBytes)} ({Format.Count(Deleted, "файл", "файла", "файлов")}).";
            if (Skipped > 0)
                text += $" Пропущено: {Skipped} — заняты или нет доступа.";
            if (Cancelled)
                text += " Очистка остановлена по вашей просьбе.";
            return text;
        }
    }
}

/// <summary>
/// The cleaning section's work: checks every category (read-only), deletes what the user confirmed, empties the Trash
/// through Finder. Before deleting, the chosen categories are checked again, so a program opened since the last check
/// keeps its cache and files that became recent stay.
/// </summary>
public sealed class CleanerService
{
    private readonly string _home;
    private readonly ICommandRunner _runner;
    private readonly IFileRemover _remover;
    private readonly Func<FdaState> _fda;
    private readonly Func<DateTime> _nowUtc;
    private readonly RunningAppsService _running;

    public CleanerService(string home, ICommandRunner runner, IFileRemover remover, Func<FdaState> fda, Func<DateTime>? nowUtc = null)
    {
        _home = home;
        _runner = runner;
        _remover = remover;
        _fda = fda;
        _nowUtc = nowUtc ?? (() => DateTime.UtcNow);
        _running = new RunningAppsService(runner);
    }

    public async Task<IReadOnlyList<CategoryScan>> ScanAllAsync(CancellationToken ct)
    {
        var scanner = await ScannerAsync(ct);
        return await Task.Run(() => CleanerCatalog.All.Select(c => scanner.Scan(c, ct)).ToList(), ct);
    }

    public async Task<CategoryScan> ScanAsync(CleanerCategory category, CancellationToken ct)
    {
        var scanner = await ScannerAsync(ct);
        return await Task.Run(() => scanner.Scan(category, ct), ct);
    }

    /// <summary>Deletes the regenerable files of <paramref name="categories"/> (the Trash is emptied separately).</summary>
    public async Task<CleanResult> CleanAsync(IReadOnlyCollection<CleanerCategory> categories, IProgress<double>? progress, CancellationToken ct)
    {
        var chosen = categories.Where(c => c.Action == CleanAction.Delete).ToList();
        var scanner = await ScannerAsync(ct);
        return await Task.Run(() =>
        {
            var targets = chosen.SelectMany(c => scanner.Scan(c, ct).Targets).Where(t => t.Cleanable).ToList();
            var total = Math.Max(1, targets.Sum(t => t.Files.Count));
            long freed = 0;
            int deleted = 0, skipped = 0, done = 0;
            var cancelled = false;
            foreach (var target in targets)
            {
                foreach (var file in target.Files)
                {
                    if (ct.IsCancellationRequested)
                    {
                        cancelled = true;
                        break;
                    }
                    switch (_remover.RemoveFile(target.Root, file.Relative))
                    {
                        case RemoveOutcome.Deleted:
                            freed += file.Length;
                            deleted++;
                            break;
                        case RemoveOutcome.Gone:
                            break;
                        default:
                            skipped++;
                            break;
                    }
                    progress?.Report(100.0 * ++done / total);
                }
                RemoveEmptyFolders(target.Root);
                if (cancelled)
                    break;
            }
            var result = new CleanResult(freed, deleted, skipped, cancelled);
            if (deleted > 0 || skipped > 0)
                Log.Action($"Очистка ({string.Join(", ", chosen.Select(c => c.Title))}): {result.Message}");
            return result;
        }, CancellationToken.None);
    }

    /// <summary>«Очистить корзину»: Finder empties every Trash of the user. Returns the text for the user.</summary>
    public async Task<string> EmptyTrashAsync(CancellationToken ct)
    {
        var result = await _runner.RunAsync("/usr/bin/osascript", new[] { "-e", "tell application \"Finder\" to empty the trash" },
            TimeSpan.FromMinutes(10), ct);
        if (result.Ok)
        {
            Log.Action("Корзина очищена (через Finder)");
            return "Корзина очищена.";
        }
        Log.Warn($"Корзина не очищена: {result.StdErr.Trim()} (код {result.ExitCode})");
        if (result.StdErr.Contains("-1743", StringComparison.Ordinal))
            return "Не удалось: macOS не разрешила программе управлять Finder. Разрешите это в Системных настройках → " +
                   "Конфиденциальность и безопасность → Автоматизация и попробуйте ещё раз.";
        if (result.StdErr.Contains("-128", StringComparison.Ordinal))
            return "Отменено — ничего не изменено.";
        return result.TimedOut ? "Не удалось: Finder не ответил вовремя." : "Не удалось очистить Корзину — подробности в журнале.";
    }

    private async Task<CleanerScanner> ScannerAsync(CancellationToken ct)
        => new(_home, await _running.LoadAsync(ct), _nowUtc(), _fda());

    /// <summary>Removes folders that became empty, except the root and the folders directly inside it (programs expect those).</summary>
    private void RemoveEmptyFolders(string root)
    {
        foreach (var folder in FileWalker.Directories(root, CancellationToken.None).Where(d => d.Contains('/')))
            _remover.RemoveEmptyDir(root, folder);
    }
}
