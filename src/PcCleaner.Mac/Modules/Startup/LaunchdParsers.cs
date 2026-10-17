using System.Globalization;
using System.Text.RegularExpressions;

namespace PcCleaner.Modules.Startup;

/// <summary>A line of `launchctl list`: the PID when running, the last exit status (negative: killed by a signal).</summary>
public sealed record LaunchdListItem(int? Pid, int? LastExit);

/// <summary>The top-level facts of `launchctl print &lt;domain&gt;/&lt;label&gt;`.</summary>
public sealed record LaunchdPrint(string? Path, string? Type, string? Program, bool Running, int? Pid, int? LastExit);

public static class LaunchdParsers
{
    private static readonly Regex DisabledLine = new("^\\s*\"(?<label>[^\"]+)\"\\s*=>\\s*(?<state>enabled|disabled|true|false)\\s*$", RegexOptions.CultureInvariant);
    private static readonly Regex LeadingNumber = new(@"^-?\d+", RegexOptions.CultureInvariant);

    /// <summary>`launchctl list` (PID, Status, Label separated by tabs; "-" when there is none).</summary>
    public static IReadOnlyDictionary<string, LaunchdListItem> ParseList(string text)
    {
        var result = new Dictionary<string, LaunchdListItem>(StringComparer.Ordinal);
        foreach (var raw in text.Split('\n'))
        {
            var parts = raw.TrimEnd('\r').Split('\t');
            if (parts.Length < 3 || parts[0] == "PID" || parts[2].Length == 0)
                continue;
            result[parts[2]] = new LaunchdListItem(Number(parts[0]), Number(parts[1]));
        }
        return result;
    }

    /// <summary>`launchctl print-disabled &lt;domain&gt;`: label → disabled. Older macOS prints true/false instead of disabled/enabled.</summary>
    public static IReadOnlyDictionary<string, bool> ParsePrintDisabled(string text)
    {
        var result = new Dictionary<string, bool>(StringComparer.Ordinal);
        foreach (var raw in text.Split('\n'))
        {
            var m = DisabledLine.Match(raw.TrimEnd('\r'));
            if (m.Success)
                result[m.Groups["label"].Value] = m.Groups["state"].Value is "disabled" or "true";
        }
        return result;
    }

    /// <summary>`launchctl print`: only the service's own lines (one tab deep), not those of nested blocks.</summary>
    public static LaunchdPrint ParsePrint(string text)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.Length < 2 || line[0] != '\t' || line[1] == '\t')
                continue;
            var eq = line.IndexOf(" = ", StringComparison.Ordinal);
            if (eq > 0)
                values.TryAdd(line[1..eq].Trim(), line[(eq + 3)..].Trim());
        }
        var state = values.GetValueOrDefault("state");
        return new LaunchdPrint(
            values.GetValueOrDefault("path"),
            values.GetValueOrDefault("type"),
            values.GetValueOrDefault("program"),
            state == "running",
            values.TryGetValue("pid", out var pid) ? Number(pid) : null,
            values.TryGetValue("last exit code", out var exit) ? Number(exit) : null);
    }

    private static int? Number(string text)
    {
        var m = LeadingNumber.Match(text.Trim());
        return m.Success && int.TryParse(m.Value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var n) ? n : null;
    }
}
