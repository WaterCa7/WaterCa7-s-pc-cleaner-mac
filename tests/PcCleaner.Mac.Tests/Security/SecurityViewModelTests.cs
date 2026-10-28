using PcCleaner.Core;
using PcCleaner.Mac.Tests.Support;
using PcCleaner.Modules.Security;
using Xunit;

namespace PcCleaner.Mac.Tests.Security;

/// <summary>The page model: checks by group, hosts lines, and nothing changes without «Да».</summary>
[Collection("dialogs")]
public class SecurityViewModelTests
{
    internal sealed class Page : IDisposable
    {
        public Page(bool infected = false, SecurityPart part = SecurityPart.Protection)
        {
            Mac = new SecurityMac();
            if (infected)
                Mac.Infected();
            Mac.Fake.OnFixture("/usr/sbin/softwareupdate", new[] { "-l" }, "security/softwareupdate-26.txt");
            Vm = new SecurityViewModel(Mac.Service, new SecurityFixes(new AdminPrompt(Mac.Fake), new BackupStore(Path.Combine(Dir.Path, "Backups")),
                Path.Combine(Dir.Path, "tmp")), new MacShell(Mac.Fake), part);
        }

        public SecurityMac Mac { get; }

        public TempDir Dir { get; } = new();

        public SecurityViewModel Vm { get; }

        public async Task LoadAsync()
        {
            await Vm.OnNavigatedToAsync();
            await Vm.UpdatesLoading;
        }

        public CheckItem Item(string id) => Vm.Groups.SelectMany(g => g.Items).Single(i => i.Check.Id == id);

        public void Dispose()
        {
            Mac.Dispose();
            Dir.Dispose();
        }
    }

    [Fact]
    public async Task Groups_AndUpdatesInTheBackground()
    {
        using var page = new Page();
        await page.LoadAsync();
        Assert.Equal(new[] { CheckGroup.MacOs, CheckGroup.Access, CheckGroup.Profiles }, page.Vm.Groups.Select(g => g.Group));
        Assert.Equal("Защита macOS", page.Vm.Groups[0].Title);
        var updates = page.Item("updates");
        Assert.Equal(("есть обновления безопасности", "WarningBrush", "Warning"), (updates.Summary, updates.BrushKey, updates.IconName));
        Assert.Equal(("ShieldCheckmark", true), (page.Item("gatekeeper").IconName, page.Item("gatekeeper").IsOk));
        Assert.Equal(("Error", false), (page.Item("sip").IconName, page.Item("sip").CanFix));
        Assert.True(page.Item("firewall").CanFix);
        Assert.True(page.Item("filevault").HasSettings);
        Assert.Equal("Требуют внимания: 7.", page.Vm.Summary);
    }

    [Fact]
    public async Task OpenSettings_OpensThePane()
    {
        using var page = new Page();
        await page.LoadAsync();
        await page.Item("filevault").SettingsCommand.ExecuteAsync();
        Assert.Contains($"/usr/bin/open {SecurityLinks.FileVault}", page.Mac.Fake.Calls);
    }

    [Fact]
    public async Task Fix_OnlyAfterYes_ListingTheCommands()
    {
        using var page = new Page();
        await page.LoadAsync();
        var asked = new List<string>();
        try
        {
            Dialogs.ConfirmHandler = r =>
            {
                asked.Add(r.Message);
                return Task.FromResult(false);
            };
            await page.Vm.FixAsync(page.Item("firewall"));
            Assert.DoesNotContain(page.Mac.Fake.Calls, c => c.StartsWith("/usr/bin/osascript", StringComparison.Ordinal));
            Assert.Contains("• /usr/libexec/ApplicationFirewall/socketfilterfw --setglobalstate on", Assert.Single(asked), StringComparison.Ordinal);

            var args = AdminPrompt.Arguments("Включить брандмауэр", SecurityFixes.Commands(FixKind.Firewall, page.Vm.Report!));
            page.Mac.Fake.On("/usr/bin/osascript", args, new CommandResult(0, "", ""));
            Dialogs.ConfirmHandler = _ => Task.FromResult(true);
            await page.Vm.FixAsync(page.Item("firewall"));
            Assert.Equal("Брандмауэр включён.", page.Vm.LastResult);
        }
        finally
        {
            Dialogs.UseSilentMode();
        }
    }

    [Fact]
    public async Task Hosts_DangerousLinesAreTicked()
    {
        using var page = new Page(infected: true, part: SecurityPart.Network);
        await page.LoadAsync();
        Assert.True(page.Vm.HasHostsLines);
        Assert.Equal(new[] { true, true, true }, page.Vm.HostsLines.Select(l => l.IsSelected));
        Assert.Equal("Закомментировать отмеченные строки", page.Item("hosts").FixText);
        foreach (var line in page.Vm.HostsLines)
            line.IsSelected = false;
        try
        {
            var asked = 0;
            Dialogs.ConfirmHandler = _ =>
            {
                asked++;
                return Task.FromResult(true);
            };
            await page.Vm.FixAsync(page.Item("hosts"));
            Assert.Equal(0, asked);
            Assert.DoesNotContain(page.Mac.Fake.Calls, c => c.StartsWith("/usr/bin/osascript", StringComparison.Ordinal));
        }
        finally
        {
            Dialogs.UseSilentMode();
        }
    }

    [Fact]
    public async Task TheNetworkTab_ShowsOnlyTheNetworkChecks_AndTheHostsLines()
    {
        using var page = new Page(infected: true, part: SecurityPart.Network);
        await page.LoadAsync();

        Assert.Equal("Network", page.Vm.Key);
        Assert.Equal("Сеть", page.Vm.Title);
        Assert.Equal(new[] { CheckGroup.Network }, page.Vm.Groups.Select(g => g.Group));
        Assert.True(page.Vm.HasHostsLines);
    }

    [Fact]
    public async Task TheNetworkTab_ShowsTheRunnersOwnHostsLine_ForInformation()
    {
        using var page = new Page(part: SecurityPart.Network);
        await page.LoadAsync();

        // The runner's own line ("192.168.64.2 Test Mac.local") is shown for information, not ticked.
        var own = Assert.Single(page.Vm.HostsLines);
        Assert.Equal((SecurityStatus.Info, false), (own.Risk, own.IsSelected));
    }

    [Fact]
    public async Task TheProtectionTab_HasNoHostsLines_AndIsCalledSecurity()
    {
        using var page = new Page(infected: true);
        await page.LoadAsync();

        Assert.Equal("Security", page.Vm.Key);
        Assert.Equal("Безопасность", page.Vm.Title);
        Assert.False(page.Vm.HasHostsLines);
    }
}
