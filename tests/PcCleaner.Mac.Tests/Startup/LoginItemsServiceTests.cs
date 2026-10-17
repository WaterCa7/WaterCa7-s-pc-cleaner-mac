using PcCleaner.Core;
using PcCleaner.Mac.Tests.Support;
using PcCleaner.Modules.Startup;
using Xunit;

namespace PcCleaner.Mac.Tests.Startup;

public class LoginItemsServiceTests
{
    private static readonly LoginItem Telegram = new("Telegram", "/Applications/Telegram.app", false);

    [Fact]
    public void Arguments_ScriptLinesThenOurMarkerThenTheData()
    {
        var args = LoginItemsService.Arguments(LoginItemsService.RemoveScript, "-e evil");
        Assert.Equal(LoginItemsService.RemoveScript.Length * 2 + 2, args.Count);
        Assert.Equal(new[] { "login-item", "-e evil" }, args.TakeLast(2));
        Assert.All(LoginItemsService.RemoveScript.Concat(LoginItemsService.AddScript), line => Assert.DoesNotContain("evil", line));
        Assert.Equal(LoginItemsService.ListScript.Length * 2, LoginItemsService.Arguments(LoginItemsService.ListScript).Count);
    }

    [Fact]
    public void Parse()
    {
        Assert.Equal(
            new[] { Telegram, new LoginItem("Музыка Pro", "/Applications/Музыка Pro.app", true) },
            LoginItemsService.Parse("Telegram\t/Applications/Telegram.app/\tfalse\nМузыка Pro\t/Applications/Музыка Pro.app\ttrue\n\nbroken line\n"));
    }

    [Fact]
    public async Task List_WithoutPermission_SaysWhereToAllow()
    {
        var fake = new FakeCommandRunner().On("/usr/bin/osascript", LoginItemsService.Arguments(LoginItemsService.ListScript),
            new CommandResult(1, "", "execution error: Not authorized to send Apple events to System Events. (-1743)"));
        var result = await new LoginItemsService(fake, new BackupStore(TestEnvironment.Root)).ListAsync(CancellationToken.None);
        Assert.True(result.NeedsPermission);
        Assert.Contains("Автоматизация", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Remove_CopiesFirst()
    {
        using var dir = new TempDir();
        var backups = new BackupStore(dir.Path);
        var fake = new FakeCommandRunner().On("/usr/bin/osascript", LoginItemsService.Arguments(LoginItemsService.RemoveScript, "Telegram"), new CommandResult(0, "", ""));
        var result = await new LoginItemsService(fake, backups).RemoveAsync(Telegram, CancellationToken.None);
        Assert.True(result.Ok, result.Message);
        var backup = Assert.Single(backups.List());
        Assert.Equal((BackupKind.LoginItem, "/Applications/Telegram.app", "false"), (backup.Kind, backup.Details["path"], backup.Details["hidden"]));
    }

    [Fact]
    public async Task Remove_NoBackup_NoChange()
    {
        using var dir = new TempDir();
        var fake = new FakeCommandRunner();
        var result = await new LoginItemsService(fake, new BackupStore(dir.File("file", "x"))).RemoveAsync(Telegram, CancellationToken.None);
        Assert.False(result.Ok);
        Assert.Empty(fake.Calls);
    }

    [Fact]
    public async Task Add()
    {
        var fake = new FakeCommandRunner().On("/usr/bin/osascript", LoginItemsService.Arguments(LoginItemsService.AddScript, "/Applications/Telegram.app", "true"), new CommandResult(0, "", ""));
        var service = new LoginItemsService(fake, new BackupStore(TestEnvironment.Root));
        Assert.Equal(new StartupResult(true, "«Telegram» снова открывается при входе."), await service.AddAsync("/Applications/Telegram.app", hidden: true, CancellationToken.None));
        Assert.False((await service.AddAsync("Telegram.app", false, CancellationToken.None)).Ok);
    }

    [MacFact]
    public async Task RealMac_ScriptsCompile()
    {
        using var dir = new TempDir();
        var runner = new ProcessCommandRunner();
        foreach (var script in new[] { LoginItemsService.ListScript, LoginItemsService.RemoveScript, LoginItemsService.AddScript })
        {
            var result = await runner.RunAsync("/usr/bin/osacompile", new[] { "-o", Path.Combine(dir.Path, Guid.NewGuid() + ".scpt") }
                .Concat(script.SelectMany(line => new[] { "-e", line })).ToArray());
            Assert.True(result.Ok, result.StdErr);
        }
    }
}
