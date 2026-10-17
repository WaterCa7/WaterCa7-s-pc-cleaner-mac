using System.ComponentModel;
using PcCleaner.Core;

namespace PcCleaner.Modules.Dashboard;

/// <summary>One finding on the dashboard.</summary>
public sealed class IssueItem : ObservableObject
{
    public IssueItem(HealthIssue issue, Func<IssueItem, Task> fix, Action<string>? open)
    {
        Issue = issue;
        FixCommand = new AsyncRelayCommand(() => fix(this), () => CanFix);
        OpenCommand = new RelayCommand(() => open?.Invoke(issue.TargetPageKey!), () => CanOpen);
        CanOpen = open is not null && !string.IsNullOrEmpty(issue.TargetPageKey);
        issue.PropertyChanged += OnIssueChanged;
    }

    public HealthIssue Issue { get; }

    public string Title => Issue.Title;

    public string Details => Issue.Details;

    public bool HasDetails => !string.IsNullOrWhiteSpace(Issue.Details);

    public string? GainText => HealthScoring.GainText(Issue);

    public bool HasGain => GainText is not null;

    public string SeverityText => Severity.Text(Issue.Severity);

    public string SeverityBrushKey => Severity.BrushKey(Issue.Severity);

    public string SeveritySoftBrushKey => Severity.SoftBrushKey(Issue.Severity);

    public bool CanFix => Issue.CanFix;

    public bool CanOpen { get; }

    public bool IsFixed => Issue.IsFixed;

    public string? FixResult => Issue.FixResult;

    public bool HasFixResult => !string.IsNullOrWhiteSpace(Issue.FixResult);

    public AsyncRelayCommand FixCommand { get; }

    public RelayCommand OpenCommand { get; }

    private void OnIssueChanged(object? sender, PropertyChangedEventArgs e)
    {
        OnPropertyChanged(nameof(IsFixed));
        OnPropertyChanged(nameof(FixResult));
        OnPropertyChanged(nameof(HasFixResult));
        OnPropertyChanged(nameof(CanFix));
        FixCommand.RaiseCanExecuteChanged();
    }
}

/// <summary>Findings of one severity: "Важно — Заметно мешает работе или защите".</summary>
public sealed record IssueGroupItem(IssueSeverity Severity, IReadOnlyList<IssueItem> Items)
{
    public string Title => HealthScoring.GroupTitle(Severity);

    /// <summary>The shared hints speak of "компьютер"; on a Mac it is "Mac".</summary>
    public string Hint => HealthScoring.GroupHint(Severity).Replace("компьютера", "Mac");

    public string BrushKey => Dashboard.Severity.BrushKey(Severity);
}

/// <summary>A section of the scan and how it went.</summary>
public sealed class ModuleRow : ObservableObject
{
    private ModuleScanState _state = ModuleScanState.Waiting;
    private string _stateText = "ждёт";

    public ModuleRow(string name) => Name = name;

    public string Name { get; }

    public ModuleScanState State
    {
        get => _state;
        private set
        {
            if (Set(ref _state, value))
            {
                OnPropertyChanged(nameof(IsRunning));
                OnPropertyChanged(nameof(IsDone));
                OnPropertyChanged(nameof(IsFailed));
            }
        }
    }

    public string StateText
    {
        get => _stateText;
        private set => Set(ref _stateText, value);
    }

    public bool IsRunning => State == ModuleScanState.Running;

    public bool IsDone => State == ModuleScanState.Done;

    public bool IsFailed => State is ModuleScanState.Failed or ModuleScanState.TimedOut;

    public void SetRunning()
    {
        State = ModuleScanState.Running;
        StateText = "проверяется…";
    }

    public void SetResult(ModuleScanResult result)
    {
        State = result.State;
        StateText = result.State switch
        {
            ModuleScanState.Done => result.Issues.Count == 0
                ? "проблем нет"
                : HealthScoring.FindingsText(result.Issues.Count(HealthScoring.IsProblem), result.Issues.Count(i => !HealthScoring.IsProblem(i))),
            ModuleScanState.TimedOut => result.Error ?? "не уложился во время",
            ModuleScanState.Failed => "не удалось проверить: " + Lower((result.Error ?? "ошибка").TrimEnd('.')),
            ModuleScanState.Cancelled => "остановлено",
            _ => "ждёт",
        };
    }

    /// <summary>SmartScanner reports errors as sentences ("Нет доступа …"); inside the row they continue a phrase.</summary>
    private static string Lower(string text) => text.Length == 0 ? text : char.ToLower(text[0], Format.Ru) + text[1..];
}

public sealed record InfoRow(string Label, string Value);

/// <summary>Severity badges: text and theme brushes.</summary>
public static class Severity
{
    public static string Text(IssueSeverity s) => s switch
    {
        IssueSeverity.Critical => "Критично",
        IssueSeverity.High => "Важно",
        IssueSeverity.Medium => "Внимание",
        IssueSeverity.Low => "Мелочь",
        _ => "Совет",
    };

    public static string BrushKey(IssueSeverity s) => s switch
    {
        >= IssueSeverity.High => "DangerBrush",
        IssueSeverity.Medium => "WarningBrush",
        IssueSeverity.Low => "InfoBrush",
        _ => "MutedBrush",
    };

    public static string SoftBrushKey(IssueSeverity s) => s switch
    {
        >= IssueSeverity.High => "DangerSoftBrush",
        IssueSeverity.Medium => "WarningSoftBrush",
        IssueSeverity.Low => "InfoSoftBrush",
        _ => "Surface3Brush",
    };
}
