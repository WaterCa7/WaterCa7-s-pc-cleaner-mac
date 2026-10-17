using PcCleaner.Core;

namespace PcCleaner.Modules.Cleaner;

/// <summary>
/// Finds what each category may delete. Only reads. The rules that keep the user's data safe live here: Apple's own
/// caches, the caches of running programs and browsers, recent files, passwords, history and cookies are never listed.
/// </summary>
public sealed class CleanerScanner
{
    public static readonly TimeSpan CacheMinAge = TimeSpan.FromHours(1);
    public static readonly TimeSpan LogMinAge = TimeSpan.FromDays(7);

    /// <summary>Folder names of browser caches; nothing else inside a browser profile is ever listed.</summary>
    public static readonly IReadOnlySet<string> BrowserCacheFolders = new HashSet<string>(StringComparer.Ordinal)
    {
        "Cache", "Code Cache", "GPUCache", "DawnCache", "DawnGraphiteCache", "DawnWebGPUCache", "ShaderCache", "GrShaderCache",
        "GraphiteDawnCache",
    };

    /// <summary>Folders of ~/Library/Caches that are not app caches of this category (Apple's or other categories').</summary>
    private static readonly HashSet<string> ExcludedCacheFolders = new(StringComparer.OrdinalIgnoreCase)
    {
        // Other categories.
        "Homebrew", "pip", "Yarn", "CocoaPods", "Google", "Microsoft Edge", "BraveSoftware", "com.operasoftware.Opera", "Yandex",
        "Firefox", "Mozilla",
        // macOS services without a com.apple. name ("softwareupdate" may hold a downloaded macOS update).
        "CloudKit", "FamilyCircle", "GeoServices", "PassKit", "SiriTTS", "ColorSync", "Metal", "TemporaryItems", "Animoji",
        "softwareupdate",
        // This program.
        AppInfo.BundleId, "WaterCa7sPcCleaner",
    };

    private const string OwnLogs = "WaterCa7sPcCleaner";

    private readonly string _home;
    private readonly RunningAppSet _running;
    private readonly DateTime _nowUtc;
    private readonly FdaState _fda;

    public CleanerScanner(string home, RunningAppSet running, DateTime nowUtc, FdaState fda)
    {
        _home = home;
        _running = running;
        _nowUtc = nowUtc;
        _fda = fda;
    }

    private string Library => Path.Combine(_home, "Library");

    private string Caches => Path.Combine(Library, "Caches");

    public CategoryScan Scan(CleanerCategory category, CancellationToken ct)
    {
        try
        {
            return category.Id switch
            {
                "caches" => Result(category, AppCacheTargets(ct)),
                "logs" => Result(category, LogTargets(ct)),
                "browsers" => BrowserScan(category, ct),
                "developer" => Result(category, DeveloperTargets(ct)),
                "trash" => TrashScan(category, ct),
                _ => throw new ArgumentException($"Неизвестная категория «{category.Id}»", nameof(category)),
            };
        }
        catch (UnauthorizedAccessException)
        {
            return new CategoryScan(category, Array.Empty<CleanTarget>(), ScanState.NeedsFullDiskAccess,
                "macOS не разрешила прочитать эту папку — нужен полный доступ к диску.");
        }
        catch (IOException ex)
        {
            Log.Warn($"Очистка: «{category.Title}» не проверена: {ErrorText.For(ex)}");
            return new CategoryScan(category, Array.Empty<CleanTarget>(), ScanState.Error, ErrorText.Sentence(ex));
        }
    }

    private static CategoryScan Result(CleanerCategory category, IReadOnlyList<CleanTarget> targets, string? note = null)
        => new(category, targets, targets.Any(t => t.Files.Count > 0) ? ScanState.Ready : ScanState.Empty, note);

    // ----- Кэши программ

    private IReadOnlyList<CleanTarget> AppCacheTargets(CancellationToken ct)
    {
        if (!Directory.Exists(Caches))
            return Array.Empty<CleanTarget>();
        var targets = new List<CleanTarget>();
        foreach (var dir in Children(Caches))
        {
            var name = Path.GetFileName(dir);
            if (name.StartsWith('.') || name.StartsWith("com.apple.", StringComparison.OrdinalIgnoreCase) || ExcludedCacheFolders.Contains(name))
                continue;
            var skip = _running.Matches(name) ? "программа запущена — закройте её, чтобы очистить" : null;
            targets.Add(new CleanTarget(name, dir, Older(dir, CacheMinAge, ct), skip));
        }
        return targets;
    }

    // ----- Журналы

    private IReadOnlyList<CleanTarget> LogTargets(CancellationToken ct)
    {
        var logs = Path.Combine(Library, "Logs");
        if (!Directory.Exists(logs))
            return Array.Empty<CleanTarget>();
        var targets = new List<CleanTarget>
        {
            new("Журналы программ", logs, Older(logs, LogMinAge, ct, rel => rel is OwnLogs or "DiagnosticReports")),
        };
        var reports = Path.Combine(logs, "DiagnosticReports");
        if (Directory.Exists(reports))
            targets.Add(new CleanTarget("Отчёты о сбоях", reports, Older(reports, LogMinAge, ct)));
        return targets;
    }

    // ----- Браузеры

    private sealed record Chromium(string Name, string CachesBase, string SupportBase, string[] Processes);

    private static readonly Chromium[] ChromiumBrowsers =
    {
        new("Google Chrome", "Google/Chrome", "Google/Chrome", new[] { "Google Chrome", "com.google.Chrome" }),
        new("Microsoft Edge", "Microsoft Edge", "Microsoft Edge", new[] { "Microsoft Edge", "com.microsoft.edgemac" }),
        new("Brave", "BraveSoftware/Brave-Browser", "BraveSoftware/Brave-Browser", new[] { "Brave Browser", "com.brave.Browser" }),
        new("Opera", "com.operasoftware.Opera", "com.operasoftware.Opera", new[] { "Opera", "com.operasoftware.Opera" }),
        new("Яндекс Браузер", "Yandex/YandexBrowser", "Yandex/YandexBrowser", new[] { "Yandex", "ru.yandex.desktop.yandex-browser" }),
    };

    private CategoryScan BrowserScan(CleanerCategory category, CancellationToken ct)
    {
        var targets = new List<CleanTarget>();
        foreach (var browser in ChromiumBrowsers)
        {
            var skip = _running.IsRunning(browser.Processes) ? "браузер открыт — закройте его, чтобы очистить" : null;
            foreach (var baseDir in new[] { Path.Combine(Caches, browser.CachesBase), Path.Combine(Library, "Application Support", browser.SupportBase) })
            {
                foreach (var profile in ChromiumProfiles(baseDir))
                {
                    foreach (var cache in Children(profile).Where(d => BrowserCacheFolders.Contains(Path.GetFileName(d))))
                    {
                        var where = profile == baseDir ? Path.GetFileName(cache) : $"{Path.GetFileName(profile)}, {Path.GetFileName(cache)}";
                        targets.Add(new CleanTarget($"{browser.Name} — {where}", cache, Older(cache, CacheMinAge, ct), skip));
                    }
                }
            }
        }

        var firefoxSkip = _running.IsRunning("firefox", "Firefox", "org.mozilla.firefox") ? "браузер открыт — закройте его, чтобы очистить" : null;
        foreach (var profile in Children(Path.Combine(Caches, "Firefox", "Profiles")))
        {
            var cache2 = Path.Combine(profile, "cache2");
            if (Directory.Exists(cache2))
                targets.Add(new CleanTarget($"Firefox — {Path.GetFileName(profile)}", cache2, Older(cache2, CacheMinAge, ct), firefoxSkip));
        }

        string? note = null;
        if (_fda == FdaState.Granted)
        {
            var safariSkip = _running.IsRunning("Safari", "com.apple.Safari") ? "браузер открыт — закройте его, чтобы очистить" : null;
            foreach (var dir in new[]
                     {
                         Path.Combine(Caches, "com.apple.Safari"),
                         Path.Combine(Library, "Containers", "com.apple.Safari", "Data", "Library", "Caches"),
                     })
            {
                if (Directory.Exists(dir))
                    targets.Add(new CleanTarget("Safari", dir, Older(dir, CacheMinAge, ct), safariSkip));
            }
        }
        else
        {
            note = "Кэш Safari проверяется только с полным доступом к диску.";
        }
        return Result(category, targets, note);
    }

    private static IEnumerable<string> ChromiumProfiles(string baseDir)
    {
        if (!Directory.Exists(baseDir))
            yield break;
        yield return baseDir;
        foreach (var dir in Children(baseDir))
        {
            var name = Path.GetFileName(dir);
            if (name is "Default" or "Guest Profile" or "System Profile" || name.StartsWith("Profile ", StringComparison.Ordinal))
                yield return dir;
        }
    }

    // ----- Разработка

    private IReadOnlyList<CleanTarget> DeveloperTargets(CancellationToken ct)
    {
        var developer = Path.Combine(Library, "Developer");
        var xcodeSkip = _running.IsRunning("Xcode", "com.apple.dt.Xcode") ? "Xcode открыт — закройте его, чтобы очистить" : null;
        var simulatorSkip = _running.IsRunning("Simulator", "com.apple.iphonesimulator") ? "Simulator открыт — закройте его, чтобы очистить" : null;
        var cleanable = new (string Title, string Dir, string? Skip)[]
        {
            ("Xcode DerivedData", Path.Combine(developer, "Xcode", "DerivedData"), xcodeSkip),
            ("Кэши симуляторов", Path.Combine(developer, "CoreSimulator", "Caches"), simulatorSkip),
            ("Загрузки Homebrew", Path.Combine(Caches, "Homebrew"), null),
            ("Кэш npm", Path.Combine(_home, ".npm", "_cacache"), null),
            ("Кэш pip", Path.Combine(Caches, "pip"), null),
            ("Кэш Yarn", Path.Combine(Caches, "Yarn"), null),
            ("Кэш CocoaPods", Path.Combine(Caches, "CocoaPods"), null),
        };
        var targets = cleanable.Where(c => Directory.Exists(c.Dir))
            .Select(c => new CleanTarget(c.Title, c.Dir, Older(c.Dir, CacheMinAge, ct), c.Skip))
            .ToList();
        var showOnly = new (string Title, string Dir)[]
        {
            ("Архивы Xcode — нужны для отладки выпущенных версий, удаляйте сами", Path.Combine(developer, "Xcode", "Archives")),
            ("Файлы поддержки устройств iOS — Xcode скачает их заново", Path.Combine(developer, "Xcode", "iOS DeviceSupport")),
        };
        targets.AddRange(showOnly.Where(s => Directory.Exists(s.Dir))
            .Select(s => new CleanTarget(s.Title, s.Dir, FileWalker.Files(s.Dir, ct).ToList(), ShowOnly: true)));
        return targets;
    }

    // ----- Корзина

    private CategoryScan TrashScan(CleanerCategory category, CancellationToken ct)
    {
        var trash = Path.Combine(_home, ".Trash");
        if (!Directory.Exists(trash))
            return new CategoryScan(category, Array.Empty<CleanTarget>(), ScanState.Empty);
        var files = FileWalker.Files(trash, ct).ToList();
        return Result(category, new[] { new CleanTarget("Корзина", trash, files) });
    }

    // ----- helpers

    private IReadOnlyList<FileEntry> Older(string root, TimeSpan minAge, CancellationToken ct, Func<string, bool>? skipDir = null)
        => FileWalker.Files(root, ct, skipDir).Where(f => _nowUtc - f.LastWriteUtc >= minAge).ToList();

    /// <summary>Real folders directly inside <paramref name="dir"/> (no links), or nothing when it is missing.</summary>
    private static IEnumerable<string> Children(string dir)
    {
        if (!Directory.Exists(dir))
            return Array.Empty<string>();
        return new DirectoryInfo(dir).EnumerateDirectories()
            .Where(d => (d.Attributes & FileAttributes.ReparsePoint) == 0)
            .Select(d => d.FullName)
            .OrderBy(d => d, StringComparer.Ordinal)
            .ToList();
    }
}
