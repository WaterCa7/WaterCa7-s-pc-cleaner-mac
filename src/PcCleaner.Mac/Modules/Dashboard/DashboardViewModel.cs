using System.Collections.ObjectModel;
using System.Text;
using PcCleaner.Core;

namespace PcCleaner.Modules.Dashboard;

/// <summary>
/// «Главная»: one read-only check of every section, the 0–100 score with the same rules as the Windows version, findings
/// grouped by severity, «Исправить найденное» for the safe ones after one confirmation, and the facts about this Mac.
/// </summary>
public sealed class DashboardViewModel : PageViewModel
{
    private readonly IReadOnlyList<IHealthContributor> _contributors;
    private readonly MacInfoService _info;
    private readonly AppSettings _settings;
    private readonly INavigator? _navigator;
    private Task? _loading;
    private ScanSummary? _summary;
    private bool _summaryFromThisSession;
    private List<HealthIssue> _issues = new();
    private bool _isScanning;
    private string? _scanWarning;
    private ObservableCollection<IssueGroupItem> _groups = new();
    private ObservableCollection<ModuleRow> _modules = new();

    public DashboardViewModel(IReadOnlyList<IHealthContributor> contributors, MacInfoService info, AppSettings settings, INavigator? navigator)
    {
        _contributors = contributors;
        _info = info;
        _settings = settings;
        _navigator = navigator;
        ScanCommand = new AsyncRelayCommand(() => RunBusyAsync("Проверяем Mac…", ScanAsync), () => !IsBusy);
        FixAllCommand = new AsyncRelayCommand(() => RunBusyAsync("Исправляем найденное…", FixAllCoreAsync), () => !IsBusy && FixableCount > 0);
    }

    public override string Title => "Главная";

    public override string Subtitle => "Оценка состояния Mac и исправление найденного в один клик";

    public override string Icon => "Home";

    public override string Key => "Dashboard";

    public AsyncRelayCommand ScanCommand { get; }

    public AsyncRelayCommand FixAllCommand { get; }

    public ObservableCollection<InfoRow> InfoRows { get; } = new();

    public ObservableCollection<IssueGroupItem> Groups
    {
        get => _groups;
        private set
        {
            if (Set(ref _groups, value))
                OnPropertyChanged(nameof(HasGroups));
        }
    }

    public ObservableCollection<ModuleRow> Modules
    {
        get => _modules;
        private set
        {
            if (Set(ref _modules, value))
                OnPropertyChanged(nameof(HasModules));
        }
    }

    public bool HasModules => Modules.Count > 0;

    public bool HasGroups => Groups.Count > 0;

    public bool IsScanning
    {
        get => _isScanning;
        private set => Set(ref _isScanning, value);
    }

    /// <summary>Shown above the findings: failed sections, a stopped scan.</summary>
    public string? ScanWarning
    {
        get => _scanWarning;
        private set
        {
            if (Set(ref _scanWarning, value))
                OnPropertyChanged(nameof(HasScanWarning));
        }
    }

    public bool HasScanWarning => !string.IsNullOrEmpty(ScanWarning);

    public bool HasScan => _summary is not null;

    public int? Score => _summary?.Score;

    public string ScoreText => Score?.ToString(Format.Ru) ?? "—";

    public double ScoreValue => Score ?? 0;

    private int FailedModules => _summary?.FailedModules ?? 0;

    public string Verdict => HealthScoring.Verdict(Score, FailedModules).Replace("Компьютер", "Mac");

    public string VerdictDescription
    {
        get
        {
            var text = HealthScoring.VerdictDescription(Score, ProblemCount, _issues.Any(i => i.IsFixed), FailedModules);
            return text.Replace("Компьютер в отличной форме", "Mac в отличной форме")
                .Replace("замедляют компьютер или снижают защиту", "замедляют Mac или снижают защиту");
        }
    }

    public string ScoreBrushKey => HealthScoring.ScoreBrushKey(Score, FailedModules);

    private int ProblemCount => _summaryFromThisSession
        ? _issues.Count(i => !i.IsFixed && HealthScoring.IsProblem(i))
        : _summary?.ProblemCount ?? 0;

    private int TipCount => _summaryFromThisSession
        ? _issues.Count(i => !i.IsFixed && !HealthScoring.IsProblem(i))
        : _summary?.TipCount ?? 0;

    public string FindingsText => HealthScoring.FindingsText(ProblemCount, TipCount);

    public string LastScanText => _summary is null
        ? "Mac ещё не проверялся"
        : "Последняя проверка: " + HealthScoring.RelativeTime(_summary.Time, DateTime.Now);

    public bool HasDelta => _summary?.Delta is not null;

    public string DeltaText => _summary?.Delta?.Text ?? "";

    public int FixableCount => _issues.Count(i => i.CanFix && i.AutoFixSafe);

    public bool HasFixable => FixableCount > 0;

    public string FixAllText => FixableCount > 0 ? $"Исправить найденное ({FixableCount})" : "Исправить найденное";

    public string ScanButtonText => HasScan ? "Проверить снова" : "Проверить Mac";

    /// <summary>Loads the facts about this Mac and the previous scan once.</summary>
    public override Task OnNavigatedToAsync() => _loading ??= LoadAsync();

    public override Task PreviewScanAsync(CancellationToken ct) => ScanAsync(ct);

    private async Task LoadAsync()
    {
        _summary ??= ScanSummary.Load(_settings);
        RaiseHero();
        MacInfo info;
        try
        {
            info = await _info.LoadAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            Log.Warn($"Главная: не удалось прочитать сведения о Mac: {ErrorText.For(ex)}");
            info = new MacInfo();
        }
        Ui.Post(() =>
        {
            InfoRows.Clear();
            foreach (var (label, value) in info.Rows(DateTimeOffset.Now))
                InfoRows.Add(new InfoRow(label, value));
        });
    }

    /// <summary>Runs every section's read-only check and scores the result. Never changes the system.</summary>
    public async Task ScanAsync(CancellationToken ct)
    {
        _summary ??= ScanSummary.Load(_settings);
        var rows = _contributors.Select(c => new ModuleRow(SafeName(c))).ToList();
        Ui.Invoke(() =>
        {
            Modules = new ObservableCollection<ModuleRow>(rows);
            ScanWarning = null;
            IsScanning = true;
        });
        Log.Info($"Главная: проверка Mac запущена ({Format.Count(rows.Count, "раздел", "раздела", "разделов")})");

        var done = 0;
        async Task<ModuleScanResult> RunAsync(IHealthContributor contributor, ModuleRow row)
        {
            Ui.Post(row.SetRunning);
            var result = await SmartScanner.RunModuleAsync(contributor, SmartScanner.DefaultModuleTimeout, ct);
            Ui.Post(() => row.SetResult(result));
            var count = Interlocked.Increment(ref done);
            if (!ct.IsCancellationRequested)
                ReportProgress(100.0 * count / Math.Max(1, rows.Count), $"Проверяем Mac: готово {count} из {rows.Count} — {result.ModuleName}");
            return result;
        }

        ModuleScanResult[] results;
        try
        {
            results = await Task.WhenAll(_contributors.Select((c, i) => RunAsync(c, rows[i])));
        }
        finally
        {
            Ui.Invoke(() => IsScanning = false);
        }

        var issues = results.Where(r => r.State == ModuleScanState.Done).SelectMany(r => r.Issues).ToList();
        var failed = results.Where(r => r.State is ModuleScanState.Failed or ModuleScanState.TimedOut).Select(r => r.ModuleName).ToList();
        Ui.Invoke(() =>
        {
            _issues = issues;
            Groups = BuildGroups(issues);
            if (ct.IsCancellationRequested)
            {
                ScanWarning = "Проверка остановлена. Показаны только проблемы из уже проверенных разделов, оценка не обновлена.";
            }
            else
            {
                var score = HealthScoring.ComputeScore(issues);
                // "Было → стало" compares with what the previous scan MEASURED (see ScanSummary.NextBaseline).
                var baseline = _summary?.NextBaseline;
                _summary = MakeSummary(score, score, DateTime.Now, failed.Count, baseline?.Score, baseline?.Time);
                _summaryFromThisSession = true;
                SaveSummary();
                if (failed.Count > 0)
                    ScanWarning = $"Не удалось проверить: {string.Join(", ", failed)}. Оценка может быть неточной.";
            }
            RaiseHero();
        });
        if (!ct.IsCancellationRequested)
        {
            Log.Info($"Главная: проверка завершена — оценка {Score}, " +
                     (issues.Count == 0 ? "проблем не найдено" : "найдено " + FindingsText));
        }
    }

    /// <summary>«Исправить найденное» without the busy strip (tests).</summary>
    public Task FixAllAsync() => FixAllCoreAsync(CancellationToken.None);

    private async Task FixAllCoreAsync(CancellationToken ct)
    {
        var items = Groups.SelectMany(g => g.Items).Where(i => i.Issue.CanFix && i.Issue.AutoFixSafe).ToList();
        if (items.Count == 0)
        {
            await Dialogs.InfoAsync("Исправить найденное", "Сейчас нечего исправлять автоматически. Остальные находки откройте в их разделах.");
            return;
        }
        if (!await Dialogs.ConfirmAsync("Исправить найденное", ConfirmText(items)))
        {
            Log.Info("Главная: исправление отменено — ничего не изменено");
            return;
        }
        foreach (var item in items)
        {
            ct.ThrowIfCancellationRequested();
            await RunFixAsync(item, ct);
        }
        AfterFixes();
    }

    /// <summary>The «Исправить» button of one finding: its own confirmation, then the fix.</summary>
    public async Task FixAsync(IssueItem item)
    {
        if (!item.Issue.CanFix)
            return;
        var message = $"{item.Title}" + (item.HasDetails ? $"\n\n{item.Details}" : "") + "\n\nИсправить?";
        if (!await Dialogs.ConfirmAsync("Исправить", message))
            return;
        await RunFixAsync(item, CancellationToken.None);
        AfterFixes();
    }

    internal static string ConfirmText(IReadOnlyList<IssueItem> items)
    {
        var text = new StringBuilder("Будет сделано:\n\n");
        foreach (var item in items)
            text.Append("• ").Append(item.Title).Append(item.GainText is { } g ? $" — {g}" : "").Append('\n');
        text.Append("\nВсё сделанное запишется в журнал. Продолжить?");
        return text.ToString();
    }

    private static async Task RunFixAsync(IssueItem item, CancellationToken ct)
    {
        var issue = item.Issue;
        string result;
        try
        {
            result = await issue.FixAsync!(ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Log.Error($"Главная: не удалось исправить «{issue.Title}»", ex);
            result = "Не удалось: " + ErrorText.For(ex);
        }
        Ui.Invoke(() =>
        {
            issue.FixResult = result;
            issue.IsFixed = !HealthScoring.LooksLikeFailure(result);
        });
        if (issue.IsFixed)
            Log.Action($"Главная: «{issue.Title}» — {result}");
        else
            Log.Warn($"Главная: «{issue.Title}» не исправлено — {result}");
    }

    /// <summary>Fixed findings no longer count: the score is re-estimated (the next scan still compares with the measured one).</summary>
    private void AfterFixes()
    {
        Ui.Invoke(() =>
        {
            if (_summary is not null && _summaryFromThisSession)
            {
                _summary = MakeSummary(HealthScoring.ComputeScore(_issues), _summary.BaselineScore, _summary.Time,
                    _summary.FailedModules, _summary.PreviousScore, _summary.PreviousTime);
                SaveSummary();
            }
            RaiseHero();
        });
    }

    private ObservableCollection<IssueGroupItem> BuildGroups(IEnumerable<HealthIssue> issues)
    {
        Action<string>? open = _navigator is null ? null : key => _navigator.NavigateTo(key);
        return new ObservableCollection<IssueGroupItem>(HealthScoring.Group(issues)
            .Select(g => new IssueGroupItem(g.Severity, g.Issues.Select(i => new IssueItem(i, FixAsync, open)).ToList())));
    }

    private ScanSummary MakeSummary(int score, int measuredScore, DateTime time, int failedModules, int? previousScore, DateTime? previousTime)
    {
        var split = ReclaimableSplit.From(_issues);
        return new ScanSummary
        {
            Score = score,
            MeasuredScore = measuredScore,
            Time = time,
            IssueCount = _issues.Count(i => !i.IsFixed),
            TipCount = _issues.Count(i => !i.IsFixed && !HealthScoring.IsProblem(i)),
            ImportantCount = _issues.Count(i => !i.IsFixed && i.Severity >= IssueSeverity.High),
            FixableCount = _issues.Count(i => i.CanFix),
            ReclaimableBytes = split.TotalBytes,
            ManualReclaimableBytes = split.ManualBytes,
            FailedModules = failedModules,
            PreviousScore = previousScore,
            PreviousTime = previousScore is null ? null : previousTime,
        };
    }

    private void SaveSummary()
    {
        try
        {
            _summary?.Save(_settings);
        }
        catch (Exception ex)
        {
            Log.Warn($"Главная: не удалось сохранить результат проверки: {ErrorText.For(ex)}");
        }
    }

    private void RaiseHero()
    {
        foreach (var name in new[]
                 {
                     nameof(HasScan), nameof(Score), nameof(ScoreText), nameof(ScoreValue), nameof(Verdict), nameof(VerdictDescription),
                     nameof(ScoreBrushKey), nameof(FindingsText), nameof(LastScanText), nameof(HasDelta), nameof(DeltaText),
                     nameof(FixableCount), nameof(HasFixable), nameof(FixAllText), nameof(ScanButtonText),
                 })
            OnPropertyChanged(name);
        FixAllCommand.RaiseCanExecuteChanged();
    }

    private static string SafeName(IHealthContributor c)
    {
        try
        {
            return string.IsNullOrWhiteSpace(c.ModuleName) ? c.GetType().Name : c.ModuleName;
        }
        catch
        {
            return c.GetType().Name;
        }
    }
}
