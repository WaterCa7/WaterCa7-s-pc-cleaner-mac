using PcCleaner.Core;

namespace PcCleaner.Modules.Programs;

/// <summary>Findings of «Программы» for the dashboard (pure rules).</summary>
public static class ProgramsRules
{
    public const string ModuleName = "Программы";
    public const string PageKey = "Programs";
    public static readonly TimeSpan UnusedAge = TimeSpan.FromDays(180);
    public const long UnusedBytes = 500L << 20;

    /// <summary>Apps worth measuring for the "not opened for long" tip: a known last-use date older than half a year.</summary>
    public static bool IsLongUnused(InstalledApp app, DateTime nowUtc)
        => !app.IsProtected && app.LastUsedUtc is { } used && nowUtc - used > UnusedAge;

    /// <param name="sizes">Measured sizes by app path (only the long-unused ones need one).</param>
    public static IReadOnlyList<HealthIssue> Evaluate(IReadOnlyList<InstalledApp> apps, IReadOnlyDictionary<string, long> sizes, DateTime nowUtc)
    {
        var issues = new List<HealthIssue>();
        var own = apps.Where(a => !a.IsProtected).ToList();

        foreach (var pup in own.Where(a => a.IsPup))
        {
            issues.Add(new HealthIssue
            {
                Module = ModuleName,
                Title = $"Потенциально нежелательная программа «{pup.Name}»",
                Details = $"{pup.PupReason}. Это не вирус, но пользы от неё обычно нет. Удалить её вместе с остатками можно в разделе " +
                          "«Программы» — всё уйдёт в Корзину.",
                Severity = IssueSeverity.High,
                TargetPageKey = PageKey,
            });
        }

        var unused = own.Where(a => IsLongUnused(a, nowUtc) && sizes.GetValueOrDefault(a.Path) >= UnusedBytes)
            .OrderByDescending(a => sizes[a.Path]).ToList();
        if (unused.Count > 0)
        {
            var total = unused.Sum(a => sizes[a.Path]);
            issues.Add(new HealthIssue
            {
                Module = ModuleName,
                Title = $"Давно не запускались: {unused.Count} {Format.Plural(unused.Count, "программа", "программы", "программ")} ({Format.Bytes(total)})",
                Details = "Не открывались больше полугода и занимают много места: " +
                          string.Join(", ", unused.Select(a => $"{a.Name} — {Format.Bytes(sizes[a.Path])}")) +
                          ". Если они не нужны, их можно удалить в разделе «Программы».",
                Severity = IssueSeverity.Info,
                ReclaimableBytes = total,
                TargetPageKey = PageKey,
            });
        }

        var intel = own.Where(a => a.Architecture == AppArchitecture.IntelOnly).OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        if (intel.Count > 0)
        {
            issues.Add(new HealthIssue
            {
                Module = ModuleName,
                Title = $"Только для Intel: {intel.Count} {Format.Plural(intel.Count, "программа", "программы", "программ")}",
                Details = $"{string.Join(", ", intel.Select(a => a.Name))} — работают через Rosetta: медленнее и тратят больше заряда, а Apple " +
                          "постепенно сворачивает Rosetta. Поищите у разработчика версию для Apple Silicon.",
                Severity = IssueSeverity.Info,
                TargetPageKey = PageKey,
            });
        }
        return issues;
    }
}

/// <summary>The dashboard check of «Программы»: unwanted, long-unused and Intel-only apps (read-only).</summary>
public sealed class ProgramsContributor : IHealthContributor
{
    private readonly ProgramsService _programs;
    private readonly Func<DateTime> _nowUtc;

    public ProgramsContributor(ProgramsService programs, Func<DateTime>? nowUtc = null)
    {
        _programs = programs;
        _nowUtc = nowUtc ?? (() => DateTime.UtcNow);
    }

    public string ModuleName => ProgramsRules.ModuleName;

    public async Task<IReadOnlyList<HealthIssue>> AnalyzeAsync(CancellationToken ct)
    {
        var list = await _programs.Catalog.ListAsync(includeApple: false, ct);
        var now = _nowUtc();
        var sizes = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var app in list.Apps.Where(a => ProgramsRules.IsLongUnused(a, now)))
            sizes[app.Path] = await AppCatalog.MeasureAsync(app.Path, ct);
        return ProgramsRules.Evaluate(list.Apps, sizes, now);
    }
}
