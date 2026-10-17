using PcCleaner.Core;

namespace PcCleaner.Modules.Dashboard;

/// <summary>Facts about this Mac for the dashboard card (and later the report).</summary>
public sealed record MacInfo
{
    private const string NoData = "нет данных";

    public string? ComputerName { get; init; }

    public HardwareInfo? Hardware { get; init; }

    public string? OsVersion { get; init; }

    public string? OsBuild { get; init; }

    public DateTimeOffset? BootTime { get; init; }

    public DiskUsage? Disk { get; init; }

    /// <summary>Label/value pairs in display order.</summary>
    public IReadOnlyList<(string Label, string Value)> Rows(DateTimeOffset now)
    {
        var model = (Hardware?.ModelName, Hardware?.ModelId) switch
        {
            ({ } name, { } id) => $"{name} ({id})",
            ({ } name, null) => name,
            (null, { } id) => id,
            _ => NoData,
        };
        var os = OsVersion is null ? NoData : MacOsNames.Marketing(OsVersion) + (OsBuild is null ? "" : $" ({OsBuild})");
        var disk = Disk is { } d
            ? $"свободно {Format.Bytes(d.FreeBytes)} из {Format.Bytes(d.TotalBytes)} ({Format.Percent(Math.Round(d.FreePercent, 1))})"
            : NoData;
        var uptime = BootTime is { } boot && now > boot ? Format.Duration(now - boot) : NoData;
        return new[]
        {
            ("Имя Mac", ComputerName ?? NoData),
            ("Модель", model),
            ("Чип", Hardware?.Chip ?? NoData),
            ("Память", Hardware is { MemoryBytes: > 0 } hw ? Format.Bytes(hw.MemoryBytes) : NoData),
            ("macOS", os),
            ("Диск", disk),
            ("Без перезагрузки", uptime),
        };
    }
}
