using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PcCleaner.Modules.Dashboard;

/// <summary>What system_profiler says about the hardware (no serial number or UUIDs are read).</summary>
public sealed record HardwareInfo(string? ModelName, string? ModelId, string? Chip, long MemoryBytes, string? Cores);

/// <summary>One volume as `df -k -P` reports it.</summary>
public sealed record DiskUsage(string Mount, long TotalBytes, long UsedBytes, long FreeBytes)
{
    public double FreePercent => TotalBytes > 0 ? 100.0 * Math.Clamp(FreeBytes, 0, TotalBytes) / TotalBytes : 0;
}

/// <summary>Pure parsers of sw_vers, sysctl, system_profiler and df output.</summary>
public static class MacInfoParsers
{
    private static readonly Regex BootSeconds = new(@"\bsec\s*=\s*(\d+)", RegexOptions.CultureInvariant);
    private static readonly Regex MemoryText = new(@"^\s*(\d+(?:[.,]\d+)?)\s*(KB|MB|GB|TB)\s*$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    /// <summary>"ProductName: macOS / ProductVersion: 15.1 / BuildVersion: 24B83".</summary>
    public static (string? Name, string? Version, string? Build) ParseSwVers(string text)
    {
        string? name = null, version = null, build = null;
        foreach (var line in text.Split('\n'))
        {
            var colon = line.IndexOf(':');
            if (colon <= 0)
                continue;
            var value = line[(colon + 1)..].Trim();
            if (value.Length == 0)
                continue;
            switch (line[..colon].Trim())
            {
                case "ProductName":
                    name = value;
                    break;
                case "ProductVersion":
                    version = value;
                    break;
                case "BuildVersion":
                    build = value;
                    break;
            }
        }
        return (name, version, build);
    }

    /// <summary>`sysctl -n kern.boottime`: "{ sec = 1729152000, usec = 123456 } Thu Oct 17 10:00:00 2024".</summary>
    public static DateTimeOffset? ParseBootTime(string text)
    {
        var m = BootSeconds.Match(text);
        return m.Success && long.TryParse(m.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var sec) && sec > 0
            ? DateTimeOffset.FromUnixTimeSeconds(sec)
            : null;
    }

    /// <summary>`system_profiler SPHardwareDataType -json`; Intel Macs have cpu_type instead of chip_type.</summary>
    public static HardwareInfo? ParseHardware(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("SPHardwareDataType", out var list) || list.ValueKind != JsonValueKind.Array ||
                list.GetArrayLength() == 0)
                return null;
            var item = list[0];
            return new HardwareInfo(
                Str(item, "machine_name"),
                Str(item, "machine_model"),
                Str(item, "chip_type") ?? Str(item, "cpu_type"),
                ParseMemoryText(Str(item, "physical_memory") ?? ""),
                Str(item, "number_processors"));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>"16 GB" → bytes (binary units, as macOS counts memory); 0 when unknown.</summary>
    public static long ParseMemoryText(string text)
    {
        var m = MemoryText.Match(text);
        if (!m.Success || !double.TryParse(m.Groups[1].Value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            return 0;
        var shift = m.Groups[2].Value.ToUpperInvariant() switch
        {
            "KB" => 10,
            "MB" => 20,
            "GB" => 30,
            _ => 40,
        };
        return (long)(value * (1L << shift));
    }

    /// <summary>The volume line of `df -k -P &lt;path&gt;` (sizes in 1024-byte blocks).</summary>
    public static DiskUsage? ParseDf(string text)
    {
        var line = text.Split('\n').Select(l => l.Trim()).LastOrDefault(l => l.Length > 0 && !l.StartsWith("Filesystem", StringComparison.Ordinal));
        if (line is null)
            return null;
        var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 6 ||
            !long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var total) ||
            !long.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var used) ||
            !long.TryParse(parts[3], NumberStyles.None, CultureInfo.InvariantCulture, out var free) ||
            total <= 0)
            return null;
        return new DiskUsage(string.Join(' ', parts[5..]), total * 1024, used * 1024, free * 1024);
    }

    private static string? Str(JsonElement item, string name)
    {
        if (!item.TryGetProperty(name, out var v))
            return null;
        var s = v.ValueKind switch
        {
            JsonValueKind.String => v.GetString(),
            JsonValueKind.Number => v.GetRawText(),
            _ => null,
        };
        return string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    }
}

/// <summary>Marketing names of macOS versions.</summary>
public static class MacOsNames
{
    private static readonly Dictionary<int, string> Names = new()
    {
        [26] = "Tahoe",
        [15] = "Sequoia",
        [14] = "Sonoma",
        [13] = "Ventura",
        [12] = "Monterey",
        [11] = "Big Sur",
    };

    /// <summary>"15.1" → "macOS Sequoia 15.1"; unknown versions — "macOS 27.0"; no version — "macOS".</summary>
    public static string Marketing(string? version)
    {
        if (string.IsNullOrWhiteSpace(version))
            return "macOS";
        var major = version.Split('.')[0];
        return int.TryParse(major, NumberStyles.None, CultureInfo.InvariantCulture, out var m) && Names.TryGetValue(m, out var name)
            ? $"macOS {name} {version}"
            : $"macOS {version}";
    }
}
