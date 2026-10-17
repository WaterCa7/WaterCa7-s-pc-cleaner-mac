using System.Collections.ObjectModel;
using PcCleaner.Core;
using PcCleaner.Modules.Startup;

namespace PcCleaner.Modules.Processes;

/// <summary>
/// «Процессы и автозагрузка». Tab «Процессы»: what runs now, with signatures and hints; the user's own processes can be
/// stopped (after a confirmation; «принудительно» only after a second one). Tab «Автозагрузка»: launchd items by source
/// with reversible «Отключить»/«Включить», «Удалить» for the user's own, and login items (read only on a button, since
/// macOS asks for permission to control System Events).
/// </summary>
public sealed class ProcessesViewModel : PageViewModel
{
    public static readonly IReadOnlyList<ProcessSort> Sorts = new[]
    {
        new ProcessSort("по ЦП", rows => rows.OrderByDescending(r => r.Entry.Info.CpuPercent).ThenByDescending(r => r.Entry.Info.MemoryBytes)),
        new ProcessSort("по памяти", rows => rows.OrderByDescending(r => r.Entry.Info.MemoryBytes)),
        new ProcessSort("по имени", rows => rows.OrderBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase)),
    };

    private readonly ProcessesService _processes;
    private readonly StartupService _startup;
    private readonly LoginItemsService _loginItems;
    private readonly MacShell _shell;
    private IReadOnlyList<ProcessRow> _allProcesses = Array.Empty<ProcessRow>();
    private Task? _loading;
    private string _searchText = "";
    private bool _hideSystem = true;
    private ProcessSort _sort = Sorts[0];
    private ProcessRow? _selected;
    private bool _showApple;
    private string? _lastResult;
    private string _processSummary = "";
    private string? _startupProblems;
    private string? _loginItemsStatus;
    private bool _loginItemsLoaded;
    private bool _loginItemsNeedPermission;

    public ProcessesViewModel(ProcessesService processes, StartupService startup, LoginItemsService loginItems, MacShell shell)
    {
        _processes = processes;
        _startup = startup;
        _loginItems = loginItems;
        _shell = shell;

        RefreshCommand = new AsyncRelayCommand(() => RunBusyAsync("Смотрим процессы и автозагрузку…", RefreshAsync), () => !IsBusy);
        TerminateCommand = new AsyncRelayCommand(() => Selected is { } row ? TerminateAsync(row) : Task.CompletedTask, () => Selected?.CanTerminate == true);
        RevealProcessCommand = new AsyncRelayCommand(() => Selected?.Entry.Location is { } path ? _shell.RevealAsync(path) : Task.CompletedTask);
        SearchWebCommand = new AsyncRelayCommand(() => Selected is { } row ? _shell.OpenAsync(WebLinks.Search($"{row.Name} macOS что это")) : Task.CompletedTask);
        VirusTotalCommand = new AsyncRelayCommand(() => Selected is { } row ? VirusTotalAsync(row) : Task.CompletedTask, () => Selected?.CanHash == true);
        LoadLoginItemsCommand = new AsyncRelayCommand(LoadLoginItemsAsync, () => !IsBusy);
        OpenLoginItemsSettingsCommand = new AsyncRelayCommand(() => _shell.OpenAsync(SystemSettingsLinks.LoginItems));
        OpenAutomationSettingsCommand = new AsyncRelayCommand(() => _shell.OpenAsync(SystemSettingsLinks.Automation));
    }

    public override string Title => "Процессы и автозагрузка";

    public override string Subtitle => "Что запущено сейчас и что запускается вместе с Mac";

    public override string Icon => "Pulse";

    public override string Key => "Processes";

    /// <summary>What the last action did.</summary>
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

    // ===== «Процессы»

    public ObservableCollection<ProcessRow> Processes { get; } = new();

    public IReadOnlyList<ProcessSort> SortChoices => Sorts;

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (Set(ref _searchText, value ?? ""))
                ApplyFilter();
        }
    }

    public bool HideSystem
    {
        get => _hideSystem;
        set
        {
            if (Set(ref _hideSystem, value))
                ApplyFilter();
        }
    }

    public ProcessSort Sort
    {
        get => _sort;
        set
        {
            if (value is not null && Set(ref _sort, value))
                ApplyFilter();
        }
    }

    public ProcessRow? Selected
    {
        get => _selected;
        set
        {
            if (Set(ref _selected, value))
            {
                OnPropertyChanged(nameof(HasSelection));
                OnPropertyChanged(nameof(NoSelection));
                TerminateCommand.RaiseCanExecuteChanged();
                VirusTotalCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool HasSelection => Selected is not null;

    public bool NoSelection => Selected is null;

    public string ProcessSummary
    {
        get => _processSummary;
        private set => Set(ref _processSummary, value);
    }

    public AsyncRelayCommand TerminateCommand { get; }

    public AsyncRelayCommand RevealProcessCommand { get; }

    public AsyncRelayCommand SearchWebCommand { get; }

    public AsyncRelayCommand VirusTotalCommand { get; }

    // ===== «Автозагрузка»

    public ObservableCollection<StartupGroup> StartupGroups { get; } = new();

    public bool ShowApple
    {
        get => _showApple;
        set
        {
            if (Set(ref _showApple, value))
                _ = RunBusyAsync(value ? "Читаем записи macOS…" : "Обновляем автозагрузку…", LoadStartupAsync);
        }
    }

    public string? StartupProblems
    {
        get => _startupProblems;
        private set
        {
            if (Set(ref _startupProblems, value))
                OnPropertyChanged(nameof(HasStartupProblems));
        }
    }

    public bool HasStartupProblems => !string.IsNullOrEmpty(StartupProblems);

    public ObservableCollection<LoginItemRow> LoginItems { get; } = new();

    public bool LoginItemsLoaded
    {
        get => _loginItemsLoaded;
        private set
        {
            if (Set(ref _loginItemsLoaded, value))
                OnPropertyChanged(nameof(LoginItemsNotLoaded));
        }
    }

    public bool LoginItemsNotLoaded => !LoginItemsLoaded;

    public string? LoginItemsStatus
    {
        get => _loginItemsStatus;
        private set
        {
            if (Set(ref _loginItemsStatus, value))
                OnPropertyChanged(nameof(HasLoginItemsStatus));
        }
    }

    public bool HasLoginItemsStatus => !string.IsNullOrEmpty(LoginItemsStatus);

    public bool LoginItemsNeedPermission
    {
        get => _loginItemsNeedPermission;
        private set => Set(ref _loginItemsNeedPermission, value);
    }

    public AsyncRelayCommand LoadLoginItemsCommand { get; }

    public AsyncRelayCommand OpenLoginItemsSettingsCommand { get; }

    public AsyncRelayCommand OpenAutomationSettingsCommand { get; }

    // ===== Loading

    /// <summary>The first visit reads processes and startup items once; later visits (and callers) wait for that.</summary>
    public override Task OnNavigatedToAsync() => _loading ??= RunBusyAsync("Смотрим процессы и автозагрузку…", RefreshAsync);

    public override Task PreviewScanAsync(CancellationToken ct) => RefreshAsync(ct);

    public async Task RefreshAsync(CancellationToken ct)
    {
        await LoadProcessesAsync(ct);
        await LoadStartupAsync(ct);
    }

    private async Task LoadProcessesAsync(CancellationToken ct)
    {
        var list = await _processes.ListAsync(ct);
        Ui.Invoke(() =>
        {
            var selectedPid = Selected?.Entry.Info.Pid;
            _allProcesses = list.Items.Select(e => new ProcessRow(e, _processes.OwnUid)).ToList();
            ApplyFilter();
            Selected = Processes.FirstOrDefault(r => r.Entry.Info.Pid == selectedPid);
            if (list.Error is not null)
                ProcessSummary = "Не удалось получить список: " + list.Error;
        });
    }

    private async Task LoadStartupAsync(CancellationToken ct)
    {
        var list = await _startup.ListAsync(ShowApple, ct);
        Ui.Invoke(() =>
        {
            StartupGroups.Clear();
            foreach (var group in list.Items.GroupBy(e => e.Source).OrderBy(g => g.Key))
                StartupGroups.Add(new StartupGroup(group.Key, group.Select(e => new StartupRow(e, ToggleAsync, RemoveAsync, RevealPlistAsync)).ToList()));
            StartupProblems = list.Problems.Count == 0 ? null : string.Join("\n", list.Problems);
        });
    }

    private void ApplyFilter()
    {
        var search = SearchText.Trim();
        var rows = _allProcesses.Where(r => !HideSystem || !r.Entry.IsSystem || r.HasStrongHint);
        if (search.Length > 0)
        {
            rows = rows.Where(r => r.Name.Contains(search, StringComparison.CurrentCultureIgnoreCase)
                                   || r.PathText.Contains(search, StringComparison.CurrentCultureIgnoreCase));
        }
        var shown = Sort.Apply(rows).ToList();
        Processes.Clear();
        foreach (var row in shown)
            Processes.Add(row);
        var suspicious = _allProcesses.Count(r => r.HasStrongHint);
        ProcessSummary = $"Запущено {_allProcesses.Count} {Format.Plural(_allProcesses.Count, "процесс", "процесса", "процессов")}, показано {shown.Count}"
                         + (suspicious > 0 ? $"; с важными подсказками: {suspicious}" : "") + ".";
    }

    // ===== Process actions

    public async Task TerminateAsync(ProcessRow row)
    {
        if (!row.CanTerminate)
            return;
        if (!await Dialogs.ConfirmAsync("Завершить процесс",
                $"Завершить «{row.Name}»?\n\n{row.PathText}\n\nНесохранённые данные в этой программе могут пропасть."))
        {
            Log.Info($"Завершение «{row.Name}» отменено — ничего не изменено");
            return;
        }
        // Waiting for the process to end takes a few seconds: show it as busy work (it can be cancelled).
        TerminateResult? result = null;
        await RunBusyAsync($"Завершаем «{row.Name}»…", async ct => result = await _processes.TerminateAsync(row.Entry, force: false, ct));
        if (result is null)
            return;
        if (result.StillRunning && !result.Ok &&
            await Dialogs.ConfirmAsync("Завершить принудительно", $"{result.Message}\n\nЗавершить «{row.Name}» принудительно? Несохранённое пропадёт."))
        {
            await RunBusyAsync($"Завершаем «{row.Name}» принудительно…", async ct => result = await _processes.TerminateAsync(row.Entry, force: true, ct));
        }
        LastResult = result.Message;
        await RunBusyAsync("Обновляем список процессов…", LoadProcessesAsync);
    }

    private async Task VirusTotalAsync(ProcessRow row)
    {
        var hash = await ProcessesService.Sha256Async(row.Entry.Info.Executable, CancellationToken.None);
        if (hash is null)
        {
            await Dialogs.ErrorAsync("VirusTotal", $"Не удалось прочитать файл «{row.PathText}», чтобы посчитать его отпечаток.");
            return;
        }
        // Only the SHA-256 goes into the address: the file itself is not sent anywhere.
        await _shell.OpenAsync(WebLinks.VirusTotal(hash));
    }

    // ===== Startup actions

    public async Task ToggleAsync(StartupRow row)
    {
        var entry = row.Entry;
        if (!row.CanToggle)
            return;
        var password = entry.NeedsAdmin ? "\n\nmacOS попросит пароль администратора." : "";
        var enabling = entry.Disabled;
        var question = enabling
            ? $"Включить «{entry.Label}»? Она снова будет запускаться вместе с Mac.{password}"
            : $"Отключить «{entry.Label}»? Она перестанет запускаться вместе с Mac.\n\nЭто обратимо: включить можно здесь же, копия сохранится в «Настройках».{password}";
        if (!await Dialogs.ConfirmAsync(enabling ? "Включить запись автозагрузки" : "Отключить запись автозагрузки", question))
        {
            Log.Info($"Автозагрузка: «{entry.Label}» — отменено, ничего не изменено");
            return;
        }
        await RunBusyAsync(enabling ? "Включаем…" : "Отключаем…", async ct =>
        {
            var result = enabling ? await _startup.EnableAsync(entry, ct) : await _startup.DisableAsync(entry, ct);
            LastResult = result.Message;
            await LoadStartupAsync(ct);
        });
    }

    public async Task RemoveAsync(StartupRow row)
    {
        var entry = row.Entry;
        if (!entry.CanRemove)
            return;
        if (!await Dialogs.ConfirmAsync("Удалить запись автозагрузки",
                $"Удалить «{entry.Label}»?\n\n{entry.PlistPath}\n\nФайл уйдёт в Корзину, а копия сохранится в «Настройках» — запись можно будет вернуть. " +
                "Сама программа не удаляется."))
        {
            Log.Info($"Автозагрузка: удаление «{entry.Label}» отменено — ничего не изменено");
            return;
        }
        await RunBusyAsync("Удаляем…", async ct =>
        {
            LastResult = (await _startup.RemoveAsync(entry, ct)).Message;
            await LoadStartupAsync(ct);
        });
    }

    private Task RevealPlistAsync(StartupRow row) => row.Entry.PlistPath is { } path ? _shell.RevealAsync(path) : Task.CompletedTask;

    private async Task LoadLoginItemsAsync()
    {
        await RunBusyAsync("Читаем объекты входа…", async ct =>
        {
            var result = await _loginItems.ListAsync(ct);
            Ui.Invoke(() =>
            {
                LoginItems.Clear();
                foreach (var item in result.Items)
                    LoginItems.Add(new LoginItemRow(item, RemoveLoginItemAsync));
                LoginItemsNeedPermission = result.NeedsPermission;
                LoginItemsStatus = result.Error ?? (result.Items.Count == 0 ? "Объектов входа нет — при входе ничего не открывается само." : null);
                LoginItemsLoaded = result.Error is null;
            });
        });
    }

    public async Task RemoveLoginItemAsync(LoginItemRow row)
    {
        if (!await Dialogs.ConfirmAsync("Объекты входа",
                $"Убрать «{row.Name}» из объектов входа? Программа останется, просто перестанет открываться при входе.\n\nВернуть можно в «Настройках» → «Резервные копии»."))
            return;
        await RunBusyAsync("Убираем из объектов входа…", async ct =>
        {
            var result = await _loginItems.RemoveAsync(row.Item, ct);
            LastResult = result.Message;
            if (result.Ok)
                Ui.Invoke(() => LoginItems.Remove(row));
        });
    }
}
