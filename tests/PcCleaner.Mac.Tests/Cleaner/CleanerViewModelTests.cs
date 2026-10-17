using PcCleaner.Core;
using PcCleaner.Mac.Tests.Support;
using PcCleaner.Modules.Cleaner;
using Xunit;

namespace PcCleaner.Mac.Tests.Cleaner;

[Collection("dialogs")]
public class CleanerViewModelTests
{
    internal sealed class FakeTrash(bool ok = true) : ITrash
    {
        public List<string> Moved { get; } = new();

        public Task<TrashOutcome> MoveAsync(string path, CancellationToken ct)
        {
            if (!ok)
                return Task.FromResult(new TrashOutcome(false, "нет прав"));
            Moved.Add(path);
            return Task.FromResult(new TrashOutcome(true, null));
        }
    }

    internal static FakeCommandRunner Runner() => new FakeCommandRunner()
        .OnFixture("/bin/df", new[] { "-k", "-P" }, "df/all-volumes.txt")
        .OnFixture("/usr/sbin/diskutil", new[] { "info", "-plist", "/" }, "diskutil/root-runner.plist")
        .OnFixture("/usr/bin/tmutil", new[] { "listlocalsnapshots", "/" }, "tmutil/three.txt")
        .On("/usr/bin/osascript", new[] { "-e", "tell application \"Finder\" to empty the trash" }, new CommandResult(0, "", ""));

    internal static CleanerViewModel Create(FakeHome home, FakeCommandRunner? runner = null, ITrash? trash = null, FdaState fda = FdaState.Denied)
    {
        runner ??= Runner();
        return new CleanerViewModel(
            new CleanerService(home.Path, runner, new ManagedFileRemover(), () => fda, () => FakeHome.Now),
            new DiskService(runner, _ => null),
            new SpaceAnalyzer(home.Path),
            trash ?? new FakeTrash(),
            new MacShell(runner),
            () => fda,
            home.Path,
            () => FakeHome.Now);
    }

    private static async Task Confirmed(Func<Task> action)
    {
        Dialogs.ConfirmHandler = _ => Task.FromResult(true);
        try
        {
            await action();
        }
        finally
        {
            Dialogs.UseSilentMode();
        }
    }

    [Fact]
    public async Task Navigation_LoadsCategoriesAndDisks()
    {
        using var home = new FakeHome();
        home.Old("Library/Caches/com.example.app/a.bin", 2048);
        var vm = Create(home);
        await vm.OnNavigatedToAsync();
        Assert.True(vm.HasScan);
        Assert.Equal("2 КБ", vm.Categories.Single(c => c.Category == CleanerCatalog.AppCaches).SizeText);
        Assert.Equal(2048, vm.SelectedBytes);
        Assert.Equal("Очистить выбранное (2 КБ)", vm.CleanButtonText);
        Assert.Equal(new[] { "Macintosh HD", "Мой диск" }, vm.Volumes.Select(v => v.Name));
        Assert.StartsWith("Локальных снимков Time Machine: 3", vm.SnapshotsText);
        Assert.True(vm.ShowFdaHint);
    }

    [Fact]
    public async Task DeveloperCaches_AreNotCheckedByDefault()
    {
        using var home = new FakeHome();
        home.Old("Library/Caches/Homebrew/pkg.tar.gz", 4096);
        var vm = Create(home);
        await vm.RefreshAsync(CancellationToken.None);
        Assert.Equal(0, vm.SelectedBytes);
        vm.Categories.Single(c => c.Category == CleanerCatalog.Developer).IsChecked = true;
        Assert.Equal(4096, vm.SelectedBytes);
    }

    [Fact]
    public async Task Clean_Declined_DeletesNothing()
    {
        Dialogs.UseSilentMode();
        using var home = new FakeHome();
        var file = home.Old("Library/Caches/com.example.app/a.bin", 2048);
        var vm = Create(home);
        await vm.RefreshAsync(CancellationToken.None);
        await vm.CleanAsync();
        Assert.True(File.Exists(file));
        Assert.Null(vm.LastResult);
    }

    [Fact]
    public async Task Clean_Confirmed_ListsAndDeletes_ThenRescans()
    {
        using var home = new FakeHome();
        var file = home.Old("Library/Caches/com.example.app/a.bin", 2048);
        var vm = Create(home);
        await vm.RefreshAsync(CancellationToken.None);
        string? question = null;
        Dialogs.ConfirmHandler = r =>
        {
            question = r.Message;
            return Task.FromResult(true);
        };
        try
        {
            await vm.CleanAsync();
        }
        finally
        {
            Dialogs.UseSilentMode();
        }
        Assert.Contains("• Кэши программ — 2 КБ", question);
        Assert.DoesNotContain("Кэши разработки", question);
        Assert.False(File.Exists(file));
        Assert.Equal("Удалено 2 КБ (1 файл).", vm.LastResult);
        Assert.Equal(0, vm.SelectedBytes);
    }

    [Fact]
    public async Task EmptyTrash_OnlyAfterItsOwnYes()
    {
        using var home = new FakeHome();
        var runner = Runner();
        var vm = Create(home, runner);
        Dialogs.UseSilentMode();
        await vm.EmptyTrashAsync();
        Assert.DoesNotContain(runner.Calls, c => c.Contains("empty the trash"));
        await Confirmed(vm.EmptyTrashAsync);
        Assert.Contains(runner.Calls, c => c.Contains("empty the trash"));
        Assert.Equal("Корзина очищена.", vm.LastResult);
    }

    [Fact]
    public async Task LargeFile_ToTheTrash_AfterYes_AndTheRowGoes()
    {
        using var home = new FakeHome();
        var big = home.Old("Downloads/big.iso", 600 * 1024 * 1024);
        var trash = new FakeTrash();
        var vm = Create(home, trash: trash);
        await vm.FindLargeFilesCommand.ExecuteAsync();
        var row = Assert.Single(vm.LargeFiles);
        Assert.Equal("~" + Path.DirectorySeparatorChar + "Downloads", row.Folder);
        Dialogs.UseSilentMode();
        await vm.MoveToTrashAsync(row);
        Assert.Empty(trash.Moved);
        await Confirmed(() => vm.MoveToTrashAsync(row));
        Assert.Equal(Path.GetFullPath(big), Path.GetFullPath(Assert.Single(trash.Moved)));
        Assert.Empty(vm.LargeFiles);
        Assert.StartsWith("Перемещено в Корзину: big.iso", vm.AnalysisStatus);
    }

    [Fact]
    public async Task TrashRefused_KeepsTheRow()
    {
        using var home = new FakeHome();
        home.Old("Downloads/big.iso", 600 * 1024 * 1024);
        var vm = Create(home, trash: new FakeTrash(ok: false));
        await vm.FindLargeFilesCommand.ExecuteAsync();
        await Confirmed(() => vm.MoveToTrashAsync(vm.LargeFiles[0]));
        Assert.Single(vm.LargeFiles);
    }

    [Fact]
    public async Task TopFolders_And_OldDownloads()
    {
        using var home = new FakeHome();
        home.Old("Movies/film.mov", 5000);
        home.Add("Downloads/old.dmg", 3000, TimeSpan.FromDays(200));
        var vm = Create(home);
        await vm.FindTopFoldersCommand.ExecuteAsync();
        await vm.FindOldDownloadsCommand.ExecuteAsync();
        Assert.Equal("Movies", vm.TopFolders[0].Name);
        Assert.Equal(100, vm.TopFolders[0].Percent);
        Assert.Equal("old.dmg", Assert.Single(vm.OldDownloads).Name);
    }
}
