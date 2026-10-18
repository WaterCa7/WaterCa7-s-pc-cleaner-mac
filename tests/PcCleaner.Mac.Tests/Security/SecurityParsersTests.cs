using PcCleaner.Core;
using PcCleaner.Mac.Tests.Support;
using PcCleaner.Modules.Security;
using Xunit;

namespace PcCleaner.Mac.Tests.Security;

public class SecurityParsersTests
{
    [Theory]
    [InlineData("security/csrutil-disabled.txt", false)]
    [InlineData(null, true)]
    public void Sip(string? fixture, bool expected)
        => Assert.Equal(expected, SecurityParsers.Sip(fixture is null ? "System Integrity Protection status: enabled.\n" : Fixture.Text(fixture)));

    [Fact]
    public void Sip_CustomAndOdd()
    {
        Assert.True(SecurityParsers.Sip("System Integrity Protection status: enabled (Custom Configuration).\n\nConfiguration:\n"));
        Assert.Null(SecurityParsers.Sip("csrutil: failed to read"));
        Assert.Null(SecurityParsers.Sip("System Integrity Protection status: unknown (Custom Configuration)."));
    }

    [Fact]
    public void Gatekeeper_FileVault_Firewall()
    {
        Assert.True(SecurityParsers.Gatekeeper(Fixture.Text("security/spctl-enabled.txt")));
        Assert.False(SecurityParsers.Gatekeeper("assessments disabled\n"));
        Assert.Null(SecurityParsers.Gatekeeper(""));

        Assert.False(SecurityParsers.FileVault(Fixture.Text("security/fdesetup-off.txt")));
        Assert.True(SecurityParsers.FileVault("FileVault is On.\n"));
        Assert.True(SecurityParsers.FileVault("Encryption in progress: Percent completed = 12.3\n"));
        Assert.Null(SecurityParsers.FileVault("Error: some error"));

        Assert.False(SecurityParsers.Firewall(Fixture.Text("security/firewall-off.txt")));
        Assert.True(SecurityParsers.Firewall("Firewall is enabled. (State = 1)\n"));
        Assert.True(SecurityParsers.Firewall("Firewall is blocking all non-essential incoming connections. (State = 2)"));

        Assert.False(SecurityParsers.Stealth(Fixture.Text("security/stealth-off-15.txt")));
        Assert.False(SecurityParsers.Stealth(Fixture.Text("security/stealth-off-14.txt")));
        Assert.True(SecurityParsers.Stealth("Firewall stealth mode is on\n"));
        Assert.True(SecurityParsers.Stealth("Stealth mode enabled \n"));
    }

    [Fact]
    public void SoftwareUpdates_FromBothMacs()
    {
        var on26 = SecurityParsers.SoftwareUpdates(Fixture.Text("security/softwareupdate-26.txt"));
        Assert.Equal(new[] { "Safari", "macOS Tahoe 26.7.1", "macOS 27.0.1" }, on26.Select(u => u.Title));
        Assert.Equal((true, true, 27), (on26[2].Recommended, on26[2].NeedsRestart, on26[2].MacOsMajor));
        Assert.Equal((false, (int?)null), (on26[0].NeedsRestart, on26[0].MacOsMajor));
        Assert.Equal(2, SecurityParsers.SoftwareUpdates(Fixture.Text("security/softwareupdate-14.txt")).Count);
        Assert.Empty(SecurityParsers.SoftwareUpdates("Software Update Tool\n\nFinding available software\nNo new software available.\n"));

        var rsr = SecurityParsers.SoftwareUpdates("* Label: Background Security Improvement 26.7.1 (a)-25G241a\n\tTitle: Background Security Improvement 26.7.1 (a), Version: 26.7.1 (a), Size: 30000KiB, Recommended: YES, Action: restart, \n");
        Assert.True(Assert.Single(rsr).IsSecurity);
    }

    [Fact]
    public void LoginWindow_AndUpdatePrefs()
    {
        var login = SecurityParsers.LoginWindow(Plist.ParseDict(Fixture.Text("security/loginwindow-autologin.plist")));
        Assert.Equal(new LoginWindowPrefs(AutoLogin: true, GuestEnabled: false), login);

        var off = SecurityParsers.UpdatePrefs(Plist.ParseDict("""
            <plist version="1.0"><dict>
            <key>AutomaticCheckEnabled</key><true/><key>CriticalUpdateInstall</key><false/><key>ConfigDataInstall</key><true/>
            <key>AutomaticDownload</key><false/>
            </dict></plist>
            """));
        Assert.Equal(new UpdatePrefs(true, false, false, false), off);
        Assert.Equal(new UpdatePrefs(true, true, true, false), SecurityParsers.UpdatePrefs(Plist.ParseDict("<plist><dict/></plist>")));
    }

    [Fact]
    public void XProtect_AndEnrollment()
    {
        Assert.Equal(new XProtectInfo("5287", new DateTime(2026, 10, 13, 4, 34, 7, DateTimeKind.Utc)),
            SecurityParsers.XProtect(Fixture.Text("security/xprotect-version-26.txt")));
        Assert.Equal(new XProtectInfo("5287", null), SecurityParsers.XProtect("5287\n"));
        Assert.Null(SecurityParsers.XProtect(""));
        Assert.Equal(new UpdatePrefs(true, false, false, false),
            SecurityParsers.UpdatePrefs(Plist.ParseDict(Fixture.Text("security/softwareupdate-prefs-off.plist"))));
        Assert.False(SecurityParsers.MdmEnrolled(Fixture.Text("security/enrollment-none.txt")));
        Assert.False(SecurityParsers.MdmEnrolled("Enrolled via DEP: No\nMDM enrollment: No\n"));
        Assert.True(SecurityParsers.MdmEnrolled("Enrolled via DEP: Yes\nMDM enrollment: Yes (User Approved)\nMDM server: https://mdm.example.com\n"));
        Assert.Null(SecurityParsers.MdmEnrolled("profiles: error"));
    }

    [Theory]
    [InlineData(27, new int[0], true)]
    [InlineData(26, new int[0], true)]
    [InlineData(15, new int[0], true)]
    [InlineData(14, new int[0], false)]
    [InlineData(15, new[] { 28 }, false)]
    [InlineData(28, new int[0], true)]
    public void MacOsSupport(int current, int[] offered, bool supported)
        => Assert.Equal(supported, MacOsVersions.IsSupported(current, offered));

    [Fact]
    public void MacOsSupport_List() => Assert.Equal(new[] { 27, 26, 15 }, MacOsVersions.Supported(14, new[] { 27 }));

    [MacFact]
    public async Task RealMac_ToolsAnswerInTheExpectedFormat()
    {
        var runner = new ProcessCommandRunner();
        Assert.NotNull(SecurityParsers.Sip((await runner.RunAsync("/usr/bin/csrutil", new[] { "status" })).StdOut));
        Assert.NotNull(SecurityParsers.Gatekeeper((await runner.RunAsync("/usr/sbin/spctl", new[] { "--status" })).StdOut));
        Assert.NotNull(SecurityParsers.FileVault((await runner.RunAsync("/usr/bin/fdesetup", new[] { "status" })).StdOut));
        Assert.NotNull(SecurityParsers.Firewall((await runner.RunAsync("/usr/libexec/ApplicationFirewall/socketfilterfw", new[] { "--getglobalstate" })).StdOut));
        Assert.NotNull(SecurityParsers.Stealth((await runner.RunAsync("/usr/libexec/ApplicationFirewall/socketfilterfw", new[] { "--getstealthmode" })).StdOut));
    }
}
