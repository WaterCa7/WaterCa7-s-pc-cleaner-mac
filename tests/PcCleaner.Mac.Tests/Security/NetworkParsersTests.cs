using PcCleaner.Core;
using PcCleaner.Mac.Tests.Support;
using PcCleaner.Modules.Security;
using Xunit;

namespace PcCleaner.Mac.Tests.Security;

public class NetworkParsersTests
{
    [Fact]
    public void Services_AndRouter()
    {
        Assert.Equal(new[] { new NetworkServiceName("Ethernet", true) }, NetworkParsers.Services(Fixture.Text("network/services.txt")));
        Assert.Equal(new[] { new NetworkServiceName("Wi-Fi", true), new NetworkServiceName("Thunderbolt Bridge", false) },
            NetworkParsers.Services("An asterisk (*) denotes that a network service is disabled.\nWi-Fi\n*Thunderbolt Bridge\n"));
        Assert.Equal("192.168.64.1", NetworkParsers.Router(Fixture.Text("network/info-dhcp.txt")));
        Assert.Null(NetworkParsers.Router("Manual Configuration\nRouter: none\n"));
    }

    [Fact]
    public void Proxies()
    {
        Assert.Equal(ProxySetting.Off, NetworkParsers.Proxy(Fixture.Text("network/proxy-off.txt")));
        var on = NetworkParsers.Proxy(Fixture.Text("network/proxy-on.txt"));
        Assert.Equal((true, "127.0.0.1:8080"), (on.Enabled, on.Text));
        Assert.Equal(((string?)null, false), NetworkParsers.Pac(Fixture.Text("network/pac-off.txt")));
        Assert.Equal(("http://proxy.example.com/proxy.pac", true), NetworkParsers.Pac(Fixture.Text("network/pac-on.txt")));
        Assert.False(NetworkParsers.AutoDiscovery(Fixture.Text("network/wpad-off.txt")));
        Assert.True(NetworkParsers.AutoDiscovery("Auto Proxy Discovery: On\n"));
    }

    [Fact]
    public void ScutilProxy_TopLevelOnly()
    {
        Assert.False(NetworkParsers.ScutilProxy(Fixture.Text("network/scutil-proxy-none.txt")).Any);
        var on = NetworkParsers.ScutilProxy(Fixture.Text("network/scutil-proxy-on.txt"));
        Assert.True(on.Any);
        Assert.Equal("HTTP 127.0.0.1:8080, HTTPS 127.0.0.1:8080, автонастройка (PAC) http://proxy.example.com/proxy.pac", on.Text);
    }

    [Fact]
    public void Dns()
    {
        Assert.Empty(NetworkParsers.DnsServers(Fixture.Text("network/dns-none.txt")));
        var manual = NetworkParsers.DnsServers(Fixture.Text("network/dns-manual.txt"));
        Assert.Equal(new[] { "45.90.28.10", "1.1.1.1", "192.168.1.53" }, manual);
        Assert.Equal(new[] { true, false, false }, manual.Select(DnsRules.IsUnexpected));
        Assert.Equal("Cloudflare", DnsRules.KnownName("1.1.1.1"));
        Assert.Equal("Google", DnsRules.KnownName("2001:4860:4860:0:0:0:0:8888"));
        Assert.True(DnsRules.IsLocal("100.100.100.100"));
        Assert.True(DnsRules.IsLocal("fd00::1"));
        Assert.False(DnsRules.IsLocal("8.8.8.8"));
    }

    [Theory]
    [InlineData("network/wifi-none.json", WifiSecurity.NotConnected)]
    [InlineData("network/wifi-open.json", WifiSecurity.Open)]
    [InlineData("network/wifi-wep.json", WifiSecurity.Wep)]
    [InlineData("network/wifi-wpa2.json", WifiSecurity.Protected)]
    public void Wifi(string fixture, WifiSecurity expected) => Assert.Equal(expected, NetworkParsers.Wifi(Fixture.Text(fixture)));

    [Fact]
    public void Profiles()
    {
        Assert.Empty(ProfileParsers.Parse(Fixture.Text("security/profiles-none.json")));
        var profiles = ProfileParsers.Parse(Fixture.Text("security/profiles-mdm.json"));
        Assert.Equal(new[] { "Acme Device Management", "Search Settings" }, profiles.Select(p => p.Name));
        Assert.Equal((true, false, "Acme Corp"), (profiles[0].IsMdm, profiles[0].IsRisky, profiles[0].Organization));
        Assert.Equal((false, true, "прокси, настройки браузера"), (profiles[1].IsMdm, profiles[1].IsRisky, profiles[1].WhatText));
        Assert.Empty(ProfileParsers.Parse("не json"));
    }

    [Fact]
    public void Hosts_DefaultIsClean_HijackIsFound()
    {
        Assert.All(HostsFile.Read(Fixture.Text("network/hosts-default.txt")), e => Assert.True(e.Risk.Rank() <= SecurityStatus.Info.Rank(), e.RawLine));
        var entries = HostsFile.Read(Fixture.Text("network/hosts-hijack.txt"));
        var byHost = entries.Where(e => e.Hostnames[0] != "localhost").ToDictionary(e => e.Hostnames[0]);
        Assert.Equal(SecurityStatus.Warning, byHost["swscan.apple.com"].Risk);
        Assert.Contains("Обновление ПО macOS", byHost["swscan.apple.com"].Reason, StringComparison.Ordinal);
        Assert.Equal(SecurityStatus.Warning, byHost["ocsp.apple.com"].Risk);
        Assert.Equal(SecurityStatus.Danger, byHost["online.sberbank.ru"].Risk);
        Assert.Equal(SecurityStatus.Ok, byHost["ads.example.com"].Risk);
    }

    [Fact]
    public void Hosts_CommentOut_KeepsEverythingElse()
    {
        var text = "127.0.0.1 localhost\r\n0.0.0.0 swscan.apple.com\r\n# own note\r\n185.10.10.10 online.sberbank.ru";
        var entries = HostsFile.Read(text);
        var result = HostsFile.CommentOut(text, entries.Where(e => e.Risk.Rank() >= SecurityStatus.Warning.Rank()).ToList());
        Assert.Equal("127.0.0.1 localhost\r\n# 0.0.0.0 swscan.apple.com\r\n# own note\r\n# 185.10.10.10 online.sberbank.ru", result);
        Assert.Throws<HostsChangedException>(() => HostsFile.CommentOut(text.Replace("swscan", "swdist"), entries));
    }

    [MacFact]
    public async Task RealMac_NetworkToolsAnswer()
    {
        var runner = new ProcessCommandRunner();
        var services = NetworkParsers.Services((await runner.RunAsync("/usr/sbin/networksetup", new[] { "-listallnetworkservices" })).StdOut);
        Assert.NotEmpty(services);
        var proxy = await runner.RunAsync("/usr/sbin/networksetup", new[] { "-getwebproxy", services[0].Name });
        Assert.Contains("Enabled:", proxy.StdOut, StringComparison.Ordinal);
        NetworkParsers.ScutilProxy((await runner.RunAsync("/usr/sbin/scutil", new[] { "--proxy" })).StdOut);
        Assert.NotNull(HostsFile.Read(await File.ReadAllTextAsync(HostsFile.DefaultPath)));
    }
}
