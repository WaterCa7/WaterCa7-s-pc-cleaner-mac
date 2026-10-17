using PcCleaner.Core;
using PcCleaner.Mac.Tests.Support;
using PcCleaner.Modules.Processes;
using Xunit;

namespace PcCleaner.Mac.Tests.Processes;

public class ProcessesServiceTests
{
    private static readonly DateTime Stamp = new(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly string[] CommArgs = { "-axww", "-o", "pid=,ppid=,uid=,pcpu=,rss=,etime=,comm=" };
    private static readonly string[] ArgsArgs = { "-axww", "-o", "pid=,args=" };

    private static readonly ProcessEntry Telegram = new(
        new ProcessInfo(401, 1, 501, 0, 0, TimeSpan.Zero, "/Applications/Telegram.app/Contents/MacOS/Telegram", null), false, null, null, Array.Empty<Hint>());

    private static FakeCommandRunner Codesign(FakeCommandRunner fake, string path, int exit, string fixture)
        => fake.On("/usr/bin/codesign", new[] { "-dv", "--verbose=2", path }, new CommandResult(exit, "", Fixture.Text(fixture)));

    private static FakeCommandRunner Mixed()
    {
        var fake = new FakeCommandRunner()
            .OnFixture("/bin/ps", CommArgs, "ps/mixed-comm.txt")
            .OnFixture("/bin/ps", ArgsArgs, "ps/mixed-args.txt");
        Codesign(fake, "/Applications/Google Chrome.app", 0, "codesign/developer-id-dotnet.txt");
        Codesign(fake, "/private/tmp/.x/updater", 1, "codesign/unsigned.txt");
        Codesign(fake, "/Users/anna/.cargo/bin/rust-analyzer", 0, "codesign/adhoc-app.txt");
        Codesign(fake, "/opt/homebrew/Cellar/redis/7.2/bin/redis-server", 1, "codesign/unsigned.txt");
        return fake;
    }

    private static ProcessesService Service(FakeCommandRunner fake)
        => new(fake, new CodeSignatureService(fake, _ => Stamp), ownPid: 999, ownUid: 501,
            missing: path => path.EndsWith("/gone", StringComparison.Ordinal), exitWait: TimeSpan.Zero);

    [Fact]
    public async Task List_ChecksEachProgramOnce_AndSkipsMacOS()
    {
        var fake = Mixed();
        var list = await Service(fake).ListAsync(CancellationToken.None);
        Assert.Null(list.Error);
        Assert.Equal(13, list.Items.Count);
        var codesign = fake.Calls.Where(c => c.StartsWith("/usr/bin/codesign", StringComparison.Ordinal)).ToList();
        Assert.Equal(9, codesign.Count);
        Assert.Single(codesign, c => c.Contains("Google Chrome", StringComparison.Ordinal));
        Assert.DoesNotContain(codesign, c => c.Contains("--verify", StringComparison.Ordinal) || c.Contains("/sbin/", StringComparison.Ordinal) || c.Contains("/System/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task List_Entries()
    {
        var items = (await Service(Mixed()).ListAsync(CancellationToken.None)).Items.ToDictionary(e => e.Info.Pid);
        Assert.True(items[1].IsSystem && items[1].Protected is not null);
        Assert.True(items[160].IsSystem && items[160].Protected is not null);

        var helper = items[402];
        Assert.Equal((SignatureKind.DeveloperId, false, (string?)null), (helper.Signature!.Kind, helper.IsSystem, helper.Protected));
        Assert.Empty(helper.Hints);
        Assert.Equal("/Applications/Google Chrome.app", helper.Location);
        Assert.EndsWith("--type=renderer", helper.Info.CommandLine, StringComparison.Ordinal);

        Assert.True(items[510].HasStrongHint);
        Assert.True(items[520].HasStrongHint);
        Assert.True(items[540].HasStrongHint);
        Assert.False(items[530].HasStrongHint);
        Assert.False(items[500].HasStrongHint);
        Assert.Contains(items[570].Hints, h => h is { Strong: true } && h.Text.StartsWith("файл программы удалён", StringComparison.Ordinal));
        Assert.Equal(new[] { new Hint("без подписи разработчика", false) }, items[580].Hints);

        Assert.Equal(("системный процесс (root)", false), (items[550].Protected, items[550].IsSystem));
        var bare = items[560];
        Assert.Equal((false, (SignatureInfo?)null, (string?)null), (bare.IsSystem, bare.Signature, bare.Location));
    }

    [Fact]
    public async Task List_PsFails_ReportsIt()
    {
        var fake = new FakeCommandRunner().On("/bin/ps", CommArgs, new CommandResult(1, "", "ps: Operation not permitted"));
        var list = await Service(fake).ListAsync(CancellationToken.None);
        Assert.Empty(list.Items);
        Assert.Equal("не удалось получить список процессов: ps: Operation not permitted", list.Error);
    }

    [Fact]
    public async Task UpdatedInPlace_IsOnlyAdvice()
    {
        var fake = new FakeCommandRunner()
            .On("/bin/ps", CommArgs, new CommandResult(0, "  700     1   501   0.0   1024 05:00 /opt/homebrew/Cellar/redis/7.1/bin/redis-server\n", ""))
            .On("/bin/ps", ArgsArgs, new CommandResult(0, "", ""));
        var service = new ProcessesService(fake, new CodeSignatureService(fake, _ => Stamp), 999, 501, missing: _ => true, exitWait: TimeSpan.Zero);
        var entry = Assert.Single((await service.ListAsync(CancellationToken.None)).Items);
        Assert.False(entry.HasStrongHint);
        Assert.Contains(entry.Hints, h => h.Text.StartsWith("программа обновилась", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Terminate_Protected_DoesNothing()
    {
        var fake = new FakeCommandRunner();
        var result = await Service(fake).TerminateAsync(Telegram with { Protected = "это сама программа" }, force: false, CancellationToken.None);
        Assert.Equal(new TerminateResult(false, true, "«Telegram» нельзя завершить: это сама программа."), result);
        Assert.Empty(fake.Calls);
    }

    [Fact]
    public async Task Terminate_NumberNowBelongsToAnotherProgram_DoesNotKill()
    {
        var fake = new FakeCommandRunner()
            .On("/bin/ps", new[] { "-p", "401", "-o", "uid=,comm=" }, new CommandResult(0, "  501 /Applications/Other.app/Contents/MacOS/Other\n", ""));
        var result = await Service(fake).TerminateAsync(Telegram, force: false, CancellationToken.None);
        Assert.True(result.Ok);
        Assert.DoesNotContain(fake.Calls, c => c.StartsWith("/bin/kill", StringComparison.Ordinal));
    }

    private static FakeCommandRunner Running(bool staysAlive, string signal, int killExit = 0)
        => new FakeCommandRunner()
            .On("/bin/ps", new[] { "-p", "401", "-o", "uid=,comm=" }, new CommandResult(0, "  501 /Applications/Telegram.app/Contents/MacOS/Telegram\n", ""))
            .On("/bin/kill", new[] { signal, "401" }, new CommandResult(killExit, "", killExit == 0 ? "" : "kill: 401: No such process"))
            .On("/bin/kill", new[] { "-0", "401" }, new CommandResult(staysAlive ? 0 : 1, "", ""));

    [Fact]
    public async Task Terminate_Ends()
    {
        var fake = Running(staysAlive: false, "-TERM");
        Assert.Equal(new TerminateResult(true, false, "«Telegram» завершён."), await Service(fake).TerminateAsync(Telegram, force: false, CancellationToken.None));
        Assert.Contains("/bin/kill -TERM 401", fake.Calls);
    }

    [Fact]
    public async Task Terminate_StillRunning_OffersForce()
    {
        var result = await Service(Running(staysAlive: true, "-TERM")).TerminateAsync(Telegram, force: false, CancellationToken.None);
        Assert.Equal((false, true), (result.Ok, result.StillRunning));
        Assert.Contains("принудительно", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Terminate_Force_UsesKill()
    {
        var fake = Running(staysAlive: false, "-KILL");
        Assert.True((await Service(fake).TerminateAsync(Telegram, force: true, CancellationToken.None)).Ok);
        Assert.Contains("/bin/kill -KILL 401", fake.Calls);
    }

    [Fact]
    public async Task Terminate_EndedOnItsOwn_IsFine()
    {
        var result = await Service(Running(staysAlive: false, "-TERM", killExit: 1)).TerminateAsync(Telegram, force: false, CancellationToken.None);
        Assert.Equal(new TerminateResult(true, false, "«Telegram» уже завершился."), result);
    }

    [Fact]
    public async Task Sha256_OfAFile()
    {
        using var dir = new TempDir();
        Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", await ProcessesService.Sha256Async(dir.File("abc", "abc"), CancellationToken.None));
        Assert.Null(await ProcessesService.Sha256Async(Path.Combine(dir.Path, "нет"), CancellationToken.None));
    }

    [Fact]
    public void FileMissing_OnlyWhenReallyGone()
    {
        using var dir = new TempDir();
        Assert.False(ProcessesService.FileMissing(dir.File("x", "1")));
        Assert.True(ProcessesService.FileMissing(Path.Combine(dir.Path, "нет")));
        Assert.True(ProcessesService.FileMissing(Path.Combine(dir.Path, "нет", "x")));
    }

    [MacFact]
    public async Task RealMac_FindsItself_AndProtectsIt()
    {
        var runner = new ProcessCommandRunner();
        Assert.True(MacNative.UserId > 0);
        var service = new ProcessesService(runner, new CodeSignatureService(runner), Environment.ProcessId, MacNative.UserId);
        var list = await service.ListAsync(CancellationToken.None);
        Assert.Null(list.Error);
        Assert.True(list.Items.Count > 20);
        Assert.Equal("это сама программа", list.Items.Single(e => e.Info.Pid == Environment.ProcessId).Protected);
        Assert.True(list.Items.Single(e => e.Info.Pid == 1).IsSystem);
        Assert.True(await service.IsAliveAsync(Environment.ProcessId, CancellationToken.None));
    }
}
