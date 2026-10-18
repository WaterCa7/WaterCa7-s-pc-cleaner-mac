using PcCleaner.Core;
using PcCleaner.Mac.Tests.Support;
using PcCleaner.Modules.Security;
using Xunit;

namespace PcCleaner.Mac.Tests.Security;

/// <summary>A Mac as the CI runner sees it, in a temporary folder for the files the checks read.</summary>
internal sealed class SecurityMac : IDisposable
{
    public const string Firewall = "/usr/libexec/ApplicationFirewall/socketfilterfw";
    public const string Networksetup = "/usr/sbin/networksetup";
    public static readonly DateTime Now = new(2026, 10, 18, 12, 0, 0, DateTimeKind.Utc);

    private readonly TempDir _dir = new();

    public SecurityMac()
    {
        Paths = new SecurityPaths(
            _dir.File("hosts", Fixture.Text("network/hosts-default.txt")),
            _dir.File("loginwindow.plist", Fixture.Text("security/loginwindow-autologin.plist")),
            _dir.File("SoftwareUpdate.plist", Fixture.Text("security/softwareupdate-prefs-off.plist")),
            Path.Combine(_dir.Path, "XProtect", "Info.plist"));
        Fake = new FakeCommandRunner()
            .OnFixture("/usr/bin/csrutil", new[] { "status" }, "security/csrutil-disabled.txt")
            .OnFixture("/usr/sbin/spctl", new[] { "--status" }, "security/spctl-enabled.txt")
            .OnFixture("/usr/bin/fdesetup", new[] { "status" }, "security/fdesetup-off.txt")
            .OnFixture(Firewall, new[] { "--getglobalstate" }, "security/firewall-off.txt")
            .OnFixture(Firewall, new[] { "--getstealthmode" }, "security/stealth-off-15.txt")
            .On("/usr/bin/sw_vers", new[] { "-productVersion" }, new CommandResult(0, "26.6.2\n", ""))
            .OnFixture("/usr/bin/xprotect", new[] { "version" }, "security/xprotect-version-26.txt")
            .OnFixture("/bin/launchctl", new[] { "print-disabled", "system" }, "security/disabled-system.txt")
            .On("/bin/launchctl", new[] { "print", "system/com.apple.screensharing" }, new CommandResult(113, "", Fixture.Text("security/screensharing-missing.txt")))
            .OnFixture("/usr/sbin/system_profiler", new[] { "SPConfigurationProfileDataType", "-json" }, "security/profiles-none.json")
            .OnFixture("/usr/bin/profiles", new[] { "status", "-type", "enrollment" }, "security/enrollment-none.txt")
            .OnFixture(Networksetup, new[] { "-listallnetworkservices" }, "network/services.txt")
            .OnFixture(Networksetup, new[] { "-getautoproxyurl", "Ethernet" }, "network/pac-off.txt")
            .OnFixture(Networksetup, new[] { "-getwebproxy", "Ethernet" }, "network/proxy-off.txt")
            .OnFixture(Networksetup, new[] { "-getsecurewebproxy", "Ethernet" }, "network/proxy-off.txt")
            .OnFixture(Networksetup, new[] { "-getsocksfirewallproxy", "Ethernet" }, "network/proxy-off.txt")
            .OnFixture(Networksetup, new[] { "-getproxyautodiscovery", "Ethernet" }, "network/wpad-off.txt")
            .OnFixture(Networksetup, new[] { "-getdnsservers", "Ethernet" }, "network/dns-none.txt")
            .OnFixture("/usr/sbin/scutil", new[] { "--proxy" }, "network/scutil-proxy-none.txt")
            .OnFixture("/usr/sbin/system_profiler", new[] { "SPAirPortDataType", "-json" }, "network/wifi-none.json");
    }

    public SecurityPaths Paths { get; }

    public FakeCommandRunner Fake { get; }

    public SecurityService Service => new(Fake, Paths, () => Now);

    /// <summary>The network of a Mac adware got to: a proxy, a strange DNS server, hijacked hosts, a profile, open Wi-Fi.</summary>
    public SecurityMac Infected()
    {
        Fake.OnFixture(Networksetup, new[] { "-getwebproxy", "Ethernet" }, "network/proxy-on.txt")
            .OnFixture(Networksetup, new[] { "-getdnsservers", "Ethernet" }, "network/dns-manual.txt")
            .OnFixture("/usr/sbin/system_profiler", new[] { "SPConfigurationProfileDataType", "-json" }, "security/profiles-mdm.json")
            .OnFixture("/usr/sbin/system_profiler", new[] { "SPAirPortDataType", "-json" }, "network/wifi-open.json");
        File.WriteAllText(Paths.Hosts, Fixture.Text("network/hosts-hijack.txt"));
        return this;
    }

    public void Dispose() => _dir.Dispose();
}

public class SecurityServiceTests
{
    [Fact]
    public async Task CiMac_AsTheRunnerIs()
    {
        using var mac = new SecurityMac();
        var report = await mac.Service.CheckAsync(CancellationToken.None);
        var status = report.Checks.ToDictionary(c => c.Id, c => c.Status);
        Assert.Equal(SecurityStatus.Danger, status["sip"]);
        Assert.Equal(SecurityStatus.Ok, status["gatekeeper"]);
        Assert.Equal(SecurityStatus.Warning, status["filevault"]);
        Assert.Equal(SecurityStatus.Warning, status["firewall"]);
        Assert.Equal(SecurityStatus.Info, status["stealth"]);
        Assert.Equal(SecurityStatus.Warning, status["autoupdate"]);
        Assert.Equal(SecurityStatus.Ok, status["macos"]);
        Assert.Equal(SecurityStatus.Ok, status["xprotect"]);
        Assert.Equal(SecurityStatus.Warning, status["autologin"]);
        Assert.Equal(SecurityStatus.Ok, status["guest"]);
        Assert.Equal(SecurityStatus.Warning, status["ssh"]);
        Assert.Equal(SecurityStatus.Ok, status["screensharing"]);
        Assert.Equal(SecurityStatus.Ok, status["profiles"]);
        Assert.Equal(SecurityStatus.Ok, status["proxy"]);
        Assert.Equal(SecurityStatus.Ok, status["dns"]);
        Assert.Equal(SecurityStatus.Ok, status["hosts"]);
        Assert.Equal(SecurityStatus.Ok, status["wifi"]);

        var firewall = report.Find("firewall")!;
        Assert.Equal((FixKind.Firewall, IssueSeverity.Medium), (firewall.Fix, firewall.Severity));
        Assert.Equal(IssueSeverity.Critical, report.Find("sip")!.Severity);
        Assert.Equal(SecurityLinks.FileVault, report.Find("filevault")!.SettingsLink);
        Assert.Contains("установка ответов безопасности", report.Find("autoupdate")!.Summary, StringComparison.Ordinal);
        Assert.Contains("версия 5287", report.Find("xprotect")!.Summary, StringComparison.Ordinal);
        Assert.DoesNotContain(mac.Fake.Calls, c => c.Contains(" -set", StringComparison.Ordinal) || c.Contains("--set", StringComparison.Ordinal));
    }

    [Fact]
    public async Task InfectedNetwork_IsFound()
    {
        using var mac = new SecurityMac().Infected();
        var report = await mac.Service.CheckAsync(CancellationToken.None);

        var proxy = report.Find("proxy")!;
        Assert.Equal((SecurityStatus.Warning, FixKind.Proxy, IssueSeverity.High), (proxy.Status, proxy.Fix, proxy.Severity));
        Assert.StartsWith("Ethernet: HTTP 127.0.0.1:8080.", proxy.Details, StringComparison.Ordinal);

        var dns = report.Find("dns")!;
        Assert.Equal((SecurityStatus.Warning, FixKind.Dns), (dns.Status, dns.Fix));
        Assert.StartsWith("Ethernet: 45.90.28.10.", dns.Details, StringComparison.Ordinal);
        Assert.Equal(("Ethernet", 3), (report.ManualDns[0].Service, report.ManualDns[0].Servers.Count));

        var hosts = report.Find("hosts")!;
        Assert.Equal((SecurityStatus.Danger, FixKind.Hosts, "подозрительных строк: 3"), (hosts.Status, hosts.Fix, hosts.Summary));
        Assert.NotNull(report.HostsText);

        Assert.Equal(SecurityStatus.Warning, report.Find("wifi")!.Status);
        Assert.DoesNotContain(report.Checks, c => c.Details.Contains("Cafe", StringComparison.Ordinal) || c.Summary.Contains("Cafe", StringComparison.Ordinal));

        var profile = report.Find("profile:Search Settings")!;
        Assert.Equal((SecurityStatus.Danger, IssueSeverity.High), (profile.Status, profile.Severity));
        Assert.Equal((SecurityStatus.Info, "Mac управляется «Acme Corp»"), (report.Find("mdm")!.Status, report.Find("mdm")!.Summary));
        Assert.Null(report.Find("profiles"));
    }

    [Fact]
    public async Task ToolsThatDoNotAnswer_AreUnknown_NotFailures()
    {
        using var mac = new SecurityMac();
        var empty = new SecurityService(new FakeCommandRunner(), mac.Paths, () => SecurityMac.Now);
        var report = await empty.CheckAsync(CancellationToken.None);
        Assert.Equal(SecurityStatus.Unknown, report.Find("sip")!.Status);
        Assert.Equal(SecurityStatus.Unknown, report.Find("macos")!.Status);
        Assert.Equal(SecurityStatus.Unknown, report.Find("xprotect")!.Status);
        Assert.StartsWith("Не удалось проверить", report.Find("gatekeeper")!.Details, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OldXProtect_FromTheBundleDate()
    {
        using var mac = new SecurityMac();
        mac.Fake.On("/usr/bin/xprotect", new[] { "version" }, new CommandResult(127, "", "not found"));
        Directory.CreateDirectory(Path.GetDirectoryName(mac.Paths.XProtectPlist)!);
        File.WriteAllText(mac.Paths.XProtectPlist, "<plist/>");
        File.SetLastWriteTimeUtc(mac.Paths.XProtectPlist, SecurityMac.Now.AddDays(-90));
        var check = (await mac.Service.CheckAsync(CancellationToken.None)).Find("xprotect")!;
        Assert.Equal((SecurityStatus.Info, IssueSeverity.Low), (check.Status, check.Severity));
        Assert.StartsWith("Базы не обновлялись 90 дн.", check.Details, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Updates()
    {
        using var mac = new SecurityMac();
        mac.Fake.OnFixture("/usr/sbin/softwareupdate", new[] { "-l" }, "security/softwareupdate-26.txt");
        var check = await mac.Service.UpdatesAsync(CancellationToken.None);
        Assert.Equal((SecurityStatus.Warning, IssueSeverity.High, "есть обновления безопасности"), (check.Status, check.Severity, check.Summary));

        mac.Fake.OnFixture("/usr/sbin/softwareupdate", new[] { "-l" }, "security/softwareupdate-14.txt")
            .On("/usr/bin/sw_vers", new[] { "-productVersion" }, new CommandResult(0, "14.8.2\n", ""));
        var on14 = await mac.Service.UpdatesAsync(CancellationToken.None);
        Assert.Equal((SecurityStatus.Info, IssueSeverity.Medium), (on14.Status, on14.Severity));

        mac.Fake.On("/usr/sbin/softwareupdate", new[] { "-l" }, new CommandResult(-1, "", "", TimedOut: true));
        Assert.Equal(SecurityStatus.Unknown, (await mac.Service.UpdatesAsync(CancellationToken.None)).Status);
        mac.Fake.On("/usr/sbin/softwareupdate", new[] { "-l" }, new CommandResult(0, "Software Update Tool\n\nFinding available software\nNo new software available.\n", ""));
        Assert.Equal(SecurityStatus.Ok, (await mac.Service.UpdatesAsync(CancellationToken.None)).Status);
    }

    [Fact]
    public void MacOsVersion()
    {
        Assert.Equal(SecurityStatus.Ok, SecurityService.MacOsCheck(26, Array.Empty<int>()).Status);
        var old = SecurityService.MacOsCheck(14, Array.Empty<int>());
        Assert.Equal((SecurityStatus.Warning, IssueSeverity.High), (old.Status, old.Severity));
        Assert.Contains("(27, 26, 15)", old.Details, StringComparison.Ordinal);
    }

    [MacFact]
    public async Task RealMac_AllChecksAnswer()
    {
        var report = await new SecurityService(new ProcessCommandRunner()).CheckAsync(CancellationToken.None);
        Assert.True(report.Checks.Count >= 17, string.Join(", ", report.Checks.Select(c => c.Id)));
        var unknown = report.Checks.Where(c => c.Status == SecurityStatus.Unknown).Select(c => $"{c.Id}: {c.Details}").ToList();
        // Only XProtect may lack a date on old systems; everything else must be read.
        Assert.True(unknown.All(u => u.StartsWith("xprotect", StringComparison.Ordinal)), string.Join("; ", unknown));
    }
}
