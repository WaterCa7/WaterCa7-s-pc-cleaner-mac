using System.Globalization;

namespace PcCleaner.Modules.Programs;

public static class AppParsers
{
    /// <summary>
    /// `mdls -raw -name kMDItemLastUsedDate a.app b.app …`: one value per file, separated by NUL; "(null)" when the app was
    /// never opened (or Spotlight does not know it). Null when the output does not have one value per file.
    /// </summary>
    public static IReadOnlyList<DateTime?>? LastUsed(string raw, int count)
    {
        var values = raw.Split('\0');
        // The last value may or may not end with a NUL (and a newline when there is a single file).
        if (values.Length == count + 1 && values[^1].Trim().Length == 0)
            values = values[..^1];
        if (values.Length != count)
            return null;
        return values.Select(v => ParseDate(v.Trim())).ToList();
    }

    /// <summary>"2026-10-17 12:00:00 +0000" (UTC offset without a colon) → UTC time; anything else → null.</summary>
    public static DateTime? ParseDate(string text)
    {
        var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 3 || parts[2].Length != 5 || parts[2][0] is not ('+' or '-'))
            return null;
        if (!DateTime.TryParseExact(parts[0] + " " + parts[1], "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var local))
            return null;
        if (!int.TryParse(parts[2].AsSpan(1, 2), NumberStyles.None, CultureInfo.InvariantCulture, out var hours) ||
            !int.TryParse(parts[2].AsSpan(3, 2), NumberStyles.None, CultureInfo.InvariantCulture, out var minutes))
            return null;
        var offset = new TimeSpan(hours, minutes, 0);
        return DateTime.SpecifyKind(parts[2][0] == '+' ? local - offset : local + offset, DateTimeKind.Utc);
    }
}
