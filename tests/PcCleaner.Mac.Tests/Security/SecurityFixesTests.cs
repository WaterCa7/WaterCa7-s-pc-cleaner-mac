using PcCleaner.Core;
using PcCleaner.Mac.Tests.Support;
using PcCleaner.Modules.Security;
using PcCleaner.Modules.Startup;
using Xunit;

namespace PcCleaner.Mac.Tests.Security;

public class SecurityFixesTests
{
    /// <summary>Answers every command with success and remembers the password-window commands (and the hosts text they copy).</summary>
    private sealed class YesRunner(int exitCode = 0, string stderr = "") : ICommandRunner
    {
        public List<IReadOnlyList<string>> AdminCalls { get; } = new();

        public string? CopiedHostsText { get; private set; }

        public Task<CommandResult> RunAsync(string exe, IReadOnlyList<string> args, TimeSpan? timeout = null, CancellationToken ct = default)
        {
            if (exe == "/usr/bin/osascript")
            {
                var data = args.Skip(AdminPrompt.Script.Length).ToList();
                AdminCalls.Add(data);
                var cp = data.IndexOf("/bin/cp");
                if (cp >= 0 && File.Exists(data[cp + 1]))
                    CopiedHostsText = File.ReadAllText(data[cp + 1]);
            }
            return Task.FromResult(new CommandResult(exitCode, "", stderr));
        }
    }

    private static async Task<SecurityReport> InfectedReport(SecurityMac mac) => await mac.Service.CheckAsync(CancellationToken.None);

    private static (SecurityFixes Fixes, BackupStore Backups) Fixes(TempDir dir, YesRunner runner, string? hostsPath = null)
    {
        var backups = new BackupStore(Path.Combine(dir.Path, "Backups"));
        return (new SecurityFixes(new AdminPrompt(runner), backups, Path.Combine(dir.Path, "tmp")), backups);
    }

    [Fact]
    public async Task Gatekeeper_DependsOnTheMacOsVersion()
    {
        using var mac = new SecurityMac();
        using var dir = new TempDir();
        var report = await InfectedReport(mac);
        Assert.Equal("--global-enable", SecurityFixes.Commands(FixKind.Gatekeeper, report)[0].Args[0]);
        Assert.Equal("--master-enable", SecurityFixes.Commands(FixKind.Gatekeeper, report with { MacOsMajor = 14 })[0].Args[0]);

        var runner = new YesRunner();
        var result = await Fixes(dir, runner).Fixes.ApplyAsync(FixKind.Gatekeeper, report, null, mac.Paths.Hosts, CancellationToken.None);
        Assert.Equal(new StartupResult(true, "Gatekeeper включён."), result);
        Assert.Equal(new[] { "Включить Gatekeeper", AdminPrompt.Separator, "/usr/sbin/spctl", "--global-enable" }, runner.AdminCalls.Single());
    }

    [Fact]
    public async Task Cancelled_ChangesNothing()
    {
        using var mac = new SecurityMac();
        using var dir = new TempDir();
        var runner = new YesRunner(1, "execution error: User canceled. (-128)");
        var result = await Fixes(dir, runner).Fixes.ApplyAsync(FixKind.Firewall, await InfectedReport(mac), null, mac.Paths.Hosts, CancellationToken.None);
        Assert.Equal(new StartupResult(false, "Отменено — ничего не изменено."), result);
    }

    [Fact]
    public async Task Proxy_CopyFirst_ThenOff()
    {
        using var mac = new SecurityMac().Infected();
        using var dir = new TempDir();
        var runner = new YesRunner();
        var (fixes, backups) = Fixes(dir, runner);
        var report = await InfectedReport(mac);
        Assert.StartsWith("Выключить прокси для: Ethernet (HTTP 127.0.0.1:8080)", SecurityFixes.Describe(FixKind.Proxy, report), StringComparison.Ordinal);

        var result = await fixes.ApplyAsync(FixKind.Proxy, report, null, mac.Paths.Hosts, CancellationToken.None);
        Assert.True(result.Ok, result.Message);
        Assert.Equal(new[] { "Выключить прокси", AdminPrompt.Separator, "/usr/sbin/networksetup", "-setwebproxystate", "Ethernet", "off" }, runner.AdminCalls.Single());
        var backup = Assert.Single(backups.List());
        Assert.Equal(BackupKind.NetworkSettings, backup.Kind);
        var saved = Assert.Single(BackupStore.ReadJson<List<NetworkSnapshot>>(backup)!);
        Assert.Equal(("Ethernet", "127.0.0.1", 8080, 3), (saved.Service, saved.Web.Server, saved.Web.Port, saved.Dns.Count));
    }

    [Fact]
    public async Task NoCopy_NoChange()
    {
        using var mac = new SecurityMac().Infected();
        using var dir = new TempDir();
        var runner = new YesRunner();
        var fixes = new SecurityFixes(new AdminPrompt(runner), new BackupStore(dir.File("not-a-folder", "x")), Path.Combine(dir.Path, "tmp"));
        var report = await InfectedReport(mac);
        foreach (var kind in new[] { FixKind.Proxy, FixKind.Dns })
        {
            var result = await fixes.ApplyAsync(kind, report, null, mac.Paths.Hosts, CancellationToken.None);
            Assert.StartsWith("Не удалось сохранить резервную копию — ничего не изменено", result.Message, StringComparison.Ordinal);
        }
        var hosts = await fixes.ApplyAsync(FixKind.Hosts, report, report.HostsEntries.Where(e => e.Risk == SecurityStatus.Danger).ToList(), mac.Paths.Hosts,
            CancellationToken.None);
        Assert.False(hosts.Ok);
        Assert.Empty(runner.AdminCalls);
    }

    [Fact]
    public async Task Dns_BackToAutomatic()
    {
        using var mac = new SecurityMac().Infected();
        using var dir = new TempDir();
        var runner = new YesRunner();
        Assert.True((await Fixes(dir, runner).Fixes.ApplyAsync(FixKind.Dns, await InfectedReport(mac), null, mac.Paths.Hosts, CancellationToken.None)).Ok);
        Assert.Equal(new[] { "Вернуть автоматический DNS", AdminPrompt.Separator, "/usr/sbin/networksetup", "-setdnsservers", "Ethernet", "Empty" },
            runner.AdminCalls.Single());
    }

    [Fact]
    public async Task Hosts_ChosenLinesCommentedOut_ThroughATemporaryCopy()
    {
        using var mac = new SecurityMac().Infected();
        using var dir = new TempDir();
        var runner = new YesRunner();
        var (fixes, backups) = Fixes(dir, runner);
        var report = await InfectedReport(mac);
        var chosen = report.HostsEntries.Where(e => e.Risk is SecurityStatus.Danger or SecurityStatus.Warning).ToList();
        var result = await fixes.ApplyAsync(FixKind.Hosts, report, chosen, mac.Paths.Hosts, CancellationToken.None);
        Assert.True(result.Ok, result.Message);

        var call = runner.AdminCalls.Single();
        Assert.Equal("/bin/cp", call[2]);
        Assert.Equal(mac.Paths.Hosts, call[4]);
        Assert.Contains("/usr/bin/dscacheutil", call);
        Assert.Contains("# 185.10.10.10 online.sberbank.ru", runner.CopiedHostsText, StringComparison.Ordinal);
        Assert.Contains("\n0.0.0.0 ads.example.com # реклама", runner.CopiedHostsText, StringComparison.Ordinal);
        Assert.False(File.Exists(call[3]));
        Assert.Equal(BackupKind.HostsFile, Assert.Single(backups.List()).Kind);
        Assert.Equal(Fixture.Text("network/hosts-hijack.txt"), File.ReadAllText(mac.Paths.Hosts));
    }

    [Fact]
    public async Task Hosts_ChangedSinceTheCheck_NothingHappens()
    {
        using var mac = new SecurityMac().Infected();
        using var dir = new TempDir();
        var runner = new YesRunner();
        var report = await InfectedReport(mac);
        File.AppendAllText(mac.Paths.Hosts, "1.2.3.4 new.example.com\n");
        var result = await Fixes(dir, runner).Fixes.ApplyAsync(FixKind.Hosts, report, report.HostsEntries.Take(1).ToList(), mac.Paths.Hosts, CancellationToken.None);
        Assert.StartsWith("Файл hosts изменился после проверки", result.Message, StringComparison.Ordinal);
        Assert.Empty(runner.AdminCalls);
    }

    [Fact]
    public async Task Restore_NetworkAndHosts()
    {
        using var mac = new SecurityMac().Infected();
        using var dir = new TempDir();
        var runner = new YesRunner();
        var (fixes, backups) = Fixes(dir, runner);
        var report = await InfectedReport(mac);
        await fixes.ApplyAsync(FixKind.Proxy, report, null, mac.Paths.Hosts, CancellationToken.None);
        await fixes.ApplyAsync(FixKind.Hosts, report, report.HostsEntries.Where(e => e.Risk == SecurityStatus.Danger).ToList(), mac.Paths.Hosts, CancellationToken.None);
        runner.AdminCalls.Clear();

        var restorer = new BackupRestorer(backups, dir.Path, 501, runner, new AdminPrompt(runner), new LoginItemsService(runner, backups));
        var records = backups.List();
        Assert.Equal("файл hosts изменён", BackupRestorer.Describe(records[0]));
        Assert.Equal("прокси выключен", BackupRestorer.Describe(records[1]));
        Assert.True((await restorer.RestoreAsync(records[1], CancellationToken.None)).Ok);
        var network = runner.AdminCalls.Single();
        Assert.Equal("Вернуть прежние настройки прокси и DNS", network[0]);
        Assert.Contains("-setwebproxy", network);
        Assert.Contains("45.90.28.10", network);

        Assert.True((await restorer.RestoreAsync(records[0], CancellationToken.None)).Ok);
        Assert.Equal(new[] { "/bin/cp", records[0].StoredFile!, mac.Paths.Hosts }, runner.AdminCalls[1].Skip(2).Take(3));
    }

    [Fact]
    public void RestoreCommands_ForASnapshot()
    {
        var snapshot = new NetworkSnapshot("Wi-Fi", new ProxySetting(true, "10.0.0.1", 3128), ProxySetting.Off, ProxySetting.Off, null, false, Array.Empty<string>());
        Assert.Equal(new[]
            {
                "-setwebproxy Wi-Fi 10.0.0.1 3128", "-setsecurewebproxystate Wi-Fi off", "-setsocksfirewallproxystate Wi-Fi off",
                "-setautoproxystate Wi-Fi off", "-setdnsservers Wi-Fi Empty",
            },
            SecurityFixes.RestoreCommands(snapshot).Select(c => string.Join(' ', c.Args)));
    }
}
