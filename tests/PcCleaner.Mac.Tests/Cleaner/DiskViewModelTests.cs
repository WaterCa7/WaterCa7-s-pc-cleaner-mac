using PcCleaner.Core;
using PcCleaner.Mac.Tests.Support;
using PcCleaner.Modules.Cleaner;
using Xunit;

namespace PcCleaner.Mac.Tests.Cleaner;

[Collection("dialogs")]
public class DiskViewModelTests
{
    private static DiskViewModel Create(FakeHome home, FakeCommandRunner? runner = null, ITrash? trash = null)
    {
        runner ??= CleanerViewModelTests.Runner();
        return new DiskViewModel(
            new DiskService(runner, _ => null),
            new SpaceAnalyzer(home.Path),
            trash ?? new CleanerViewModelTests.FakeTrash(),
            new MacShell(runner),
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
    public void ThePage_IsDisksOfTheSystemSection()
    {
        using var home = new FakeHome();
        var vm = Create(home);
        Assert.Equal("Disk", vm.Key);
        Assert.Equal("Диски", vm.Title);
        Assert.Equal("HardDrive", vm.Icon);
    }

    [Fact]
    public async Task Navigation_LoadsTheVolumesAndSnapshots()
    {
        using var home = new FakeHome();
        var vm = Create(home);
        await vm.OnNavigatedToAsync();
        Assert.Equal(new[] { "Macintosh HD", "Мой диск" }, vm.Volumes.Select(v => v.Name));
        Assert.StartsWith("Локальных снимков Time Machine: 3", vm.SnapshotsText);
    }

    [Fact]
    public async Task RefreshCommand_ReadsTheVolumesAgain()
    {
        using var home = new FakeHome();
        var runner = CleanerViewModelTests.Runner();
        var vm = Create(home, runner);
        await vm.OnNavigatedToAsync();
        Assert.Equal(2, vm.Volumes.Count);
        var before = runner.Calls.Count(c => c.StartsWith("/bin/df", StringComparison.Ordinal));

        runner.On("/bin/df", new[] { "-k", "-P" }, new CommandResult(0,
            "Filesystem     1024-blocks      Used Available Capacity  Mounted on\n" +
            "/dev/disk3s5     482797652 312345678 150123456    68%    /System/Volumes/Data\n", ""));
        await vm.RefreshCommand.ExecuteAsync();

        Assert.Equal(before + 1, runner.Calls.Count(c => c.StartsWith("/bin/df", StringComparison.Ordinal)));
        Assert.Single(vm.Volumes);
    }

    [Fact]
    public async Task LargeFile_ToTheTrash_AfterYes_AndTheRowGoes()
    {
        using var home = new FakeHome();
        var big = home.Old("Downloads/big.iso", 600 * 1024 * 1024);
        var trash = new CleanerViewModelTests.FakeTrash();
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
        var vm = Create(home, trash: new CleanerViewModelTests.FakeTrash(ok: false));
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
