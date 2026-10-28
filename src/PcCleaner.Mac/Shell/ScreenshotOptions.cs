using System.Globalization;

namespace PcCleaner.Shell;

/// <summary>
/// `--screenshots &lt;folder&gt; [--page Dashboard,System,Protection/Network] [--no-scan] [--size 1040x660]`: renders every tab
/// without a screen after a read-only scan (--page: a page key, a section or Section/Tab) and saves NN-Key.png (a section of one
/// page) or NN-Section-Tab.png and log.txt.
/// </summary>
public sealed record ScreenshotOptions(string Dir, IReadOnlyList<string> Pages, bool Scan, int Width, int Height)
{
    public const int DefaultWidth = 1100;
    public const int DefaultHeight = 720;
    public const int MinWidth = 960;
    public const int MinHeight = 640;

    /// <summary>False when the arguments are not a screenshot run; otherwise true with the options or an error (Russian).</summary>
    public static bool TryParse(IReadOnlyList<string> args, out ScreenshotOptions? options, out string? error)
    {
        options = null;
        error = null;
        if (!args.Contains("--screenshots", StringComparer.OrdinalIgnoreCase))
            return false;

        string? dir = null;
        var pages = new List<string>();
        var scan = true;
        int width = DefaultWidth, height = DefaultHeight;
        for (var i = 0; i < args.Count; i++)
        {
            string? Next() => i + 1 < args.Count && !args[i + 1].StartsWith("--", StringComparison.Ordinal) ? args[++i] : null;
            switch (args[i].ToLowerInvariant())
            {
                case "--screenshots":
                    dir = Next();
                    break;
                case "--page":
                    pages.AddRange((Next() ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
                    break;
                case "--no-scan":
                    scan = false;
                    break;
                case "--size":
                    if (!TryParseSize(Next(), out width, out height))
                    {
                        error = $"Неверный размер: нужен вид 1040x660, не меньше {MinWidth}x{MinHeight}";
                        return true;
                    }
                    break;
                default:
                    error = $"Неизвестный параметр «{args[i]}»";
                    return true;
            }
        }
        if (string.IsNullOrWhiteSpace(dir))
        {
            error = "После --screenshots укажите папку для снимков";
            return true;
        }
        options = new ScreenshotOptions(dir, pages, scan, width, height);
        return true;
    }

    private static bool TryParseSize(string? text, out int width, out int height)
    {
        width = height = 0;
        var parts = (text ?? "").Split('x', 'X', '×');
        return parts.Length == 2 &&
               int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out width) &&
               int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out height) &&
               width >= MinWidth && height >= MinHeight && width <= 4000 && height <= 4000;
    }
}
