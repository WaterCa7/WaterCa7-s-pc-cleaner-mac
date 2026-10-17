using PcCleaner.Core;

namespace PcCleaner.Modules.Dashboard;

// Copied from the Windows version (src/PcCleaner/Modules/Dashboard/ReportModels.cs, lines 5-103): there these records share
// a file with WPF-dependent code, so the file itself cannot be shared. The copy is listed in Shared/SOURCE.md.
public enum ScoreTrend
{
    Same,
    Better,
    Worse,
}

/// <summary>Score change between the previous and the latest smart scan: "Было 52 → стало 78 (+26)".</summary>
public sealed record ScoreDelta(int Previous, int Current, DateTime? PreviousTime)
{
    public int Change => Current - Previous;

    public ScoreTrend Trend => Change switch
    {
        > 0 => ScoreTrend.Better,
        < 0 => ScoreTrend.Worse,
        _ => ScoreTrend.Same,
    };

    /// <summary>"+26", "−12" (typographic minus) or "без изменений".</summary>
    public string ChangeText => Change switch
    {
        > 0 => "+" + Change.ToString(Format.Ru),
        < 0 => "−" + (-Change).ToString(Format.Ru),
        _ => "без изменений",
    };

    public string Text => $"Было {Previous} → стало {Current} ({ChangeText})";

    /// <summary>Tooltip / report line explaining what is compared.</summary>
    public string Explanation(DateTime now) => PreviousTime is { } t
        ? $"Сравнение с прошлой проверкой ({HealthScoring.RelativeTime(t, now)})"
        : "Сравнение с прошлой проверкой";

    /// <summary>Null when there is nothing to compare (first scan, or the current score is unknown).</summary>
    public static ScoreDelta? Create(int? previous, int? current, DateTime? previousTime = null)
        => previous is null || current is null
            ? null
            : new ScoreDelta(Math.Clamp(previous.Value, 0, 100), Math.Clamp(current.Value, 0, 100), previousTime);
}

/// <summary>
/// Reclaimable space split honestly: what the fixes on the dashboard free right away (issues with a FixAsync)
/// and what only the user can free after reviewing it (old downloads, Windows.old, ...).
/// </summary>
public readonly record struct ReclaimableSplit(long AutoBytes, long ManualBytes)
{
    public long TotalBytes => Add(AutoBytes, ManualBytes);

    public bool IsEmpty => AutoBytes <= 0 && ManualBytes <= 0;

    /// <summary>Remaining (not fixed) issues only; negative sizes are ignored; sums saturate instead of overflowing.</summary>
    public static ReclaimableSplit From(IEnumerable<HealthIssue> issues)
    {
        long auto = 0, manual = 0;
        foreach (var issue in issues)
        {
            if (issue is null || issue.IsFixed || issue.ReclaimableBytes <= 0)
                continue;
            if (issue.FixAsync is not null)
                auto = Add(auto, issue.ReclaimableBytes);
            else
                manual = Add(manual, issue.ReclaimableBytes);
        }
        return new ReclaimableSplit(auto, manual);
    }

    /// <summary>"Можно освободить 2,1 ГБ" or null.</summary>
    public string? AutoText => AutoBytes > 0 ? "Можно освободить " + Format.Bytes(AutoBytes) : null;

    // Not "после проверки вручную": everywhere else in the app "проверка" means the scan, while this space is freed
    // only if the user looks through it and decides to delete it (old downloads, Windows.old...).
    private const string ManualHint = "если решите удалить сами";

    /// <summary>"Ещё до 41 ГБ — если решите удалить сами" (or a standalone wording when nothing is freed automatically).</summary>
    public string? ManualText => ManualBytes <= 0
        ? null
        : AutoBytes > 0
            ? $"Ещё до {Format.Bytes(ManualBytes)} — {ManualHint}"
            : $"До {Format.Bytes(ManualBytes)} можно освободить, {ManualHint}";

    /// <summary>One line for reports: "Можно освободить 2,1 ГБ (ещё до 41 ГБ — если решите удалить сами)".</summary>
    public string? SummaryText
    {
        get
        {
            if (AutoBytes > 0 && ManualBytes > 0)
                return $"Можно освободить {Format.Bytes(AutoBytes)} (ещё до {Format.Bytes(ManualBytes)} — {ManualHint})";
            return AutoText ?? ManualText;
        }
    }

    private static long Add(long a, long b)
    {
        a = Math.Max(0, a);
        b = Math.Max(0, b);
        return b > long.MaxValue - a ? long.MaxValue : a + b;
    }
}
