using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;

namespace PcCleaner.Modules.Security;

/// <summary>A proxy of one kind as `networksetup -get…proxy` reports it.</summary>
public sealed record ProxySetting(bool Enabled, string Server, int Port)
{
    public static readonly ProxySetting Off = new(false, "", 0);

    public string Text => Port > 0 ? $"{Server}:{Port.ToString(CultureInfo.InvariantCulture)}" : Server;
}

/// <summary>Everything that sends a network service's traffic through a proxy.</summary>
public sealed record ServiceProxies(string Service, ProxySetting Web, ProxySetting Secure, ProxySetting Socks, string? PacUrl, bool PacEnabled, bool AutoDiscovery)
{
    public bool Any => Web.Enabled || Secure.Enabled || Socks.Enabled || PacEnabled;

    /// <summary>"HTTP 127.0.0.1:8080, HTTPS …, PAC http://…".</summary>
    public string Text
    {
        get
        {
            var parts = new List<string>();
            if (Web.Enabled)
                parts.Add("HTTP " + Web.Text);
            if (Secure.Enabled)
                parts.Add("HTTPS " + Secure.Text);
            if (Socks.Enabled)
                parts.Add("SOCKS " + Socks.Text);
            if (PacEnabled)
                parts.Add("автонастройка (PAC) " + (PacUrl ?? ""));
            return string.Join(", ", parts);
        }
    }
}

/// <summary>A network service (Wi-Fi, Ethernet…) from `networksetup -listallnetworkservices`.</summary>
public sealed record NetworkServiceName(string Name, bool Enabled);

/// <summary>How the current Wi-Fi network is protected (its name is never kept).</summary>
public enum WifiSecurity
{
    NotConnected,
    Open,
    Wep,
    Protected,
}

/// <summary>Pure parsers of `networksetup`, `scutil` and `system_profiler SPAirPortDataType`.</summary>
public static class NetworkParsers
{
    /// <summary>`networksetup -listallnetworkservices`: the first line explains the "*" (disabled).</summary>
    public static IReadOnlyList<NetworkServiceName> Services(string text)
        => text.Split('\n').Select(l => l.TrimEnd('\r'))
            .Where(l => l.Length > 0 && !l.StartsWith("An asterisk", StringComparison.Ordinal))
            .Select(l => l.StartsWith('*') ? new NetworkServiceName(l[1..], false) : new NetworkServiceName(l, true))
            .ToList();

    /// <summary>`networksetup -getwebproxy|-getsecurewebproxy|-getsocksfirewallproxy &lt;service&gt;`.</summary>
    public static ProxySetting Proxy(string text)
    {
        var values = Pairs(text);
        var enabled = values.GetValueOrDefault("Enabled") == "Yes";
        var port = int.TryParse(values.GetValueOrDefault("Port"), NumberStyles.None, CultureInfo.InvariantCulture, out var p) ? p : 0;
        return new ProxySetting(enabled, values.GetValueOrDefault("Server") ?? "", port);
    }

    /// <summary>`networksetup -getautoproxyurl`: "URL: (null)" / "Enabled: No".</summary>
    public static (string? Url, bool Enabled) Pac(string text)
    {
        var values = Pairs(text);
        var url = values.GetValueOrDefault("URL");
        return (string.IsNullOrEmpty(url) || url == "(null)" ? null : url, values.GetValueOrDefault("Enabled") == "Yes");
    }

    /// <summary>`networksetup -getproxyautodiscovery`: "Auto Proxy Discovery: On|Off".</summary>
    public static bool AutoDiscovery(string text) => Pairs(text).GetValueOrDefault("Auto Proxy Discovery") == "On";

    /// <summary>`networksetup -getdnsservers`: the addresses set by hand ("There aren't any DNS Servers set…" → none).</summary>
    public static IReadOnlyList<string> DnsServers(string text)
        => text.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0 && IPAddress.TryParse(l, out _)).ToList();

    /// <summary>`networksetup -getinfo`: the router address, if any.</summary>
    public static string? Router(string text)
        => Pairs(text).GetValueOrDefault("Router") is { } router && IPAddress.TryParse(router, out _) ? router : null;

    /// <summary>`scutil --proxy`: the proxies macOS uses now (all services merged).</summary>
    public static ServiceProxies ScutilProxy(string text)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            var colon = line.IndexOf(" : ", StringComparison.Ordinal);
            if (colon > 0 && !line.Contains('{', StringComparison.Ordinal))
                values.TryAdd(line[..colon].Trim(), line[(colon + 3)..].Trim());
        }
        ProxySetting Read(string prefix) => new(
            values.GetValueOrDefault(prefix + "Enable") == "1",
            values.GetValueOrDefault(prefix + "Proxy") ?? "",
            int.TryParse(values.GetValueOrDefault(prefix + "Port"), NumberStyles.None, CultureInfo.InvariantCulture, out var port) ? port : 0);
        return new ServiceProxies("", Read("HTTP"), Read("HTTPS"), Read("SOCKS"), values.GetValueOrDefault("ProxyAutoConfigURLString"),
            values.GetValueOrDefault("ProxyAutoConfigEnable") == "1", values.GetValueOrDefault("ProxyAutoDiscoveryEnable") == "1");
    }

    /// <summary>The security of the Wi-Fi network the Mac is connected to (`system_profiler SPAirPortDataType -json`).</summary>
    public static WifiSecurity Wifi(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            foreach (var mode in FindStrings(doc.RootElement, "spairport_current_network_information", "spairport_security_mode"))
            {
                var m = mode.ToLowerInvariant();
                if (m.EndsWith("_none", StringComparison.Ordinal) || m.EndsWith("_open", StringComparison.Ordinal))
                    return WifiSecurity.Open;
                if (m.Contains("wep", StringComparison.Ordinal))
                    return WifiSecurity.Wep;
                return WifiSecurity.Protected;
            }
        }
        catch (JsonException)
        {
            // Not JSON: as if not connected.
        }
        return WifiSecurity.NotConnected;
    }

    private static IEnumerable<string> FindStrings(JsonElement element, string container, string key)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    if (property.Name == container && property.Value.ValueKind == JsonValueKind.Object &&
                        property.Value.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String)
                    {
                        yield return value.GetString()!;
                    }
                    foreach (var found in FindStrings(property.Value, container, key))
                        yield return found;
                }
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    foreach (var found in FindStrings(item, container, key))
                        yield return found;
                }
                break;
        }
    }

    private static Dictionary<string, string> Pairs(string text)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            var colon = line.IndexOf(':');
            if (colon > 0)
                values.TryAdd(line[..colon].Trim(), line[(colon + 1)..].Trim());
        }
        return values;
    }
}

/// <summary>Which DNS servers are expected: home network addresses and well-known public resolvers.</summary>
public static class DnsRules
{
    private static readonly Dictionary<string, string> KnownPublic = new(StringComparer.OrdinalIgnoreCase)
    {
        ["8.8.8.8"] = "Google", ["8.8.4.4"] = "Google", ["2001:4860:4860::8888"] = "Google", ["2001:4860:4860::8844"] = "Google",
        ["1.1.1.1"] = "Cloudflare", ["1.0.0.1"] = "Cloudflare", ["1.1.1.2"] = "Cloudflare", ["1.0.0.2"] = "Cloudflare",
        ["1.1.1.3"] = "Cloudflare", ["1.0.0.3"] = "Cloudflare", ["2606:4700:4700::1111"] = "Cloudflare", ["2606:4700:4700::1001"] = "Cloudflare",
        ["9.9.9.9"] = "Quad9", ["149.112.112.112"] = "Quad9", ["9.9.9.10"] = "Quad9", ["9.9.9.11"] = "Quad9",
        ["2620:fe::fe"] = "Quad9", ["2620:fe::9"] = "Quad9",
        ["208.67.222.222"] = "OpenDNS", ["208.67.220.220"] = "OpenDNS", ["208.67.222.123"] = "OpenDNS", ["208.67.220.123"] = "OpenDNS",
        ["77.88.8.8"] = "Яндекс DNS", ["77.88.8.1"] = "Яндекс DNS", ["77.88.8.88"] = "Яндекс DNS", ["77.88.8.2"] = "Яндекс DNS",
        ["77.88.8.7"] = "Яндекс DNS", ["77.88.8.3"] = "Яндекс DNS",
        ["94.140.14.14"] = "AdGuard DNS", ["94.140.15.15"] = "AdGuard DNS", ["94.140.14.15"] = "AdGuard DNS", ["94.140.15.16"] = "AdGuard DNS",
        ["94.140.14.140"] = "AdGuard DNS", ["94.140.14.141"] = "AdGuard DNS",
        ["76.76.2.0"] = "Control D", ["76.76.10.0"] = "Control D",
        ["185.228.168.9"] = "CleanBrowsing", ["185.228.169.9"] = "CleanBrowsing",
        ["8.26.56.26"] = "Comodo Secure DNS", ["8.20.247.20"] = "Comodo Secure DNS",
    };

    /// <summary>The name of a well-known public resolver, or null.</summary>
    public static string? KnownName(string address)
        => IPAddress.TryParse(address, out var ip) && KnownPublic.TryGetValue(ip.ToString(), out var name) ? name : null;

    /// <summary>A home or local address (router, Pi-hole, VPN): private, link-local, loopback, CGNAT.</summary>
    public static bool IsLocal(string address)
    {
        if (!IPAddress.TryParse(address, out var ip))
            return false;
        if (IPAddress.IsLoopback(ip))
            return true;
        if (ip.AddressFamily == AddressFamily.InterNetworkV6)
            return ip.IsIPv6LinkLocal || ip.IsIPv6UniqueLocal || ip.IsIPv6SiteLocal;
        var b = ip.GetAddressBytes();
        return b[0] == 10 || (b[0] == 172 && b[1] >= 16 && b[1] <= 31) || (b[0] == 192 && b[1] == 168) ||
               (b[0] == 169 && b[1] == 254) || (b[0] == 100 && b[1] >= 64 && b[1] <= 127);
    }

    /// <summary>Set by hand, not local and not a well-known resolver: worth asking who set it.</summary>
    public static bool IsUnexpected(string address) => !IsLocal(address) && KnownName(address) is null;
}
