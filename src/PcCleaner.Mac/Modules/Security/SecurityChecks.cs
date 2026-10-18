using PcCleaner.Core;

namespace PcCleaner.Modules.Security;

public enum CheckGroup
{
    MacOs,
    Access,
    Profiles,
    Network,
}

/// <summary>Changes the app can make itself, through the macOS password window.</summary>
public enum FixKind
{
    None,
    Gatekeeper,
    Firewall,
    Stealth,
    Proxy,
    Dns,
    Hosts,
}

/// <summary>
/// One check of «Безопасность и сеть»: how it looks, what to tell the user, and what can be done — a fix through the
/// password window, a System Settings pane, or only an instruction.
/// </summary>
/// <param name="Severity">The dashboard finding when the check is not OK (null: nothing to report).</param>
public sealed record SecurityCheck(string Id, CheckGroup Group, string Title, SecurityStatus Status, string Summary, string Details, IssueSeverity? Severity = null)
{
    public FixKind Fix { get; init; }

    public string? FixText { get; init; }

    public string? SettingsLink { get; init; }

    public string? SettingsText { get; init; }

    public bool IsProblem => Severity is not null && Status is SecurityStatus.Warning or SecurityStatus.Danger or SecurityStatus.Info;
}

/// <summary>Everything one check run found, with the raw facts the fixes need.</summary>
public sealed record SecurityReport(
    IReadOnlyList<SecurityCheck> Checks,
    IReadOnlyList<ServiceProxies> Proxies,
    IReadOnlyList<(string Service, IReadOnlyList<string> Servers)> ManualDns,
    string? HostsText,
    IReadOnlyList<HostsEntry> HostsEntries,
    IReadOnlyList<ConfigProfile> Profiles,
    int? MacOsMajor = null)
{
    public SecurityCheck? Find(string id) => Checks.FirstOrDefault(c => c.Id == id);
}

/// <summary>Panes of System Settings for the checks (legacy ids that macOS 13+ still opens).</summary>
public static class SecurityLinks
{
    public const string FileVault = "x-apple.systempreferences:com.apple.preference.security?FileVault";
    public const string SoftwareUpdate = "x-apple.systempreferences:com.apple.preferences.softwareupdate";
    public const string Users = "x-apple.systempreferences:com.apple.preferences.users";
    public const string Sharing = "x-apple.systempreferences:com.apple.preferences.sharing";
    public const string Profiles = "x-apple.systempreferences:com.apple.preferences.configurationprofiles";
    public const string Network = "x-apple.systempreferences:com.apple.preference.network";
    public const string Firewall = "x-apple.systempreferences:com.apple.preference.security?Firewall";
}
