using System.Collections.Concurrent;
using PcCleaner.Core;

namespace PcCleaner.Modules.Programs;

public enum AppLocation
{
    /// <summary>/Applications (and its subfolders such as «Утилиты» or a vendor's folder).</summary>
    Applications,

    /// <summary>~/Applications.</summary>
    UserApplications,

    /// <summary>/System/Applications: built into macOS, read-only.</summary>
    Apple,
}

/// <summary>An installed app with what its bundle says about it.</summary>
/// <param name="Name">What Finder shows: the bundle's file name without ".app".</param>
/// <param name="BundleName">CFBundleName (leftovers may be named after it).</param>
/// <param name="Uninstaller">The app's own uninstaller (inside the bundle or next to it), or null.</param>
/// <param name="PupReason">Why it is a potentially unwanted program, or null.</param>
/// <param name="Problem">Why the bundle could not be read completely (it is still listed), or null.</param>
public sealed record InstalledApp(
    string Path,
    string Name,
    string? BundleName,
    string? BundleId,
    string? Version,
    string? ExecutablePath,
    AppLocation Location,
    bool IsAppStore,
    AppArchitecture Architecture,
    DateTime? LastUsedUtc,
    string? Uninstaller,
    string? PupReason,
    string? Problem)
{
    public SignatureInfo? Signature { get; init; }

    public bool IsApple => Location == AppLocation.Apple;

    /// <summary>Not removable from the app: built into macOS, or this program itself.</summary>
    public bool IsProtected => IsApple || string.Equals(BundleId, AppInfo.BundleId, StringComparison.OrdinalIgnoreCase);

    public bool IsPup => PupReason is not null;

    public string VersionText => Version ?? "—";

    /// <summary>Where it came from: App Store, a developer Apple knows, or nobody vouches for it.</summary>
    public string SourceText => IsApple ? "встроена в macOS"
        : IsAppStore ? "App Store"
        : Signature?.Kind switch
        {
            SignatureKind.DeveloperId => Signature!.Text,
            SignatureKind.Apple => "Apple",
            SignatureKind.AppStore => "App Store",
            null or SignatureKind.Unknown => "подпись не проверялась",
            _ => Signature!.Text,
        };

    public string ArchitectureText => Architecture switch
    {
        AppArchitecture.AppleSilicon => "Apple Silicon",
        AppArchitecture.Universal => "Universal (Apple Silicon и Intel)",
        AppArchitecture.IntelOnly => "только Intel — работает через Rosetta",
        _ => "неизвестно",
    };
}

/// <summary>Where apps are looked for (a test points these at a temporary folder).</summary>
public sealed record AppFolders(string Applications, string UserApplications, IReadOnlyList<string> Apple)
{
    public static AppFolders For(string home) => new(
        "/Applications", Path.Combine(home, "Applications"), new[] { "/System/Applications", "/System/Applications/Utilities" });
}

public sealed record AppList(IReadOnlyList<InstalledApp> Apps, IReadOnlyList<string> Problems);

/// <summary>
/// Installed apps: /Applications/*.app and /Applications/&lt;folder&gt;/*.app (vendor folders, «Утилиты»), the same in
/// ~/Applications, and macOS's own apps on request. Links are not followed (Safari and Xcode aliases are links).
/// </summary>
public sealed class AppCatalog
{
    private readonly AppFolders _folders;
    private readonly ICommandRunner _runner;
    private readonly CodeSignatureService _signatures;

    public AppCatalog(AppFolders folders, ICommandRunner runner, CodeSignatureService signatures)
    {
        _folders = folders;
        _runner = runner;
        _signatures = signatures;
    }

    public async Task<AppList> ListAsync(bool includeApple, CancellationToken ct)
    {
        var bundles = new List<(string Path, AppLocation Location)>();
        bundles.AddRange(FindBundles(_folders.Applications, nested: true).Select(p => (p, AppLocation.Applications)));
        bundles.AddRange(FindBundles(_folders.UserApplications, nested: true).Select(p => (p, AppLocation.UserApplications)));
        if (includeApple)
        {
            foreach (var folder in _folders.Apple)
                bundles.AddRange(FindBundles(folder, nested: false).Select(p => (p, AppLocation.Apple)));
        }

        var apps = new List<InstalledApp>();
        foreach (var (path, location) in bundles)
            apps.Add(await ReadAsync(path, location, ct));

        var problems = new List<string>();
        var lastUsed = await LastUsedAsync(apps.Select(a => a.Path).ToList(), ct);
        if (lastUsed is null && apps.Count > 0)
            problems.Add("Spotlight не сообщил, когда программы запускались последний раз");
        else if (lastUsed is not null)
            apps = apps.Select((a, i) => a with { LastUsedUtc = lastUsed[i] }).ToList();

        var signatures = await SignaturesAsync(apps.Where(a => !a.IsApple).Select(a => a.Path).ToList(), ct);
        apps = apps.Select(a => a.IsApple ? a : a with { Signature = signatures.GetValueOrDefault(a.Path) }).ToList();
        return new AppList(apps, problems);
    }

    /// <summary>The size of the bundle on disk (links are not followed); slow for big apps, so it runs apart.</summary>
    public static Task<long> MeasureAsync(string path, CancellationToken ct) => Task.Run(() => FileWalker.Measure(path, ct).Bytes, ct);

    /// <summary>The .app folders in <paramref name="folder"/> and, when <paramref name="nested"/>, one level of plain subfolders.</summary>
    public static IReadOnlyList<string> FindBundles(string folder, bool nested)
    {
        var result = new List<string>();
        foreach (var dir in SafeDirectories(folder))
        {
            if (IsLink(dir))
                continue;
            if (dir.EndsWith(".app", StringComparison.OrdinalIgnoreCase))
                result.Add(dir);
            else if (nested)
                result.AddRange(SafeDirectories(dir).Where(d => d.EndsWith(".app", StringComparison.OrdinalIgnoreCase) && !IsLink(d)));
        }
        return result;
    }

    /// <summary>An uninstaller inside the bundle (Contents/Resources, Contents/Library, Contents/Helpers) or next to it.</summary>
    public static string? FindUninstaller(string appPath, string applicationsRoot)
    {
        static bool IsUninstaller(string path) => System.IO.Path.GetFileName(path).Contains("uninstall", StringComparison.OrdinalIgnoreCase);

        foreach (var inner in new[] { "Resources", "Library", "Helpers" })
        {
            var found = SafeDirectories(System.IO.Path.Combine(appPath, "Contents", inner))
                .FirstOrDefault(d => d.EndsWith(".app", StringComparison.OrdinalIgnoreCase) && IsUninstaller(d) && !IsLink(d));
            if (found is not null)
                return found;
        }

        var parent = System.IO.Path.GetDirectoryName(appPath);
        if (parent is null)
            return null;
        var name = System.IO.Path.GetFileNameWithoutExtension(appPath);
        // In the Applications folder itself an uninstaller must carry the app's name; in a vendor folder any will do.
        var inRoot = string.Equals(System.IO.Path.TrimEndingDirectorySeparator(parent), System.IO.Path.TrimEndingDirectorySeparator(applicationsRoot),
            StringComparison.Ordinal);
        return SafeDirectories(parent).FirstOrDefault(d =>
            d != appPath && d.EndsWith(".app", StringComparison.OrdinalIgnoreCase) && IsUninstaller(d) && !IsLink(d) &&
            (!inRoot || System.IO.Path.GetFileNameWithoutExtension(d).Contains(name, StringComparison.OrdinalIgnoreCase)));
    }

    private async Task<InstalledApp> ReadAsync(string path, AppLocation location, CancellationToken ct)
    {
        var name = System.IO.Path.GetFileNameWithoutExtension(path);
        var plist = await PlistFile.ReadAsync(System.IO.Path.Combine(path, "Contents", "Info.plist"), _runner, ct);
        var info = plist.Value;
        var bundleId = info?.GetString("CFBundleIdentifier");
        var bundleName = info?.GetString("CFBundleName");
        var executableName = info?.GetString("CFBundleExecutable");
        var executable = executableName is null ? null : System.IO.Path.Combine(path, "Contents", "MacOS", executableName);
        var version = info?.GetString("CFBundleShortVersionString") ?? info?.GetString("CFBundleVersion");
        var architecture = executable is null ? AppArchitecture.Unknown : MachO.Architecture(MachO.ReadFile(executable));
        var appStore = Directory.Exists(System.IO.Path.Combine(path, "Contents", "_MASReceipt"));
        var root = location == AppLocation.UserApplications ? _folders.UserApplications : _folders.Applications;
        var uninstaller = location == AppLocation.Apple ? null : FindUninstaller(path, root);
        var pup = location == AppLocation.Apple ? null : MacPups.Match(name, bundleName, bundleId);
        var problem = plist.Problem is not null ? "Info.plist не читается" : null;
        return new InstalledApp(path, name, bundleName, bundleId, version, executable, location, appStore, architecture, null, uninstaller, pup, problem);
    }

    private async Task<IReadOnlyList<DateTime?>?> LastUsedAsync(IReadOnlyList<string> paths, CancellationToken ct)
    {
        if (paths.Count == 0)
            return Array.Empty<DateTime?>();
        var result = await _runner.RunAsync("/usr/bin/mdls", new[] { "-name", "kMDItemLastUsedDate", "-raw" }.Concat(paths).ToList(),
            TimeSpan.FromSeconds(60), ct);
        return result.Ok ? AppParsers.LastUsed(result.StdOut, paths.Count) : null;
    }

    private async Task<IReadOnlyDictionary<string, SignatureInfo>> SignaturesAsync(IReadOnlyList<string> paths, CancellationToken ct)
    {
        var result = new ConcurrentDictionary<string, SignatureInfo>(StringComparer.Ordinal);
        using var gate = new SemaphoreSlim(4);
        await Task.WhenAll(paths.Select(async path =>
        {
            await gate.WaitAsync(ct);
            try
            {
                result[path] = await _signatures.GetAsync(path, ct, verify: false);
            }
            finally
            {
                gate.Release();
            }
        }));
        return result;
    }

    private static IEnumerable<string> SafeDirectories(string folder)
    {
        try
        {
            return Directory.Exists(folder) ? Directory.EnumerateDirectories(folder).Order(StringComparer.OrdinalIgnoreCase).ToList() : Array.Empty<string>();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Array.Empty<string>();
        }
    }

    private static bool IsLink(string path)
    {
        try
        {
            return new DirectoryInfo(path).Attributes.HasFlag(FileAttributes.ReparsePoint);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return true;
        }
    }
}
