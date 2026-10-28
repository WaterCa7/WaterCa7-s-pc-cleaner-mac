using PcCleaner.Core;
using PcCleaner.Modules.Dashboard;

namespace PcCleaner.Modules.Cleaner;

/// <summary>Findings of «Очистка» and «Диски» for the dashboard (pure rules).</summary>
public static class CleanerRules
{
    public const long MediumCacheBytes = 500L << 20;
    public const long LowCacheBytes = 100L << 20;
    public const long DeveloperBytes = 2L << 30;
    public const long TrashBytes = 1L << 30;

    /// <param name="scans">The read-only check of every category.</param>
    /// <param name="cleanRegenerable">The fix of the cache finding: cleans <see cref="CleanerCatalog.Regenerable"/>.</param>
    public static IReadOnlyList<HealthIssue> Evaluate(IReadOnlyList<CategoryScan> scans, Func<CancellationToken, Task<string>> cleanRegenerable)
    {
        var issues = new List<HealthIssue>();
        var regenerable = scans.Where(s => CleanerCatalog.Regenerable.Contains(s.Category)).Sum(s => s.Bytes);
        if (regenerable >= LowCacheBytes)
        {
            issues.Add(new HealthIssue
            {
                Module = DiskSpaceRules.ModuleName,
                Title = $"Кэши и старые журналы занимают {Format.Bytes(regenerable)}",
                Details = "Временные файлы программ и браузеров и журналы старше недели — программы создадут их заново. " +
                          "Пароли, история браузеров и ваши файлы не затрагиваются.",
                Severity = regenerable >= MediumCacheBytes ? IssueSeverity.Medium : IssueSeverity.Low,
                ReclaimableBytes = regenerable,
                FixAsync = cleanRegenerable,
                AutoFixSafe = true,
                TargetPageKey = "Cleaner",
            });
        }
        if (Bytes(scans, CleanerCatalog.Developer) is var developer and >= DeveloperBytes)
        {
            issues.Add(new HealthIssue
            {
                Module = DiskSpaceRules.ModuleName,
                Title = $"Кэши разработки занимают {Format.Bytes(developer)}",
                Details = "Xcode DerivedData, кэши симуляторов, Homebrew, npm и других инструментов. Их можно очистить в разделе " +
                          "«Очистка»; следующая сборка проекта займёт больше времени.",
                Severity = IssueSeverity.Low,
                ReclaimableBytes = developer,
                TargetPageKey = "Cleaner",
            });
        }
        if (Bytes(scans, CleanerCatalog.Trash) is var trash and >= TrashBytes)
        {
            issues.Add(new HealthIssue
            {
                Module = DiskSpaceRules.ModuleName,
                Title = $"В Корзине {Format.Bytes(trash)}",
                Details = "Удалённые файлы по-прежнему занимают место, пока Корзина не очищена. Очистить её можно в разделе " +
                          "«Очистка» — это безвозвратно.",
                Severity = IssueSeverity.Low,
                ReclaimableBytes = trash,
                TargetPageKey = "Cleaner",
            });
        }
        return issues;
    }

    private static long Bytes(IReadOnlyList<CategoryScan> scans, CleanerCategory category)
        => scans.FirstOrDefault(s => s.Category == category)?.Bytes ?? 0;
}

/// <summary>The dashboard check of «Очистка» and «Диски»: free disk space and what the cleaning would free.</summary>
public sealed class CleanerContributor : IHealthContributor
{
    private readonly CleanerService _cleaner;
    private readonly MacInfoService _info;

    public CleanerContributor(CleanerService cleaner, MacInfoService info)
    {
        _cleaner = cleaner;
        _info = info;
    }

    public string ModuleName => DiskSpaceRules.ModuleName;

    public async Task<IReadOnlyList<HealthIssue>> AnalyzeAsync(CancellationToken ct)
    {
        var issues = new List<HealthIssue>();
        if (await _info.DiskAsync(ct) is { } disk)
        {
            if (DiskSpaceRules.Evaluate(disk) is { } space)
                issues.Add(space);
        }
        else
        {
            Log.Warn("Главная: не удалось узнать свободное место на диске");
        }
        var scans = await _cleaner.ScanAllAsync(ct);
        issues.AddRange(CleanerRules.Evaluate(scans,
            async fixCt => (await _cleaner.CleanAsync(CleanerCatalog.Regenerable, null, fixCt)).Message));
        return issues;
    }
}
