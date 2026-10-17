using PcCleaner.Core;

namespace PcCleaner.Modules.Dashboard;

/// <summary>Reads the facts about this Mac with read-only tools; a tool that fails leaves its fields empty.</summary>
public sealed class MacInfoService
{
    private static readonly string[] NoArgs = Array.Empty<string>();

    private readonly ICommandRunner _runner;

    public MacInfoService(ICommandRunner runner) => _runner = runner;

    public async Task<MacInfo> LoadAsync(CancellationToken ct)
    {
        var name = RunAsync("/usr/sbin/scutil", new[] { "--get", "ComputerName" }, ct);
        var swVers = RunAsync("/usr/bin/sw_vers", NoArgs, ct);
        var boot = RunAsync("/usr/sbin/sysctl", new[] { "-n", "kern.boottime" }, ct);
        var hardware = RunAsync("/usr/sbin/system_profiler", new[] { "SPHardwareDataType", "-json" }, ct, TimeSpan.FromSeconds(30));
        var disk = DiskAsync(ct);

        var (_, version, build) = MacInfoParsers.ParseSwVers(await swVers ?? "");
        return new MacInfo
        {
            ComputerName = (await name)?.Trim() is { Length: > 0 } n ? n : null,
            Hardware = await hardware is { } json ? MacInfoParsers.ParseHardware(json) : null,
            OsVersion = version,
            OsBuild = build,
            BootTime = await boot is { } b ? MacInfoParsers.ParseBootTime(b) : null,
            Disk = await disk,
        };
    }

    /// <summary>The data volume, where the user's files live; the root volume on systems without one.</summary>
    public async Task<DiskUsage?> DiskAsync(CancellationToken ct)
    {
        foreach (var path in new[] { "/System/Volumes/Data", "/" })
        {
            if (await RunAsync("/bin/df", new[] { "-k", "-P", path }, ct) is { } text && MacInfoParsers.ParseDf(text) is { } usage)
                return usage;
        }
        return null;
    }

    private async Task<string?> RunAsync(string exe, string[] args, CancellationToken ct, TimeSpan? timeout = null)
    {
        var result = await _runner.RunAsync(exe, args, timeout, ct);
        if (result.Ok)
            return result.StdOut;
        Log.Info($"Сведения о Mac: {exe} {string.Join(' ', args)} — {(result.TimedOut ? "не ответил вовремя" : $"код {result.ExitCode}")}");
        return null;
    }
}
