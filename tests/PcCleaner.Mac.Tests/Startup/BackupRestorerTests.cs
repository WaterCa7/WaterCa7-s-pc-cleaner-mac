using PcCleaner.Core;
using PcCleaner.Mac.Tests.Support;
using PcCleaner.Modules.Startup;
using Xunit;

namespace PcCleaner.Mac.Tests.Startup;

public class BackupRestorerTests
{
    private sealed class Setup : IDisposable
    {
        private readonly TempDir _dir = new();

        public Setup()
        {
            Home = Directory.CreateDirectory(Path.Combine(_dir.Path, "home")).FullName;
            System = Directory.CreateDirectory(Path.Combine(_dir.Path, "Library", "LaunchDaemons")).FullName;
            Store = new BackupStore(Path.Combine(Home, "Backups"));
            Restorer = new BackupRestorer(Store, Home, 501, Fake, new AdminPrompt(Fake), new LoginItemsService(Fake, Store));
        }

        public string Home { get; }

        public string System { get; }

        public BackupStore Store { get; }

        public FakeCommandRunner Fake { get; } = new();

        public BackupRestorer Restorer { get; }

        public string UserAgent(string label) => Path.Combine(Home, "Library", "LaunchAgents", label + ".plist");

        /// <summary>A plist with a backup of it, as the startup page leaves it before a change.</summary>
        public BackupRecord Saved(string path, string label, string domain, string action, LaunchSource source = LaunchSource.UserAgent)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, StartupHome.Agent(label, "/Users/anna/bin/tool"));
            return Store.SavePlist(path, label, new Dictionary<string, string>
            {
                ["label"] = label, ["source"] = source.ToString(), ["domain"] = domain, ["action"] = action,
            });
        }

        public void Loaded(string target, bool loaded)
            => Fake.On("/bin/launchctl", new[] { "print", target }, loaded ? new CommandResult(0, "x = {\n}\n", "") : new CommandResult(113, "", "Could not find service"));

        public void Ok(params string[] args) => Fake.On("/bin/launchctl", args, new CommandResult(0, "", ""));

        public void Dispose() => _dir.Dispose();
    }

    [Fact]
    public async Task RemovedUserAgent_GoesBack_AndStarts()
    {
        using var s = new Setup();
        var plist = s.UserAgent("com.example.tool");
        var record = s.Saved(plist, "com.example.tool", "gui/501", "удаление");
        var original = File.ReadAllBytes(plist);
        File.Delete(plist);
        s.Loaded("gui/501/com.example.tool", false);
        s.Ok("enable", "gui/501/com.example.tool");
        s.Ok("bootstrap", "gui/501", plist);

        Assert.Equal(new StartupResult(true, "«com.example.tool» восстановлена."), await s.Restorer.RestoreAsync(record, CancellationToken.None));
        Assert.Equal(original, File.ReadAllBytes(plist));
        Assert.DoesNotContain(s.Fake.Calls, c => c.StartsWith("/usr/bin/osascript", StringComparison.Ordinal));
    }

    [Fact]
    public async Task DisabledUserAgent_StillLoaded_IsOnlyEnabled()
    {
        using var s = new Setup();
        var plist = s.UserAgent("com.example.tool");
        var record = s.Saved(plist, "com.example.tool", "gui/501", "отключение");
        s.Loaded("gui/501/com.example.tool", true);
        s.Ok("enable", "gui/501/com.example.tool");
        Assert.True((await s.Restorer.RestoreAsync(record, CancellationToken.None)).Ok);
        Assert.DoesNotContain(s.Fake.Calls, c => c.Contains("bootstrap", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Daemon_ThroughThePasswordWindow()
    {
        using var s = new Setup();
        var plist = Path.Combine(s.System, "com.example.daemon.plist");
        var record = s.Saved(plist, "com.example.daemon", "system", "отключение", LaunchSource.Daemon);
        s.Loaded("system/com.example.daemon", false);
        var commands = new[]
        {
            new AdminCommand("/bin/launchctl", "enable", "system/com.example.daemon"),
            new AdminCommand("/bin/launchctl", "bootstrap", "system", plist),
        };
        s.Fake.On("/usr/bin/osascript", AdminPrompt.Arguments("Восстановить «com.example.daemon» в автозагрузке", commands), new CommandResult(0, "", ""));
        Assert.True((await s.Restorer.RestoreAsync(record, CancellationToken.None)).Ok);
        Assert.DoesNotContain(s.Fake.Calls, c => c.StartsWith("/bin/launchctl enable", StringComparison.Ordinal));
    }

    [Fact]
    public async Task MissingSystemPlist_IsCopiedBackAsRoot()
    {
        using var s = new Setup();
        var plist = Path.Combine(s.System, "com.example.global.plist");
        var record = s.Saved(plist, "com.example.global", "gui/501", "отключение", LaunchSource.GlobalAgent);
        File.Delete(plist);
        s.Loaded("gui/501/com.example.global", false);
        var commands = new[]
        {
            new AdminCommand("/bin/cp", record.StoredFile!, plist),
            new AdminCommand("/usr/sbin/chown", "root:wheel", plist),
            new AdminCommand("/bin/chmod", "644", plist),
            new AdminCommand("/bin/launchctl", "enable", "gui/501/com.example.global"),
            new AdminCommand("/bin/launchctl", "bootstrap", "gui/501", plist),
        };
        s.Fake.On("/usr/bin/osascript", AdminPrompt.Arguments("Восстановить «com.example.global» в автозагрузке", commands),
            new CommandResult(1, "", "execution error: User canceled. (-128)"));
        Assert.Equal(new StartupResult(false, "Отменено — ничего не изменено."), await s.Restorer.RestoreAsync(record, CancellationToken.None));
        Assert.False(File.Exists(plist));
    }

    [Fact]
    public async Task UndoOfEnabling_DisablesAgain()
    {
        using var s = new Setup();
        var record = s.Saved(s.UserAgent("com.example.tool"), "com.example.tool", "gui/501", "включение");
        s.Loaded("gui/501/com.example.tool", true);
        s.Ok("disable", "gui/501/com.example.tool");
        s.Ok("bootout", "gui/501/com.example.tool");
        Assert.Equal(new StartupResult(true, "«com.example.tool» снова отключена."), await s.Restorer.RestoreAsync(record, CancellationToken.None));
        Assert.Contains("/bin/launchctl bootout gui/501/com.example.tool", s.Fake.Calls);
    }

    [Fact]
    public async Task NothingToRestoreFrom()
    {
        using var s = new Setup();
        var plist = s.UserAgent("com.example.tool");
        var record = s.Saved(plist, "com.example.tool", "gui/501", "удаление");
        File.Delete(plist);
        File.Delete(record.StoredFile!);
        s.Loaded("gui/501/com.example.tool", false);
        Assert.Equal(new StartupResult(false, "Файл копии не найден — восстановить нельзя."), await s.Restorer.RestoreAsync(record, CancellationToken.None));
        Assert.DoesNotContain(s.Fake.Calls, c => c.Contains("enable", StringComparison.Ordinal));
    }

    [Fact]
    public async Task LoginItem_IsAddedBack()
    {
        using var s = new Setup();
        var record = s.Store.SaveLoginItem("Telegram", "/Applications/Telegram.app", hidden: true);
        s.Fake.On("/usr/bin/osascript", LoginItemsService.Arguments(LoginItemsService.AddScript, "/Applications/Telegram.app", "true"), new CommandResult(0, "", ""));
        Assert.True((await s.Restorer.RestoreAsync(record, CancellationToken.None)).Ok);
        Assert.Equal("убран из объектов входа", BackupRestorer.Describe(record));
    }
}
