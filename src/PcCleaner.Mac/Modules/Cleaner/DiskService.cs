using System.Globalization;
using System.Text.RegularExpressions;
using PcCleaner.Core;

namespace PcCleaner.Modules.Cleaner;

/// <summary>One mounted volume as `df -k -P` reports it.</summary>
public sealed record VolumeUsage(string Device, string Mount, long TotalBytes, long UsedBytes, long FreeBytes);

/// <summary>A volume the user cares about; <see cref="AvailableBytes"/> includes what macOS can purge by itself.</summary>
public sealed record VolumeInfo(string Name, string Mount, long TotalBytes, long FreeBytes, long? AvailableBytes)
{
    public double UsedPercent => TotalBytes > 0 ? 100.0 * Math.Clamp(TotalBytes - FreeBytes, 0, TotalBytes) / TotalBytes : 0;

    /// <summary>Space macOS frees by itself when needed (caches, local snapshots); 0 when unknown.</summary>
    public long PurgeableBytes => AvailableBytes is { } available && available > FreeBytes ? available - FreeBytes : 0;
}

public sealed record DisksInfo(IReadOnlyList<VolumeInfo> Volumes, IReadOnlyList<DateTime> Snapshots, bool SnapshotsKnown);

public static class DiskParsers
{
    public const string DataVolume = "/System/Volumes/Data";

    private static readonly Regex Snapshot = new(@"com\.apple\.TimeMachine\.(\d{4}-\d{2}-\d{2}-\d{6})\.local", RegexOptions.CultureInvariant);

    /// <summary>Every real volume (device /dev/…) of `df -k -P`; mount points may contain spaces.</summary>
    public static IReadOnlyList<VolumeUsage> ParseVolumes(string df)
    {
        var volumes = new List<VolumeUsage>();
        foreach (var raw in df.Split('\n'))
        {
            var parts = raw.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 6 || !parts[0].StartsWith("/dev/", StringComparison.Ordinal) ||
                !long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var total) ||
                !long.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var used) ||
                !long.TryParse(parts[3], NumberStyles.None, CultureInfo.InvariantCulture, out var free) || total <= 0)
                continue;
            volumes.Add(new VolumeUsage(parts[0], string.Join(' ', parts[5..]), total * 1024, used * 1024, free * 1024));
        }
        return volumes;
    }

    /// <summary>The data volume of the startup disk (or "/" on systems without one) and external volumes in /Volumes.</summary>
    public static IReadOnlyList<VolumeUsage> UserVolumes(IReadOnlyList<VolumeUsage> all)
    {
        var system = all.FirstOrDefault(v => v.Mount == DataVolume) ?? all.FirstOrDefault(v => v.Mount == "/");
        var result = new List<VolumeUsage>();
        if (system is not null)
            result.Add(system);
        result.AddRange(all.Where(v => v.Mount.StartsWith("/Volumes/", StringComparison.Ordinal)).OrderBy(v => v.Mount, StringComparer.Ordinal));
        return result;
    }

    /// <summary>Times of the local Time Machine snapshots in `tmutil listlocalsnapshots /`.</summary>
    public static IReadOnlyList<DateTime> ParseSnapshots(string text)
        => Snapshot.Matches(text)
            .Select(m => DateTime.TryParseExact(m.Groups[1].Value, "yyyy-MM-dd-HHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var t)
                ? t
                : (DateTime?)null)
            .OfType<DateTime>()
            .OrderBy(t => t)
            .ToList();
}

/// <summary>Volumes, local Time Machine snapshots and purgeable space (read-only).</summary>
public sealed class DiskService
{
    private readonly ICommandRunner _runner;
    private readonly Func<string, long?> _availableForImportantUsage;

    public DiskService(ICommandRunner runner, Func<string, long?>? availableForImportantUsage = null)
    {
        _runner = runner;
        _availableForImportantUsage = availableForImportantUsage ?? MacNative.AvailableForImportantUsage;
    }

    public async Task<DisksInfo> LoadAsync(CancellationToken ct)
    {
        var df = await _runner.RunAsync("/bin/df", new[] { "-k", "-P" }, ct: ct);
        var volumes = df.Ok ? DiskParsers.UserVolumes(DiskParsers.ParseVolumes(df.StdOut)) : Array.Empty<VolumeUsage>();
        if (!df.Ok)
            Log.Warn($"Диски: df не ответил (код {df.ExitCode})");

        var startupName = "Macintosh HD";
        var diskutil = await _runner.RunAsync("/usr/sbin/diskutil", new[] { "info", "-plist", "/" }, ct: ct);
        if (diskutil.Ok)
        {
            try
            {
                if (Plist.ParseDict(diskutil.StdOut).GetString("VolumeName") is { Length: > 0 } name)
                    startupName = name;
            }
            catch (PlistException)
            {
                // The default name will do.
            }
        }

        var infos = volumes.Select(v => new VolumeInfo(
                v.Mount is DiskParsers.DataVolume or "/" ? startupName : Path.GetFileName(v.Mount),
                v.Mount, v.TotalBytes, v.FreeBytes, _availableForImportantUsage(v.Mount)))
            .ToList();

        var tmutil = await _runner.RunAsync("/usr/bin/tmutil", new[] { "listlocalsnapshots", "/" }, ct: ct);
        return new DisksInfo(infos, tmutil.Ok ? DiskParsers.ParseSnapshots(tmutil.StdOut) : Array.Empty<DateTime>(), tmutil.Ok);
    }
}
