using PcCleaner.Core;

namespace PcCleaner.Modules.Dashboard;

/// <summary>Facts about the Mac for the report (the rows of «Об этом Mac» on the dashboard).</summary>
public sealed record ReportMacInfo
{
    public string ComputerName { get; init; } = "—";

    public IReadOnlyList<(string Label, string Value)> Facts { get; init; } = Array.Empty<(string, string)>();

    /// <summary>Label/value pairs in display order (shared by the HTML and the text report).</summary>
    public IReadOnlyList<(string Label, string Value)> Rows() => Facts;
}

/// <summary>Result of one module in the report.</summary>
public sealed record ReportModule(string Name, ModuleScanState State, string StatusText, string? Error)
{
    public static ReportModule From(ModuleRow row) => new(row.Name, row.State, row.StateText, null);
}

/// <summary>One finding in the report (with what happened to it during this session).</summary>
public sealed record ReportIssue(
    IssueSeverity Severity,
    string Module,
    string Title,
    string Details,
    string? Gain,
    bool IsFixed,
    string? FixResult)
{
    /// <summary>A fix was tried in this session but did not succeed.</summary>
    public bool FixFailed => !IsFixed && !string.IsNullOrWhiteSpace(FixResult);

    public static ReportIssue From(HealthIssue issue) => new(
        issue.Severity,
        issue.Module ?? "",
        issue.Title ?? "",
        issue.Details ?? "",
        HealthScoring.GainText(issue),
        issue.IsFixed,
        string.IsNullOrWhiteSpace(issue.FixResult) ? null : issue.FixResult.Trim());
}

/// <summary>Findings of one severity, in display order.</summary>
public sealed record ReportGroup(IssueSeverity Severity, IReadOnlyList<ReportIssue> Issues)
{
    public string Title => HealthScoring.GroupTitle(Severity);

    public string Hint => HealthScoring.GroupHint(Severity).Replace("компьютера", "Mac");
}

/// <summary>Everything a saved / copied report contains. Built by the dashboard, rendered by <see cref="ReportBuilder"/>.</summary>
public sealed record DashboardReport
{
    public DateTime CreatedAt { get; init; }

    public DateTime ScanTime { get; init; }

    public int? Score { get; init; }

    public ScoreDelta? Delta { get; init; }

    public ReclaimableSplit Reclaimable { get; init; }

    public ReportMacInfo Pc { get; init; } = new();

    public IReadOnlyList<ReportModule> Modules { get; init; } = Array.Empty<ReportModule>();

    public IReadOnlyList<ReportGroup> Groups { get; init; } = Array.Empty<ReportGroup>();

    /// <summary>"Не удалось проверить: …" — the score may be incomplete.</summary>
    public string? Warning { get; init; }

    /// <summary>Modules that failed or timed out: the score only counts the others (verdict "Проверка неполная").</summary>
    public int FailedModules { get; init; }

    public bool IsIncomplete => FailedModules > 0;

    public string AppName { get; init; } = AppInfo.Name;

    public string AppVersion { get; init; } = AppInfo.Version;

    public string RepoUrl { get; init; } = AppInfo.RepoUrl;

    public IEnumerable<ReportIssue> AllIssues => Groups.SelectMany(g => g.Issues);

    public int FixedCount => AllIssues.Count(i => i.IsFixed);

    /// <summary>Remaining findings that lower the score.</summary>
    public int RemainingProblems => AllIssues.Count(i => !i.IsFixed && i.Severity > IssueSeverity.Info);

    public int RemainingTips => AllIssues.Count(i => !i.IsFixed && i.Severity <= IssueSeverity.Info);

    public string Verdict => HealthScoring.Verdict(Score, FailedModules).Replace("Компьютер", "Mac");

    public string VerdictDescription => HealthScoring.VerdictDescription(Score, RemainingProblems, FixedCount > 0, FailedModules)
        .Replace("Компьютер в отличной форме", "Mac в отличной форме")
        .Replace("замедляют компьютер или снижают защиту", "замедляют Mac или снижают защиту");

    /// <summary>"Найдено: 2 проблемы и 1 совет" over every finding of the scan (fixed ones included).</summary>
    public string FindingsText
    {
        get
        {
            var all = AllIssues.ToList();
            var problems = all.Count(i => i.Severity > IssueSeverity.Info);
            return all.Count == 0 ? "Проблем не найдено" : "Найдено: " + HealthScoring.FindingsText(problems, all.Count - problems);
        }
    }

    /// <summary>Groups issues by severity exactly like the dashboard list (most severe first).</summary>
    public static IReadOnlyList<ReportGroup> GroupIssues(IEnumerable<HealthIssue> issues)
        => HealthScoring.Group(issues.Where(i => i is not null))
            .Select(g => new ReportGroup(g.Severity, g.Issues.Select(ReportIssue.From).ToList()))
            .ToList();
}
