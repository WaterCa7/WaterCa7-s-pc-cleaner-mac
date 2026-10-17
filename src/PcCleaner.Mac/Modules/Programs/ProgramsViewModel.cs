using System.Collections.ObjectModel;
using System.Text;
using PcCleaner.Core;

namespace PcCleaner.Modules.Programs;

/// <summary>
/// «Программы»: installed apps with search, filters and sorting; the selected app's details and leftovers. Removal asks
/// first with the full list, then asks to close the app if it runs (stopping it is a separate, warned choice), offers the
/// app's own uninstaller, and only then moves the app and the ticked leftovers to the Trash.
/// </summary>
public sealed class ProgramsViewModel : PageViewModel
{
    public static readonly IReadOnlyList<Choice<AppFilter>> Filters = new[]
    {
        new Choice<AppFilter>("Все программы", AppFilter.All),
        new Choice<AppFilter>("Давно не запускались", AppFilter.LongUnused),
        new Choice<AppFilter>("Только для Intel", AppFilter.IntelOnly),
        new Choice<AppFilter>("Нежелательные", AppFilter.Unwanted),
        new Choice<AppFilter>("Из App Store", AppFilter.AppStore),
    };

    public static readonly IReadOnlyList<Choice<string>> Sorts = new[]
    {
        new Choice<string>("по имени", "name"),
        new Choice<string>("по размеру", "size"),
        new Choice<string>("по последнему запуску", "used"),
    };

    private readonly ProgramsService _programs;
    private readonly MacShell _shell;
    private readonly string _home;
    private readonly Func<DateTime> _nowUtc;
    private IReadOnlyList<AppRow> _all = Array.Empty<AppRow>();
    private Task? _loading;
    private CancellationTokenSource? _measuring;
    private CancellationTokenSource? _leftoverLoad;
    private string _searchText = "";
    private Choice<AppFilter> _filter = Filters[0];
    private Choice<string> _sort = Sorts[0];
    private bool _showApple;
    private AppRow? _selected;
    private string _summary = "";
    private string? _lastResult;
    private string? _problems;
    private bool _leftoversLoading;
    private bool _leftoversNeedFda;

    public ProgramsViewModel(ProgramsService programs, MacShell shell, string home, Func<DateTime>? nowUtc = null)
    {
        _programs = programs;
        _shell = shell;
        _home = home;
        _nowUtc = nowUtc ?? (() => DateTime.UtcNow);
        RefreshCommand = new AsyncRelayCommand(() => RunBusyAsync("Читаем список программ…", RefreshAsync), () => !IsBusy);
        UninstallCommand = new AsyncRelayCommand(() => Selected is { } row ? UninstallAsync(row) : Task.CompletedTask, () => Selected?.CanUninstall == true);
        RevealCommand = new AsyncRelayCommand(() => Selected is { } row ? _shell.RevealAsync(row.App.Path) : Task.CompletedTask);
        SearchWebCommand = new AsyncRelayCommand(() => Selected is { } row ? _shell.OpenAsync(WebLinks.Search($"{row.Name} mac")) : Task.CompletedTask);
        OpenFdaSettingsCommand = new AsyncRelayCommand(() => _shell.OpenAsync(SystemSettingsLinks.FullDiskAccess));
    }

    public override string Title => "Программы";

    public override string Subtitle => "Установленные программы и их остатки";

    public override string Icon => "Apps";

    public override string Key => "Programs";

    public ObservableCollection<AppRow> Apps { get; } = new();

    public ObservableCollection<LeftoverRow> Leftovers { get; } = new();

    public IReadOnlyList<Choice<AppFilter>> FilterChoices => Filters;

    public IReadOnlyList<Choice<string>> SortChoices => Sorts;

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (Set(ref _searchText, value ?? ""))
                ApplyFilter();
        }
    }

    public Choice<AppFilter> Filter
    {
        get => _filter;
        set
        {
            if (value is not null && Set(ref _filter, value))
                ApplyFilter();
        }
    }

    public Choice<string> Sort
    {
        get => _sort;
        set
        {
            if (value is not null && Set(ref _sort, value))
                ApplyFilter();
        }
    }

    /// <summary>Also list macOS's own apps (read-only).</summary>
    public bool ShowApple
    {
        get => _showApple;
        set
        {
            if (Set(ref _showApple, value))
                Reloading = RunBusyAsync("Читаем список программ…", RefreshAsync);
        }
    }

    public AppRow? Selected
    {
        get => _selected;
        set
        {
            if (!Set(ref _selected, value))
                return;
            OnPropertyChanged(nameof(HasSelection));
            OnPropertyChanged(nameof(NoSelection));
            UninstallCommand.RaiseCanExecuteChanged();
            _ = LoadLeftoversAsync(value);
        }
    }

    public bool HasSelection => Selected is not null;

    public bool NoSelection => Selected is null;

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

    public string? Problems
    {
        get => _problems;
        private set
        {
            if (Set(ref _problems, value))
                OnPropertyChanged(nameof(HasProblems));
        }
    }

    public bool HasProblems => !string.IsNullOrEmpty(Problems);

    public bool LeftoversLoading
    {
        get => _leftoversLoading;
        private set
        {
            if (Set(ref _leftoversLoading, value))
                OnPropertyChanged(nameof(LeftoversText));
        }
    }

    public bool LeftoversNeedFda
    {
        get => _leftoversNeedFda;
        private set => Set(ref _leftoversNeedFda, value);
    }

    public string LeftoversText => LeftoversLoading ? "Ищем остатки в ~/Library…"
        : Selected?.App.IsProtected == true ? ""
        : Leftovers.Count == 0 ? "Остатков в ~/Library не найдено."
        : $"Остатки в ~/Library — отмеченные уйдут в Корзину вместе с программой ({Format.Bytes(Leftovers.Where(l => l.IsChecked).Sum(l => l.Leftover.Bytes))}):";

    public AsyncRelayCommand RefreshCommand { get; }

    public AsyncRelayCommand UninstallCommand { get; }

    public AsyncRelayCommand RevealCommand { get; }

    public AsyncRelayCommand SearchWebCommand { get; }

    public AsyncRelayCommand OpenFdaSettingsCommand { get; }

    /// <summary>The reload started by «Показать встроенные программы macOS» (tests wait for it).</summary>
    public Task Reloading { get; private set; } = Task.CompletedTask;

    /// <summary>The background measuring of sizes (tests wait for it).</summary>
    public Task Measuring { get; private set; } = Task.CompletedTask;

    /// <summary>The leftover search of the selected app (tests wait for it).</summary>
    public Task LeftoverSearch { get; private set; } = Task.CompletedTask;

    // ===== Loading

    public override Task OnNavigatedToAsync() => _loading ??= RunBusyAsync("Читаем список программ…", RefreshAsync);

    public override Task PreviewScanAsync(CancellationToken ct) => RefreshAsync(ct);

    public async Task RefreshAsync(CancellationToken ct)
    {
        var list = await _programs.Catalog.ListAsync(ShowApple, ct);
        var now = _nowUtc();
        Ui.Invoke(() =>
        {
            var selectedPath = Selected?.App.Path;
            _all = list.Apps.Select(a => new AppRow(a, now)).ToList();
            ApplyFilter();
            Selected = Apps.FirstOrDefault(r => r.App.Path == selectedPath);
            Problems = list.Problems.Count == 0 ? null : string.Join("\n", list.Problems);
        });
        StartMeasuring();
    }

    private void StartMeasuring()
    {
        _measuring?.Cancel();
        var cts = new CancellationTokenSource();
        _measuring = cts;
        var rows = _all.Where(r => r.Size is null).OrderBy(r => r.App.IsApple).ToList();
        Measuring = Task.Run(async () =>
        {
            foreach (var row in rows)
            {
                if (cts.IsCancellationRequested)
                    return;
                try
                {
                    var size = await AppCatalog.MeasureAsync(row.App.Path, cts.Token);
                    Ui.Invoke(() => row.Size = size);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
            Ui.Invoke(() =>
            {
                UpdateSummary();
                if (Sort.Value == "size")
                    ApplyFilter();
            });
        });
    }

    private void ApplyFilter()
    {
        var search = SearchText.Trim();
        IEnumerable<AppRow> rows = _all.Where(r => Filter.Value switch
        {
            AppFilter.LongUnused => r.IsLongUnused,
            AppFilter.IntelOnly => r.IsIntelOnly,
            AppFilter.Unwanted => r.IsPup,
            AppFilter.AppStore => r.IsAppStore,
            _ => true,
        });
        if (search.Length > 0)
        {
            rows = rows.Where(r => r.Name.Contains(search, StringComparison.CurrentCultureIgnoreCase)
                                   || (r.App.BundleId?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false));
        }
        rows = Sort.Value switch
        {
            "size" => rows.OrderByDescending(r => r.Size ?? -1),
            "used" => rows.OrderBy(r => r.App.LastUsedUtc ?? DateTime.MinValue),
            _ => rows.OrderBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase),
        };
        var shown = rows.ToList();
        var selected = Selected;
        Apps.Clear();
        foreach (var row in shown)
            Apps.Add(row);
        if (selected is not null && !shown.Contains(selected))
            Selected = null;
        UpdateSummary();
    }

    private void UpdateSummary()
    {
        var own = _all.Where(r => !r.App.IsApple).ToList();
        var measured = own.Where(r => r.Size is not null).ToList();
        var size = measured.Count == own.Count && own.Count > 0 ? $", вместе {Format.Bytes(measured.Sum(r => r.Size!.Value))}" : "";
        var pups = own.Count(r => r.IsPup);
        Summary = $"Установлено {own.Count} {Format.Plural(own.Count, "программа", "программы", "программ")}{size}, показано {Apps.Count}" +
                  (pups > 0 ? $"; нежелательных: {pups}" : "") + ".";
    }

    private async Task LoadLeftoversAsync(AppRow? row)
    {
        _leftoverLoad?.Cancel();
        Leftovers.Clear();
        LeftoversNeedFda = false;
        OnPropertyChanged(nameof(LeftoversText));
        if (row is null || row.App.IsProtected)
            return;
        var cts = new CancellationTokenSource();
        _leftoverLoad = cts;
        LeftoversLoading = true;
        var search = _programs.LeftoversAsync(row.App, _all.Select(r => r.App).ToList(), cts.Token);
        LeftoverSearch = search;
        try
        {
            var scan = await search;
            if (cts.IsCancellationRequested || !ReferenceEquals(row, Selected))
                return;
            Ui.Invoke(() =>
            {
                Leftovers.Clear();
                foreach (var leftover in scan.Items)
                    Leftovers.Add(new LeftoverRow(leftover, _home, () => OnPropertyChanged(nameof(LeftoversText))));
                LeftoversNeedFda = scan.NeedsFullDiskAccess;
            });
        }
        catch (OperationCanceledException)
        {
            // Another app was selected.
        }
        finally
        {
            if (ReferenceEquals(_leftoverLoad, cts))
                LeftoversLoading = false;
        }
    }

    // ===== Removal

    public async Task UninstallAsync(AppRow row)
    {
        var app = row.App;
        if (app.IsProtected)
            return;
        if (!ReferenceEquals(row, Selected))
            Selected = row;
        await LeftoverSearch;
        var leftovers = Leftovers.Select(l => l.Current).ToList();

        if (!await Dialogs.ConfirmAsync($"Удалить «{app.Name}»", RemovalList(row, leftovers)))
        {
            Log.Info($"Удаление «{app.Name}» отменено — ничего не изменено");
            return;
        }

        if (!await EnsureClosedAsync(app))
            return;

        if (app.Uninstaller is { } uninstaller &&
            await Dialogs.AskAsync("Свой деинсталлятор",
                $"У «{app.Name}» есть свой деинсталлятор «{Path.GetFileNameWithoutExtension(uninstaller)}». Лучше удалить программу им: он уберёт " +
                "и то, что она поставила за пределами ~/Library (службы, расширения).\n\nОткрыть его? «Нет» — удалить обычным способом, в Корзину."))
        {
            await _shell.OpenAsync(uninstaller);
            LastResult = $"Открыт деинсталлятор «{Path.GetFileNameWithoutExtension(uninstaller)}». Когда он закончит, нажмите «Обновить».";
            Log.Action($"Программы: открыт деинсталлятор {uninstaller}");
            return;
        }

        await RunBusyAsync($"Удаляем «{app.Name}»…", async ct =>
        {
            var result = await _programs.UninstallAsync(app, leftovers, row.Size, ct);
            LastResult = result.Message;
            if (result.Ok)
                await RefreshAsync(ct);
        });
    }

    /// <summary>A running app is closed by the user; stopping it is a separate, warned choice.</summary>
    private async Task<bool> EnsureClosedAsync(InstalledApp app)
    {
        if ((await _programs.RunningAsync(app, CancellationToken.None)).Count == 0)
            return true;
        if (!await Dialogs.ConfirmAsync($"«{app.Name}» открыта",
                $"Закройте «{app.Name}» (⌘Q) и нажмите «Да».\n\n«Нет» — отменить удаление."))
        {
            Log.Info($"Удаление «{app.Name}» отменено — программа открыта");
            return false;
        }
        if ((await _programs.RunningAsync(app, CancellationToken.None)).Count == 0)
            return true;
        if (!await Dialogs.ConfirmAsync("Завершить принудительно",
                $"«{app.Name}» всё ещё работает. Завершить её принудительно? Несохранённые данные в ней пропадут.\n\n«Нет» — отменить удаление."))
        {
            Log.Info($"Удаление «{app.Name}» отменено — программа открыта");
            return false;
        }
        var closed = false;
        await RunBusyAsync($"Завершаем «{app.Name}»…", async ct => closed = await _programs.QuitAsync(app, force: true, ct));
        if (!closed)
            await Dialogs.ErrorAsync($"Удалить «{app.Name}»", $"«{app.Name}» не завершается — удаление отменено, ничего не изменено.");
        return closed;
    }

    private static string RemovalList(AppRow row, IReadOnlyList<Leftover> leftovers)
    {
        var text = new StringBuilder($"В Корзину переместятся:\n\n• программа «{row.Name}»");
        if (row.Size is { } size)
            text.Append(" — ").Append(Format.Bytes(size));
        text.Append('\n');
        var ticked = leftovers.Where(l => l.Checked).ToList();
        foreach (var leftover in ticked.Take(12))
            text.Append("• ").Append(leftover.What).Append(" — ").Append(Path.GetFileName(leftover.Path)).Append(" (").Append(Format.Bytes(leftover.Bytes)).Append(")\n");
        if (ticked.Count > 12)
            text.Append($"• и ещё {ticked.Count - 12}\n");
        text.Append("\nВсё можно будет вернуть из Корзины (Finder → «Вернуть»). Удалить?");
        return text.ToString();
    }
}
