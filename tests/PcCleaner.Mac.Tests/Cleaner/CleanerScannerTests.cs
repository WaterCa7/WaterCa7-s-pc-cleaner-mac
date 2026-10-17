using PcCleaner.Core;
using PcCleaner.Mac.Tests.Support;
using PcCleaner.Modules.Cleaner;
using Xunit;

namespace PcCleaner.Mac.Tests.Cleaner;

/// <summary>A fake home folder with the layout of a real Mac; files are dated relative to <see cref="Now"/>.</summary>
public sealed class FakeHome : IDisposable
{
    public static readonly DateTime Now = new(2026, 10, 17, 12, 0, 0, DateTimeKind.Utc);

    private readonly TempDir _dir = new();

    public string Path => _dir.Path;

    /// <summary>Creates a file of <paramref name="bytes"/> bytes last written <paramref name="age"/> before <see cref="Now"/>.</summary>
    public string Add(string relative, int bytes, TimeSpan age)
    {
        var file = _dir.File(relative, new string('x', bytes));
        File.SetLastWriteTimeUtc(file, Now - age);
        return file;
    }

    public string Old(string relative, int bytes = 100) => Add(relative, bytes, TimeSpan.FromDays(30));

    /// <summary>A big file without writing its bytes (sparse where the file system supports it).</summary>
    public string AddSized(string relative, long bytes, TimeSpan age)
    {
        var file = System.IO.Path.Combine(Path, relative);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(file)!);
        using (var stream = new FileStream(file, FileMode.Create, FileAccess.Write))
            stream.SetLength(bytes);
        File.SetLastWriteTimeUtc(file, Now - age);
        return file;
    }

    public void Dispose() => _dir.Dispose();
}

public class CleanerScannerTests
{
    private static readonly TimeSpan Fresh = TimeSpan.FromMinutes(5);

    private static CategoryScan Scan(FakeHome home, CleanerCategory category, RunningAppSet? running = null, FdaState fda = FdaState.Denied)
        => new CleanerScanner(home.Path, running ?? RunningAppSet.Empty, FakeHome.Now, fda).Scan(category, CancellationToken.None);

    private static RunningAppSet Running(params string[] names)
        => new(names.Select(n => new RunningApp("/Applications/" + n + ".app", null, n, null)));

    [Fact]
    public void AppCaches_SkipAppleOtherCategoriesAndFreshFiles()
    {
        using var home = new FakeHome();
        home.Old("Library/Caches/com.spotify.client/Data/a.bin", 1000);
        home.Add("Library/Caches/com.spotify.client/Data/new.bin", 500, Fresh);
        home.Old("Library/Caches/com.apple.Music/x.bin");
        home.Old("Library/Caches/Homebrew/downloads/y.tar.gz");
        home.Old("Library/Caches/Google/Chrome/Default/Cache/z");
        home.Old("Library/Caches/CloudKit/db");
        var scan = Scan(home, CleanerCatalog.AppCaches);
        var target = Assert.Single(scan.Targets);
        Assert.Equal("com.spotify.client", target.Title);
        Assert.Equal(new[] { "Data/a.bin" }, target.Files.Select(f => f.Relative));
        Assert.Equal(1000, scan.Bytes);
        Assert.Equal(ScanState.Ready, scan.State);
    }

    [Fact]
    public void AppCaches_RunningProgram_IsSkippedWithAReason()
    {
        using var home = new FakeHome();
        home.Old("Library/Caches/com.spotify.client/a.bin", 1000);
        var scan = Scan(home, CleanerCatalog.AppCaches, Running("com.spotify.client"));
        Assert.Equal(0, scan.Bytes);
        Assert.Equal(1000, scan.SkippedBytes);
        Assert.Contains("программа запущена", Assert.Single(scan.SkipNotes));
    }

    [Fact]
    public void Logs_OlderThanAWeek_WithoutOwnLogs()
    {
        using var home = new FakeHome();
        home.Old("Library/Logs/JetBrains/idea.log", 300);
        home.Add("Library/Logs/JetBrains/today.log", 300, TimeSpan.FromDays(2));
        home.Old("Library/Logs/WaterCa7sPcCleaner/2026-09-01.log");
        home.Old("Library/Logs/DiagnosticReports/App-2026-09-01.ips", 200);
        var scan = Scan(home, CleanerCatalog.Logs);
        Assert.Equal(new[] { "Журналы программ", "Отчёты о сбоях" }, scan.Targets.Select(t => t.Title));
        Assert.Equal(new[] { "JetBrains/idea.log" }, scan.Targets[0].Files.Select(f => f.Relative));
        Assert.Equal(500, scan.Bytes);
    }

    [Fact]
    public void Browsers_OnlyCacheFolders_NeverPasswordsOrHistory()
    {
        using var home = new FakeHome();
        const string chrome = "Library/Application Support/Google/Chrome";
        home.Old($"{chrome}/Default/Code Cache/js/a", 100);
        home.Old($"{chrome}/Default/GPUCache/data_0", 100);
        home.Old($"{chrome}/Profile 1/Code Cache/js/b", 100);
        home.Old($"{chrome}/GrShaderCache/data_1", 100);
        foreach (var secret in new[] { "Login Data", "History", "Cookies", "Bookmarks", "Web Data", "Preferences" })
            home.Old($"{chrome}/Default/{secret}", 5000);
        home.Old($"{chrome}/Default/Local Storage/leveldb/000003.log", 5000);
        home.Old("Library/Caches/Google/Chrome/Default/Cache/Cache_Data/f_0001", 100);
        home.Old("Library/Caches/Firefox/Profiles/abc.default-release/cache2/entries/E1", 100);
        home.Old("Library/Application Support/Firefox/Profiles/abc.default-release/places.sqlite", 5000);
        var scan = Scan(home, CleanerCatalog.Browsers);
        Assert.Equal(600, scan.Bytes);
        var all = scan.Targets.SelectMany(t => t.Files).Select(f => f.FullPath).ToList();
        Assert.DoesNotContain(all, f => f.Contains("Login Data") || f.Contains("History") || f.Contains("Cookies") ||
                                        f.Contains("Bookmarks") || f.Contains("Local Storage") || f.Contains("places.sqlite"));
        Assert.Contains(scan.Targets, t => t.Title == "Google Chrome — Profile 1, Code Cache");
        Assert.Contains(scan.Targets, t => t.Title == "Firefox — abc.default-release");
        Assert.Equal("Кэш Safari проверяется только с полным доступом к диску.", scan.Note);
    }

    [Fact]
    public void Browsers_OpenBrowser_IsSkipped()
    {
        using var home = new FakeHome();
        home.Old("Library/Caches/Google/Chrome/Default/Cache/x", 100);
        var scan = Scan(home, CleanerCatalog.Browsers, Running("Google Chrome"));
        Assert.Equal(0, scan.Bytes);
        Assert.Contains("браузер открыт", Assert.Single(scan.SkipNotes));
    }

    [Fact]
    public void Browsers_SafariWithFullDiskAccess()
    {
        using var home = new FakeHome();
        home.Old("Library/Caches/com.apple.Safari/WebKitCache/x", 100);
        home.Old("Library/Containers/com.apple.Safari/Data/Library/Caches/y", 100);
        var scan = Scan(home, CleanerCatalog.Browsers, fda: FdaState.Granted);
        Assert.Equal(200, scan.Bytes);
        Assert.Null(scan.Note);
    }

    [Fact]
    public void Developer_ShowOnlyIsNotCounted_XcodeOpenIsSkipped()
    {
        using var home = new FakeHome();
        home.Old("Library/Developer/Xcode/DerivedData/App-abc/Build/x.o", 1000);
        home.Old("Library/Caches/Homebrew/downloads/pkg.tar.gz", 300);
        home.Old(".npm/_cacache/content-v2/x", 200);
        home.Old("Library/Developer/Xcode/Archives/2026-10-01/App.xcarchive/Info.plist", 9000);
        var scan = Scan(home, CleanerCatalog.Developer, Running("Xcode"));
        Assert.Equal(500, scan.Bytes);
        Assert.Equal(9000, scan.ShowOnlyBytes);
        Assert.Equal(1000, scan.SkippedBytes);
        Assert.Contains("Xcode открыт", Assert.Single(scan.SkipNotes));
    }

    [Fact]
    public void Trash_HoldsEverything()
    {
        using var home = new FakeHome();
        home.Add(".Trash/new.txt", 100, Fresh);
        home.Old(".Trash/old folder/x.bin", 100);
        var scan = Scan(home, CleanerCatalog.Trash);
        Assert.Equal(200, scan.Bytes);
        Assert.Equal(2, scan.FileCount);
    }

    [Fact]
    public void NothingThere_IsEmpty()
    {
        using var home = new FakeHome();
        foreach (var category in CleanerCatalog.All)
            Assert.Equal(ScanState.Empty, Scan(home, category).State);
    }

    [MacFact]
    public void RealTrash_WithoutFullDiskAccess_AsksForIt()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var scan = new CleanerScanner(home, RunningAppSet.Empty, DateTime.UtcNow, FullDiskAccess.Check(home)).Scan(CleanerCatalog.Trash, CancellationToken.None);
        Assert.Contains(scan.State, new[] { ScanState.Ready, ScanState.Empty, ScanState.NeedsFullDiskAccess });
    }
}
