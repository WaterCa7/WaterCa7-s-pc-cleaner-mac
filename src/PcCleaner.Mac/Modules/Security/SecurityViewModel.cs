using System.Collections.ObjectModel;
using PcCleaner.Core;

namespace PcCleaner.Modules.Security;

/// <summary>Which checks a <see cref="SecurityViewModel"/> shows: «Безопасность» or «Сеть» (menu «Защита»).</summary>
public enum SecurityPart
{
    Protection,
    Network,
}

/// <summary>
/// «Безопасность» or «Сеть» (menu «Защита»): one model for both tabs, each showing its own groups of the same checks with an
/// explanation and what can be done. A fix through the password window asks first, listing the commands; the update list
/// («Безопасность» only) loads in the background (it asks Apple's servers).
/// </summary>
public sealed class SecurityViewModel : PageViewModel
{
    private readonly SecurityService _security;
    private readonly SecurityFixes _fixes;
    private readonly MacShell _shell;
    private readonly SecurityPart _part;
    private SecurityReport? _report;
    private SecurityCheck _updates;
    private Task? _loading;
    private string? _lastResult;
    private string _summary = "";

    public SecurityViewModel(SecurityService security, SecurityFixes fixes, MacShell shell, SecurityPart part = SecurityPart.Protection)
    {
        _security = security;
        _fixes = fixes;
        _shell = shell;
        _part = part;
        _updates = new SecurityCheck("updates", CheckGroup.MacOs, "Обновления macOS", SecurityStatus.Unknown, "проверяем…",
            "Список обновлений загружается с серверов Apple — это может занять до минуты.");
        RefreshCommand = new AsyncRelayCommand(() => RunBusyAsync(CheckingText, RefreshAsync), () => !IsBusy);
    }

    public override string Title => _part == SecurityPart.Network ? "Сеть" : "Безопасность";

    public override string Subtitle => _part == SecurityPart.Network
        ? "Прокси, DNS, файл hosts и Wi-Fi"
        : "Защита macOS, вход и общий доступ, профили";

    public override string Icon => _part == SecurityPart.Network ? "Globe" : "Shield";

    public override string Key => _part == SecurityPart.Network ? "Network" : "Security";

    public SecurityPart Part => _part;

    /// <summary>The title of the summary card.</summary>
    public string CardTitle => _part == SecurityPart.Network ? "Сеть Mac" : "Защита Mac";

    /// <summary>The note under the summary.</summary>
    public string NoteText => _part == SecurityPart.Network
        ? "Проверки только читают настройки. Исправления спрашивают пароль администратора в окне macOS; прежние значения прокси, DNS и файла hosts сохраняются в «Настройках» → «Резервные копии»."
        : "Проверки только читают настройки. Исправления спрашивают пароль администратора в окне macOS.";

    private string CheckingText => _part == SecurityPart.Network ? "Проверяем сеть…" : "Проверяем защиту…";

    public ObservableCollection<CheckGroupItem> Groups { get; } = new();

    /// <summary>Lines of /etc/hosts worth a look (the dangerous ones are ticked).</summary>
    public ObservableCollection<HostsEntry> HostsLines { get; } = new();

    public bool HasHostsLines => HostsLines.Count > 0;

    public string Summary
    {
        get => _summary;
        private set => Set(ref _summary, value);
    }

    public string? LastResult
    {
        get => _lastResult;
        private set
        {
            if (Set(ref _lastResult, value))
                OnPropertyChanged(nameof(HasLastResult));
        }
    }

    public bool HasLastResult => !string.IsNullOrEmpty(LastResult);

    public AsyncRelayCommand RefreshCommand { get; }

    /// <summary>The background load of the update list (tests wait for it).</summary>
    public Task UpdatesLoading { get; private set; } = Task.CompletedTask;

    public SecurityReport? Report => _report;

    public override Task OnNavigatedToAsync() => _loading ??= RunBusyAsync(CheckingText, RefreshAsync);

    /// <summary>The checks without the update list (which may take a minute and needs Apple's servers).</summary>
    public override async Task PreviewScanAsync(CancellationToken ct)
    {
        _report = await _security.CheckAsync(ct);
        Ui.Invoke(Rebuild);
    }

    public async Task RefreshAsync(CancellationToken ct)
    {
        _report = await _security.CheckAsync(ct);
        Ui.Invoke(Rebuild);
        if (_part == SecurityPart.Protection)
            UpdatesLoading = LoadUpdatesAsync();
    }

    private async Task LoadUpdatesAsync()
    {
        var updates = await _security.UpdatesAsync(CancellationToken.None);
        Ui.Invoke(() =>
        {
            _updates = updates;
            Rebuild();
        });
    }

    private void Rebuild()
    {
        if (_report is null)
            return;
        var checks = _report.Checks.Where(c => (c.Group == CheckGroup.Network) == (_part == SecurityPart.Network)).ToList();
        if (_part == SecurityPart.Protection)
        {
            var at = checks.FindIndex(c => c.Id == "autoupdate");
            checks.Insert(at < 0 ? checks.Count : at, _updates);
        }
        Groups.Clear();
        foreach (var group in checks.GroupBy(c => c.Group).OrderBy(g => g.Key))
            Groups.Add(new CheckGroupItem(group.Key, group.Select(c => new CheckItem(c, FixAsync, OpenSettingsAsync)).ToList()));

        HostsLines.Clear();
        if (_part == SecurityPart.Network)
        {
            foreach (var entry in _report.HostsEntries.Where(e => e.Risk is SecurityStatus.Info or SecurityStatus.Warning or SecurityStatus.Danger))
            {
                entry.IsSelected = entry.Risk is SecurityStatus.Warning or SecurityStatus.Danger;
                HostsLines.Add(entry);
            }
        }
        OnPropertyChanged(nameof(HasHostsLines));

        var problems = checks.Count(c => c.Status is SecurityStatus.Warning or SecurityStatus.Danger);
        var unknown = checks.Count(c => c.Status == SecurityStatus.Unknown && c != _updates);
        Summary = (problems == 0 ? "Проблем не найдено" : $"Требуют внимания: {problems}") +
                  (unknown > 0 ? $"; не удалось проверить: {unknown}" : "") + ".";
    }

    public async Task FixAsync(CheckItem item)
    {
        if (_report is null || !item.CanFix)
            return;
        var kind = item.Check.Fix;
        var lines = kind == FixKind.Hosts ? HostsLines.Where(l => l.IsSelected).ToList() : null;
        if (kind == FixKind.Hosts && lines!.Count == 0)
        {
            await Dialogs.InfoAsync(Title, "Отметьте строки файла hosts, которые нужно закомментировать.");
            return;
        }
        var commands = SecurityFixes.Commands(kind, _report, hostsPath: _security.Paths.Hosts);
        var text = SecurityFixes.Describe(kind, _report, lines?.Count ?? 0) + "\n\nmacOS попросит пароль администратора. Будет выполнено:\n" +
                   string.Join("\n", commands.Select(c => "• " + c));
        if (!await Dialogs.ConfirmAsync(item.FixText, text))
        {
            Log.Info($"«{item.FixText}» — отменено, ничего не изменено");
            return;
        }
        await RunBusyAsync(item.FixText + "…", async ct =>
        {
            var result = await _fixes.ApplyAsync(kind, _report, lines, _security.Paths.Hosts, ct);
            LastResult = result.Message;
            _report = await _security.CheckAsync(ct);
            Ui.Invoke(Rebuild);
        });
    }

    private Task OpenSettingsAsync(CheckItem item) => item.Check.SettingsLink is { } link ? _shell.OpenAsync(link) : Task.CompletedTask;
}
