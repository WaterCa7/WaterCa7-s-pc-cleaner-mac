using System.Globalization;
using System.Text.RegularExpressions;
using PcCleaner.Core;

namespace PcCleaner.Modules.Security;

/// <summary>An update `softwareupdate -l` offers.</summary>
public sealed record SoftwareUpdate(string Label, string Title, string? Version, bool Recommended, bool NeedsRestart)
{
    /// <summary>Security content: a security response, background security improvement, or an update of this macOS.</summary>
    public bool IsSecurity => Title.Contains("Security", StringComparison.OrdinalIgnoreCase)
                              || Label.Contains("Security", StringComparison.OrdinalIgnoreCase)
                              || Label.Contains("RSR", StringComparison.Ordinal);

    /// <summary>The macOS major version of a "macOS …" update (26 for "macOS Tahoe 26.7.1"), else null.</summary>
    public int? MacOsMajor
    {
        get
        {
            if (!Title.StartsWith("macOS", StringComparison.Ordinal) || Version is null)
                return null;
            var dot = Version.IndexOf('.');
            return int.TryParse(dot > 0 ? Version[..dot] : Version, NumberStyles.None, CultureInfo.InvariantCulture, out var major) ? major : null;
        }
    }
}

/// <summary>Automatic update settings (/Library/Preferences/com.apple.SoftwareUpdate.plist); a missing key means "on".</summary>
public sealed record UpdatePrefs(bool AutomaticCheck, bool SecurityResponses, bool AutomaticDownload, bool InstallMacOs);

/// <summary>The XProtect signatures: version and when they were installed (UTC), when known.</summary>
public sealed record XProtectInfo(string? Version, DateTime? InstalledUtc);

/// <summary>/Library/Preferences/com.apple.loginwindow.plist: who logs in without a password, the guest account.</summary>
public sealed record LoginWindowPrefs(bool AutoLogin, bool GuestEnabled);

/// <summary>Pure parsers of the macOS security tools' output (English, LC_ALL=en_US.UTF-8).</summary>
public static class SecurityParsers
{
    private static readonly Regex UpdateLabel = new(@"^\s*\*\s*Label:\s*(.+?)\s*$", RegexOptions.CultureInvariant);
    private static readonly Regex UpdateTitle = new(@"^\s*Title:\s*(.+?),\s*Version:\s*([^,]*),", RegexOptions.CultureInvariant);

    /// <summary>`csrutil status`: "System Integrity Protection status: enabled." — null when the output is not that.</summary>
    public static bool? Sip(string text)
    {
        var line = text.Split('\n').FirstOrDefault(l => l.Contains("System Integrity Protection status:", StringComparison.Ordinal));
        if (line is null)
            return null;
        // "enabled (Custom Configuration)" is still on; "disabled" or "unknown" are not.
        var status = line[(line.IndexOf(':') + 1)..].Trim().TrimEnd('.');
        return status.StartsWith("enabled", StringComparison.Ordinal) ? true : status.StartsWith("disabled", StringComparison.Ordinal) ? false : null;
    }

    /// <summary>`spctl --status`: "assessments enabled" / "assessments disabled".</summary>
    public static bool? Gatekeeper(string text) => text.Trim() switch
    {
        var t when t.StartsWith("assessments enabled", StringComparison.Ordinal) => true,
        var t when t.StartsWith("assessments disabled", StringComparison.Ordinal) => false,
        _ => null,
    };

    /// <summary>`fdesetup status`: "FileVault is On." / "FileVault is Off." (encryption in progress counts as on).</summary>
    public static bool? FileVault(string text)
    {
        var t = text.Trim();
        if (t.StartsWith("FileVault is On", StringComparison.Ordinal) || t.Contains("Encryption in progress", StringComparison.Ordinal))
            return true;
        return t.StartsWith("FileVault is Off", StringComparison.Ordinal) ? false : null;
    }

    /// <summary>`socketfilterfw --getglobalstate`: "Firewall is enabled. (State = 1)", "(State = 2)" — block all — is on too.</summary>
    public static bool? Firewall(string text)
    {
        var t = text.Trim();
        if (t.StartsWith("Firewall is enabled", StringComparison.Ordinal) || t.Contains("blocking all", StringComparison.OrdinalIgnoreCase))
            return true;
        return t.StartsWith("Firewall is disabled", StringComparison.Ordinal) ? false : null;
    }

    /// <summary>`socketfilterfw --getstealthmode`: "Firewall stealth mode is on|off" (macOS 15+) or "Stealth mode enabled|disabled".</summary>
    public static bool? Stealth(string text)
    {
        var t = text.Trim();
        if (t.EndsWith("is on", StringComparison.Ordinal) || t.StartsWith("Stealth mode enabled", StringComparison.Ordinal))
            return true;
        if (t.EndsWith("is off", StringComparison.Ordinal) || t.StartsWith("Stealth mode disabled", StringComparison.Ordinal))
            return false;
        return null;
    }

    /// <summary>`softwareupdate -l`: the offered updates ("No new software available." → none).</summary>
    public static IReadOnlyList<SoftwareUpdate> SoftwareUpdates(string text)
    {
        var result = new List<SoftwareUpdate>();
        string? label = null;
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            var l = UpdateLabel.Match(line);
            if (l.Success)
            {
                label = l.Groups[1].Value;
                continue;
            }
            var t = UpdateTitle.Match(line);
            if (t.Success && label is not null)
            {
                var version = t.Groups[2].Value.Trim();
                result.Add(new SoftwareUpdate(label, t.Groups[1].Value.Trim(), version.Length == 0 ? null : version,
                    line.Contains("Recommended: YES", StringComparison.Ordinal), line.Contains("Action: restart", StringComparison.Ordinal)));
                label = null;
            }
        }
        return result;
    }

    public static UpdatePrefs UpdatePrefs(IReadOnlyDictionary<string, object?> plist)
        => new(
            plist.GetBool("AutomaticCheckEnabled") ?? true,
            (plist.GetBool("CriticalUpdateInstall") ?? true) && (plist.GetBool("ConfigDataInstall") ?? true),
            plist.GetBool("AutomaticDownload") ?? true,
            plist.GetBool("AutomaticallyInstallMacOSUpdates") ?? false);

    public static LoginWindowPrefs LoginWindow(IReadOnlyDictionary<string, object?> plist)
        => new(!string.IsNullOrWhiteSpace(plist.GetString("autoLoginUser")), plist.GetBool("GuestEnabled") ?? false);

    /// <summary>`xprotect version` (macOS 15+): "Version: 5287 Installed: 2026-10-13 04:34:07 +0000"; null when it says nothing.</summary>
    public static XProtectInfo? XProtect(string text)
    {
        var version = Regex.Match(text, @"(?:Version:\s*)?(\d{3,})", RegexOptions.CultureInvariant);
        if (!version.Success)
            return null;
        var installed = Regex.Match(text, @"Installed:\s*(\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2} [+-]\d{4})", RegexOptions.CultureInvariant);
        return new XProtectInfo(version.Groups[1].Value,
            installed.Success ? Programs.AppParsers.ParseDate(installed.Groups[1].Value) : null);
    }

    /// <summary>`profiles status -type enrollment`: "MDM enrollment: Yes" (also "Yes (User Approved)").</summary>
    public static bool? MdmEnrolled(string text)
    {
        var line = text.Split('\n').FirstOrDefault(l => l.Contains("MDM enrollment:", StringComparison.Ordinal));
        if (line is null)
            return null;
        return line[(line.IndexOf(':') + 1)..].Trim().StartsWith("Yes", StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>Which macOS versions Apple still updates: the three newest majors in release order (… 14, 15, 26, 27 …).</summary>
public static class MacOsVersions
{
    /// <summary>The newest major the code knows of; this Mac and `softwareupdate` may know a newer one.</summary>
    public const int NewestKnown = 27;

    public static IReadOnlyList<int> Supported(int currentMajor, IEnumerable<int> offeredMajors)
    {
        var newest = new[] { NewestKnown, currentMajor }.Concat(offeredMajors).Max();
        return Releases(newest).TakeLast(3).Reverse().ToList();
    }

    public static bool IsSupported(int currentMajor, IEnumerable<int> offeredMajors) => Supported(currentMajor, offeredMajors).Contains(currentMajor);

    /// <summary>Majors in release order up to <paramref name="newest"/>: 11…15, then 26 onwards (named after the year).</summary>
    private static IEnumerable<int> Releases(int newest)
    {
        for (var major = 11; major <= Math.Min(15, newest); major++)
            yield return major;
        for (var major = 26; major <= newest; major++)
            yield return major;
    }
}
