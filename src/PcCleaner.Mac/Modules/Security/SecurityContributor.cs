using PcCleaner.Core;

namespace PcCleaner.Modules.Security;

/// <summary>Findings of «Безопасность и сеть» for the dashboard (pure rules over the checks).</summary>
public static class SecurityRules
{
    public const string ModuleName = "Безопасность и сеть";
    public const string PageKey = "Security";
    public const string NetworkPageKey = "Network";

    /// <summary>How long the dashboard waits for `softwareupdate -l` (the module has a minute in all).</summary>
    public static readonly TimeSpan UpdatesBudget = TimeSpan.FromSeconds(40);

    /// <param name="fix">Runs a fix through the password window (never part of «Исправить найденное»).</param>
    public static IReadOnlyList<HealthIssue> Evaluate(SecurityReport report, SecurityCheck? updates, Func<FixKind, CancellationToken, Task<string>> fix)
    {
        var issues = new List<HealthIssue>();
        foreach (var check in updates is null ? report.Checks : report.Checks.Append(updates))
        {
            if (check.Severity is not { } severity || check.Status is SecurityStatus.Ok or SecurityStatus.Unknown)
                continue;
            // Hosts lines are chosen on the page; everything else can be fixed from the dashboard too.
            var kind = check.Fix;
            issues.Add(new HealthIssue
            {
                Module = ModuleName,
                Title = $"{check.Title}: {check.Summary}",
                Details = check.Details,
                Severity = severity,
                FixAsync = kind is FixKind.None or FixKind.Hosts ? null : ct => fix(kind, ct),
                AutoFixSafe = false,
                TargetPageKey = check.Group == CheckGroup.Network ? NetworkPageKey : PageKey,
            });
        }
        return issues;
    }
}

/// <summary>The dashboard check of «Безопасность и сеть»: every check plus the update list, read-only.</summary>
public sealed class SecurityContributor : IHealthContributor
{
    private readonly SecurityService _security;
    private readonly SecurityFixes _fixes;

    public SecurityContributor(SecurityService security, SecurityFixes fixes)
    {
        _security = security;
        _fixes = fixes;
    }

    public string ModuleName => SecurityRules.ModuleName;

    public async Task<IReadOnlyList<HealthIssue>> AnalyzeAsync(CancellationToken ct)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(SecurityRules.UpdatesBudget);
        var updatesTask = _security.UpdatesAsync(budget.Token);
        var report = await _security.CheckAsync(ct);
        SecurityCheck? updates;
        try
        {
            updates = await updatesTask;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            Log.Info("Главная: список обновлений macOS не успел загрузиться — проверьте его в разделе «Безопасность»");
            updates = null;
        }
        return SecurityRules.Evaluate(report, updates,
            async (kind, fixCt) => (await _fixes.ApplyAsync(kind, report, null, _security.Paths.Hosts, fixCt)).Message);
    }
}
