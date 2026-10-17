using PcCleaner.Core;

namespace PcCleaner.Modules.Dashboard;

/// <summary>Free space on the data volume: macOS needs room for updates, swap and Time Machine snapshots.</summary>
public static class DiskSpaceRules
{
    public const string ModuleName = "Очистка и диски";

    /// <summary>Less than this is always «важно», whatever the size of the disk.</summary>
    public const long LowFreeBytes = 10L << 30;

    public static HealthIssue? Evaluate(DiskUsage disk)
    {
        var percent = disk.FreePercent;
        var details = $"Свободно {Format.Bytes(disk.FreeBytes)} из {Format.Bytes(disk.TotalBytes)} " +
                      $"({Format.Percent(Math.Round(percent, 1))}). macOS нужно место для обновлений, памяти и снимков Time Machine; " +
                      "когда его мало, Mac работает медленнее, а обновления не ставятся.";
        if (disk.FreeBytes < LowFreeBytes || percent < 10)
        {
            return new HealthIssue
            {
                Module = ModuleName,
                Title = "Мало свободного места на диске",
                Details = details,
                Severity = IssueSeverity.High,
                TargetPageKey = "Cleaner",
            };
        }
        if (percent < 20)
        {
            return new HealthIssue
            {
                Module = ModuleName,
                Title = "Свободного места на диске становится мало",
                Details = details,
                Severity = IssueSeverity.Medium,
                TargetPageKey = "Cleaner",
            };
        }
        return null;
    }
}

/// <summary>The dashboard check of free disk space (the cleaning section adds its own checks in stage 2).</summary>
public sealed class DiskSpaceContributor : IHealthContributor
{
    private readonly MacInfoService _info;

    public DiskSpaceContributor(MacInfoService info) => _info = info;

    public string ModuleName => DiskSpaceRules.ModuleName;

    public async Task<IReadOnlyList<HealthIssue>> AnalyzeAsync(CancellationToken ct)
    {
        var disk = await _info.DiskAsync(ct) ?? throw new InvalidOperationException("не удалось узнать свободное место на диске");
        return DiskSpaceRules.Evaluate(disk) is { } issue ? new[] { issue } : Array.Empty<HealthIssue>();
    }
}
