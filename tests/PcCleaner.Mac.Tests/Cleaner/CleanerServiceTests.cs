using PcCleaner.Core;
using PcCleaner.Mac.Tests.Support;
using PcCleaner.Modules.Cleaner;
using PcCleaner.Modules.Dashboard;
using Xunit;

namespace PcCleaner.Mac.Tests.Cleaner;

public class CleanerServiceTests
{
    /// <summary>The managed remover with a hook before every file (denials, cancellation).</summary>
    private sealed class HookedRemover(Func<string, RemoveOutcome?>? before = null) : IFileRemover
    {
        private readonly ManagedFileRemover _inner = new();

        public RemoveOutcome RemoveFile(string root, string relative)
            => before?.Invoke(relative) ?? _inner.RemoveFile(root, relative);

        public RemoveOutcome RemoveEmptyDir(string root, string relative) => _inner.RemoveEmptyDir(root, relative);
    }

    private static CleanerService Service(FakeHome home, IFileRemover? remover = null, FakeCommandRunner? runner = null)
        => new(home.Path, runner ?? new FakeCommandRunner(), remover ?? new HookedRemover(), () => FdaState.Denied, () => FakeHome.Now);

    [Fact]
    public async Task Clean_DeletesOnlyTheTargetsFiles_AndKeepsTheirFolders()
    {
        using var home = new FakeHome();
        var old = home.Old("Library/Caches/com.example.app/Data/Sub/a.bin", 1000);
        var fresh = home.Add("Library/Caches/com.example.app/Data/new.bin", 10, TimeSpan.FromMinutes(1));
        var apple = home.Old("Library/Caches/com.apple.Music/x.bin");
        var archive = home.Old("Library/Developer/Xcode/Archives/a.xcarchive/Info.plist");
        var result = await Service(home).CleanAsync(new[] { CleanerCatalog.AppCaches, CleanerCatalog.Developer }, null, CancellationToken.None);

        Assert.False(File.Exists(old));
        Assert.True(File.Exists(fresh));
        Assert.True(File.Exists(apple));
        Assert.True(File.Exists(archive));
        Assert.True(Directory.Exists(Path.Combine(home.Path, "Library/Caches/com.example.app/Data")));
        Assert.False(Directory.Exists(Path.Combine(home.Path, "Library/Caches/com.example.app/Data/Sub")));
        Assert.Equal(new CleanResult(1000, 1, 0, false), result);
        Assert.Equal("Удалено 1000 Б (1 файл).", result.Message);
        Assert.False(HealthScoring.LooksLikeFailure(result.Message));
    }

    [Fact]
    public async Task Clean_ProgramOpenedSinceTheScan_KeepsItsCache()
    {
        using var home = new FakeHome();
        var file = home.Old("Library/Caches/Spotify/a.bin");
        var runner = new FakeCommandRunner().On("/bin/ps", new[] { "-xww", "-o", "comm=" },
            new CommandResult(0, "/Applications/Spotify.app/Contents/MacOS/Spotify\n", ""));
        var result = await Service(home, runner: runner).CleanAsync(new[] { CleanerCatalog.AppCaches }, null, CancellationToken.None);
        Assert.True(File.Exists(file));
        Assert.Equal("Удалять уже нечего.", result.Message);
    }

    [Fact]
    public async Task Clean_DeniedFiles_AreCountedAsSkipped()
    {
        using var home = new FakeHome();
        home.Old("Library/Logs/App/a.log", 100);
        home.Old("Library/Logs/App/b.log", 100);
        var remover = new HookedRemover(rel => rel.EndsWith("b.log", StringComparison.Ordinal) ? RemoveOutcome.Denied : null);
        var result = await Service(home, remover).CleanAsync(new[] { CleanerCatalog.Logs }, null, CancellationToken.None);
        Assert.Equal(new CleanResult(100, 1, 1, false), result);
        Assert.Equal("Удалено 100 Б (1 файл). Пропущено: 1 — заняты или нет доступа.", result.Message);
        Assert.False(HealthScoring.LooksLikeFailure(result.Message));
    }

    [Fact]
    public async Task Clean_Cancelled_StopsAndSaysSo()
    {
        using var home = new FakeHome();
        for (var i = 0; i < 5; i++)
            home.Old($"Library/Logs/App/{i}.log", 10);
        using var cts = new CancellationTokenSource();
        var count = 0;
        var remover = new HookedRemover(_ =>
        {
            if (++count == 2)
                cts.Cancel();
            return null;
        });
        var result = await Service(home, remover).CleanAsync(new[] { CleanerCatalog.Logs }, null, cts.Token);
        Assert.True(result.Cancelled);
        Assert.Equal(2, result.Deleted);
        Assert.EndsWith("Очистка остановлена по вашей просьбе.", result.Message);
    }

    [Fact]
    public async Task Clean_TheTrashIsNeverDeletedFileByFile()
    {
        using var home = new FakeHome();
        var file = home.Old(".Trash/x.bin");
        await Service(home).CleanAsync(new[] { CleanerCatalog.Trash }, null, CancellationToken.None);
        Assert.True(File.Exists(file));
    }

    [Fact]
    public async Task Clean_ReportsProgress()
    {
        using var home = new FakeHome();
        home.Old("Library/Logs/App/a.log");
        home.Old("Library/Logs/App/b.log");
        var reports = new List<double>();
        await Service(home).CleanAsync(new[] { CleanerCatalog.Logs }, new SyncProgress(reports.Add), CancellationToken.None);
        Assert.Equal(new[] { 50.0, 100.0 }, reports);
    }

    [Theory]
    [InlineData(0, "", "Корзина очищена.")]
    [InlineData(1, "execution error: Not authorized to send Apple events to Finder. (-1743)", "Не удалось: macOS не разрешила программе управлять Finder.")]
    [InlineData(1, "execution error: User canceled. (-128)", "Отменено — ничего не изменено.")]
    [InlineData(1, "что-то ещё", "Не удалось очистить Корзину")]
    public async Task EmptyTrash_ThroughFinder(int exit, string stderr, string expected)
    {
        using var home = new FakeHome();
        var runner = new FakeCommandRunner().On("/usr/bin/osascript", new[] { "-e", "tell application \"Finder\" to empty the trash" },
            new CommandResult(exit, "", stderr));
        Assert.StartsWith(expected, await Service(home, runner: runner).EmptyTrashAsync(CancellationToken.None));
    }

    [MacFact]
    public async Task RealDeletion_InATempHome()
    {
        using var home = new FakeHome();
        var file = home.Old("Library/Caches/com.example.app/a.bin");
        var service = new CleanerService(home.Path, new ProcessCommandRunner(), new UnixFileRemover(), () => FdaState.Denied, () => FakeHome.Now);
        var result = await service.CleanAsync(new[] { CleanerCatalog.AppCaches }, null, CancellationToken.None);
        Assert.Equal(1, result.Deleted);
        Assert.False(File.Exists(file));
    }

    private sealed class SyncProgress(Action<double> report) : IProgress<double>
    {
        public void Report(double value) => report(value);
    }
}
