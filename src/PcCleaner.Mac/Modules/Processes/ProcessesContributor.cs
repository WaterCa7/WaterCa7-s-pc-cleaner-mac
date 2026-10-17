using PcCleaner.Core;
using PcCleaner.Modules.Startup;

namespace PcCleaner.Modules.Processes;

/// <summary>Findings of «Процессы и автозагрузка» for the dashboard (pure rules).</summary>
public static class ProcessesRules
{
    public const string ModuleName = "Процессы и автозагрузка";
    public const string PageKey = "Processes";

    /// <summary>More third-party startup items than this is worth a look.</summary>
    public const int ManyThirdParty = 15;

    private const string NotAVirus = "Это не обязательно вирус: проверьте программу в разделе «Процессы и автозагрузка» (поиск в интернете, VirusTotal).";

    /// <param name="home">The user's home folder (unsigned programs there are worth a look).</param>
    /// <param name="disable">Disables a startup item (the fix; reversible, with a backup).</param>
    public static IReadOnlyList<HealthIssue> Evaluate(IReadOnlyList<ProcessEntry> processes, IReadOnlyList<LaunchEntry> startup, string home,
        Func<LaunchEntry, CancellationToken, Task<string>> disable)
    {
        var issues = new List<HealthIssue>();

        // Processes reach the dashboard only with a strong sign; an app's helpers are one finding.
        foreach (var group in processes.Where(p => p.HasStrongHint).GroupBy(p => p.Location ?? p.Info.Executable, StringComparer.Ordinal))
        {
            var first = group.First();
            issues.Add(new HealthIssue
            {
                Module = ModuleName,
                Title = $"Подозрительный процесс «{first.Info.Name}»",
                Details = $"{Reasons(first.Hints)}. Путь: {first.Info.Executable}. {NotAVirus}",
                Severity = IssueSeverity.High,
                TargetPageKey = PageKey,
            });
        }

        var active = startup.Where(e => !e.ReadOnly && !e.Disabled).ToList();
        foreach (var entry in active)
        {
            if (entry.HasStrongHint)
            {
                issues.Add(new HealthIssue
                {
                    Module = ModuleName,
                    Title = $"Подозрительная запись автозагрузки «{entry.Label}»",
                    Details = $"{Reasons(entry.Hints)}. Запускает: {entry.CommandLine}. {NotAVirus} «Исправить» отключит запись — это обратимо.",
                    Severity = IssueSeverity.High,
                    FixAsync = entry.HasLabel ? ct => disable(entry, ct) : null,
                    TargetPageKey = PageKey,
                });
            }
            else if (entry.IsBroken && entry.Source is LaunchSource.UserAgent or LaunchSource.GlobalAgent)
            {
                issues.Add(new HealthIssue
                {
                    Module = ModuleName,
                    Title = $"Сломанная запись автозагрузки «{entry.Label}»",
                    Details = $"Причина: {entry.Problem}. Обычно это остаток удалённой программы: macOS пытается её запустить и пишет ошибки в журнал. " +
                              (entry.HasLabel ? "«Исправить» отключит запись (копия сохранится)." : "Её можно удалить в разделе «Процессы и автозагрузка»."),
                    Severity = IssueSeverity.Medium,
                    FixAsync = entry.HasLabel ? ct => disable(entry, ct) : null,
                    AutoFixSafe = entry.HasLabel,
                    TargetPageKey = PageKey,
                });
            }
            else if (!entry.IsBroken && entry.Signature?.Kind is SignatureKind.Unsigned or SignatureKind.AdHoc
                     && entry.Target?.StartsWith(home.TrimEnd('/') + "/", StringComparison.Ordinal) == true)
            {
                issues.Add(new HealthIssue
                {
                    Module = ModuleName,
                    Title = $"Запись автозагрузки без подписи разработчика: «{entry.Label}»",
                    Details = $"Программа {entry.Target} лежит в вашей папке и не подписана разработчиком — так бывает у самодельных скриптов и " +
                              "инструментов разработки, но и у нежелательных программ. Если вы её не узнаёте, отключите запись.",
                    Severity = IssueSeverity.Medium,
                    TargetPageKey = PageKey,
                });
            }
        }

        var thirdParty = active.Count(e => e.Source != LaunchSource.Apple);
        if (thirdParty > ManyThirdParty)
        {
            issues.Add(new HealthIssue
            {
                Module = ModuleName,
                Title = $"В автозагрузке {thirdParty} {Format.Plural(thirdParty, "сторонняя запись", "сторонние записи", "сторонних записей")}",
                Details = "Каждая запускается вместе с Mac и работает в фоне. Отключите те, что вам не нужны, в разделе «Процессы и автозагрузка».",
                Severity = IssueSeverity.Info,
                TargetPageKey = PageKey,
            });
        }
        return issues;
    }

    private static string Reasons(IReadOnlyList<Hint> hints)
    {
        var text = string.Join("; ", hints.Where(h => h.Strong).Select(h => h.Text));
        return text.Length == 0 ? text : char.ToUpper(text[0], Format.Ru) + text[1..];
    }
}

/// <summary>The dashboard check of «Процессы и автозагрузка»: running processes and startup items (read-only).</summary>
public sealed class ProcessesContributor : IHealthContributor
{
    private readonly ProcessesService _processes;
    private readonly StartupService _startup;
    private readonly string _home;

    public ProcessesContributor(ProcessesService processes, StartupService startup, string home)
    {
        _processes = processes;
        _startup = startup;
        _home = home;
    }

    public string ModuleName => ProcessesRules.ModuleName;

    public async Task<IReadOnlyList<HealthIssue>> AnalyzeAsync(CancellationToken ct)
    {
        var processes = await _processes.ListAsync(ct);
        if (processes.Error is not null)
            Log.Warn("Главная: " + processes.Error);
        var startup = await _startup.ListAsync(includeApple: false, ct);
        return ProcessesRules.Evaluate(processes.Items, startup.Items, _home,
            async (entry, fixCt) => (await _startup.DisableAsync(entry, fixCt)).Message);
    }
}
