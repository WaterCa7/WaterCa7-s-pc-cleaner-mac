using PcCleaner.Core;

namespace PcCleaner.Modules.Cleaner;

/// <summary>A running program: its bundle and the names its caches may be called by.</summary>
public sealed record RunningApp(string BundlePath, string? BundleId, string Name, string? Executable);

/// <summary>The programs of the current user that are running now.</summary>
public sealed class RunningAppSet
{
    public static readonly RunningAppSet Empty = new(Array.Empty<RunningApp>());

    private readonly HashSet<string> _names = new(StringComparer.OrdinalIgnoreCase);

    public RunningAppSet(IEnumerable<RunningApp> apps)
    {
        Apps = apps.ToList();
        foreach (var app in Apps)
        {
            foreach (var name in new[] { app.BundleId, app.Name, app.Executable })
            {
                if (!string.IsNullOrWhiteSpace(name))
                    _names.Add(name);
            }
        }
    }

    public IReadOnlyList<RunningApp> Apps { get; }

    /// <summary>True when a cache folder named <paramref name="folderName"/> belongs to a running program.</summary>
    public bool Matches(string folderName) => _names.Contains(folderName);

    /// <summary>True when a program with this bundle id, name or executable runs ("Google Chrome", "com.apple.Safari").</summary>
    public bool IsRunning(params string[] names) => names.Any(_names.Contains);
}

/// <summary>Reads which programs run: `ps` gives the executables, their bundles' Info.plist the ids and names.</summary>
public sealed class RunningAppsService
{
    private readonly ICommandRunner _runner;

    public RunningAppsService(ICommandRunner runner) => _runner = runner;

    /// <summary>The outermost .app of every "…/X.app/Contents/MacOS/…" path (helpers inside an app count as that app).</summary>
    public static IReadOnlyList<string> BundlesFromPs(string psComm)
    {
        var bundles = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var raw in psComm.Split('\n'))
        {
            var line = raw.Trim();
            if (!Path.IsPathRooted(line))
                continue;
            var end = line.IndexOf(".app/", StringComparison.Ordinal);
            if (end < 0)
                continue;
            var bundle = line[..(end + 4)];
            if (seen.Add(bundle))
                bundles.Add(bundle);
        }
        return bundles;
    }

    public async Task<RunningAppSet> LoadAsync(CancellationToken ct)
    {
        var ps = await _runner.RunAsync("/bin/ps", new[] { "-xww", "-o", "comm=" }, ct: ct);
        if (!ps.Ok)
        {
            Log.Warn($"Не удалось узнать, какие программы запущены (ps: код {ps.ExitCode}) — кэши проверяются без этого");
            return RunningAppSet.Empty;
        }
        var apps = new List<RunningApp>();
        foreach (var bundle in BundlesFromPs(ps.StdOut))
        {
            var name = Path.GetFileNameWithoutExtension(bundle);
            var plist = await PlistFile.ReadAsync(Path.Combine(bundle, "Contents", "Info.plist"), _runner, ct);
            var info = plist.Value;
            apps.Add(new RunningApp(bundle, info?.GetString("CFBundleIdentifier"), info?.GetString("CFBundleName") ?? name,
                info?.GetString("CFBundleExecutable")));
            if (info?.GetString("CFBundleName") is { } bundleName && !string.Equals(bundleName, name, StringComparison.OrdinalIgnoreCase))
                apps.Add(new RunningApp(bundle, null, name, null));
        }
        return new RunningAppSet(apps);
    }
}
