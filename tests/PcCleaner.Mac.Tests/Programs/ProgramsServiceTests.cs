using PcCleaner.Core;
using PcCleaner.Mac.Tests.Cleaner;
using PcCleaner.Mac.Tests.Support;
using PcCleaner.Modules.Processes;
using PcCleaner.Modules.Programs;
using Xunit;

namespace PcCleaner.Mac.Tests.Programs;

public class ProgramsServiceTests
{
    private static readonly string[] CommArgs = { "-axww", "-o", "pid=,ppid=,uid=,pcpu=,rss=,etime=,comm=" };
    private static readonly string[] ArgsArgs = { "-axww", "-o", "pid=,args=" };

    /// <summary>A trash that refuses chosen paths.</summary>
    private sealed class PickyTrash(params string[] refused) : ITrash
    {
        public List<string> Moved { get; } = new();

        public Task<TrashOutcome> MoveAsync(string path, CancellationToken ct)
        {
            if (refused.Contains(path))
                return Task.FromResult(new TrashOutcome(false, "нет прав"));
            Moved.Add(path);
            return Task.FromResult(new TrashOutcome(true, null));
        }
    }

    internal static ProgramsService Service(ITrash trash, FakeCommandRunner? fake = null, string home = "/Users/anna")
    {
        fake ??= new FakeCommandRunner();
        var signatures = new CodeSignatureService(fake, _ => new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc));
        var processes = new ProcessesService(fake, signatures, 999, 501, _ => false, TimeSpan.Zero, _ => null);
        var folders = new AppFolders("/Applications", home + "/Applications", Array.Empty<string>());
        return new ProgramsService(new AppCatalog(folders, fake, signatures), trash, processes, home, () => FdaState.Denied);
    }

    private static readonly InstalledApp Writer = LeftoverFinderTests.App("/Applications/Writer.app", "Writer", "com.vendor.writer");

    private static readonly Leftover[] Leftovers =
    {
        new("/Users/anna/Library/Caches/com.vendor.writer", "Кэш", true, 1000),
        new("/Users/anna/Library/Preferences/com.vendor.writer.plist", "Настройки", true, 24),
        new("/Users/anna/Library/Group Containers/ABCDE12345.shared", "Общие данные", false, 5000),
    };

    [Fact]
    public async Task Uninstall_AppThenTickedLeftovers()
    {
        var trash = new PickyTrash();
        var result = await Service(trash).UninstallAsync(Writer, Leftovers, 1L << 20, CancellationToken.None);
        Assert.True(result.Ok);
        Assert.Equal(new[] { Writer.Path, Leftovers[0].Path, Leftovers[1].Path }, trash.Moved);
        Assert.Equal((1L << 20) + 1024, result.FreedBytes);
        Assert.Equal("«Writer» перемещена в Корзину вместе с 2 остатками (1 МБ). Вернуть можно из Корзины (Finder → «Вернуть»).", result.Message);
    }

    [Fact]
    public async Task AppNotMoved_LeftoversUntouched()
    {
        var trash = new PickyTrash(Writer.Path);
        var result = await Service(trash).UninstallAsync(Writer, Leftovers, null, CancellationToken.None);
        Assert.False(result.Ok);
        Assert.Empty(trash.Moved);
        Assert.EndsWith("Остатки не тронуты.", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LeftoverNotMoved_IsReported()
    {
        var trash = new PickyTrash(Leftovers[1].Path);
        var result = await Service(trash).UninstallAsync(Writer, Leftovers, null, CancellationToken.None);
        Assert.True(result.Ok);
        Assert.Single(result.Failed);
        Assert.Contains("Не удалось переместить: 1", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Protected_IsRefused()
    {
        var trash = new PickyTrash();
        var apple = Writer with { Location = AppLocation.Apple };
        var self = Writer with { BundleId = AppInfo.BundleId };
        Assert.False((await Service(trash).UninstallAsync(apple, Leftovers, null, CancellationToken.None)).Ok);
        Assert.False((await Service(trash).UninstallAsync(self, Leftovers, null, CancellationToken.None)).Ok);
        Assert.Empty(trash.Moved);
    }

    [Fact]
    public async Task Running_OnlyTheUsersProcessesInsideTheApp_AndQuit()
    {
        var fake = new FakeCommandRunner()
            .On("/bin/ps", CommArgs, new CommandResult(0,
                "  700     1   501   1.0  1024 05:00 /Applications/Writer.app/Contents/MacOS/Writer\n" +
                "  701   700   501   0.0  1024 05:00 /Applications/Writer.app/Contents/Frameworks/Helper.app/Contents/MacOS/Helper\n" +
                "  702     1     0   0.0  1024 05:00 /Applications/Writer.app/Contents/Library/LaunchServices/daemon\n" +
                "  703     1   501   0.0  1024 05:00 /Applications/Writer Pro.app/Contents/MacOS/Writer Pro\n", ""))
            .On("/bin/ps", ArgsArgs, new CommandResult(0, "", ""));
        var service = Service(new PickyTrash(), fake);
        Assert.Equal(new[] { 700, 701 }, (await service.RunningAsync(Writer, CancellationToken.None)).Select(e => e.Info.Pid));

        // Not confirmed by ps any more (the fake does not know "ps -p"): taken as already ended, nothing is killed.
        Assert.False(await service.QuitAsync(Writer, force: true, CancellationToken.None));
        Assert.DoesNotContain(fake.Calls, c => c.StartsWith("/bin/kill", StringComparison.Ordinal));
        var calls = fake.Calls.ToList();
        Assert.True(calls.IndexOf("/bin/ps -p 700 -o uid=,comm=") < calls.IndexOf("/bin/ps -p 701 -o uid=,comm="));
    }
}
