using System.Collections.ObjectModel;
using PcCleaner.Core;

namespace PcCleaner.Modules.Processes;

/// <summary>
/// «Процессы» (menu «Система»): what runs now, with signatures and hints; the user's own processes can be stopped
/// (after a confirmation; «принудительно» only after a second one). The startup items are on their own page, «Автозагрузка».
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
    private readonly MacShell _shell;
    private IReadOnlyList<ProcessRow> _allProcesses = Array.Empty<ProcessRow>();
    private Task? _loading;
    private string _searchText = "";
    private bool _hideSystem = true;
    private ProcessSort _sort = Sorts[0];
    private ProcessRow? _selected;
    private string? _lastResult;
    private string _processSummary = "";

    public ProcessesViewModel(ProcessesService processes, MacShell shell)
    {
        _processes = processes;
        _shell = shell;

        RefreshCommand = new AsyncRelayCommand(() => RunBusyAsync("Смотрим процессы…", RefreshAsync), () => !IsBusy);
        TerminateCommand = new AsyncRelayCommand(() => Selected is { } row ? TerminateAsync(row) : Task.CompletedTask, () => Selected?.CanTerminate == true);
        RevealProcessCommand = new AsyncRelayCommand(() => Selected?.Entry.Location is { } path ? _shell.RevealAsync(path) : Task.CompletedTask);
        SearchWebCommand = new AsyncRelayCommand(() => Selected is { } row ? _shell.OpenAsync(WebLinks.Search($"{row.Name} macOS что это")) : Task.CompletedTask);
        VirusTotalCommand = new AsyncRelayCommand(() => Selected is { } row ? VirusTotalAsync(row) : Task.CompletedTask, () => Selected?.CanHash == true);
    }

    public override string Title => "Процессы";

    public override string Subtitle => "Что запущено на Mac сейчас";

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

    // ===== Loading

    /// <summary>The first visit reads the processes once; later visits (and callers) wait for that.</summary>
    public override Task OnNavigatedToAsync() => _loading ??= RunBusyAsync("Смотрим процессы…", RefreshAsync);

    public override Task PreviewScanAsync(CancellationToken ct) => RefreshAsync(ct);

    public async Task RefreshAsync(CancellationToken ct)
    {
        await LoadProcessesAsync(ct);
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
}
