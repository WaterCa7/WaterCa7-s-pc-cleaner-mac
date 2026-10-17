using System.Collections.ObjectModel;
using System.Text;
using PcCleaner.Core;
using PcCleaner.Modules.Dashboard;

namespace PcCleaner.Modules.Cleaner;

/// <summary>
/// «Очистка и диски». Tab «Очистка»: categories with check boxes, «Очистить выбранное» (regenerable files only, after a
/// confirmation that lists them) and «Очистить корзину» (its own confirmation). Tab «Диски и место»: volumes, local
/// snapshots, and the analysis of the user's folders — run only on a button, since macOS may ask for access.
/// </summary>
public sealed class CleanerViewModel : PageViewModel
{
    public static readonly IReadOnlyList<ThresholdOption> Thresholds = new[]
    {
        new ThresholdOption("от 100 МБ", 100L << 20),
        new ThresholdOption("от 500 МБ", 500L << 20),
        new ThresholdOption("от 1 ГБ", 1L << 30),
    };

    private readonly CleanerService _cleaner;
    private readonly DiskService _disks;
    private readonly SpaceAnalyzer _analyzer;
    private readonly ITrash _trash;
    private readonly MacShell _shell;
    private readonly Func<FdaState> _fda;
    private readonly Func<DateTime> _nowUtc;
    private readonly string _home;
    private bool _loaded;
    private bool _hasScan;
    private string? _lastResult;
    private string _snapshotsText = "";
    private string? _analysisStatus;
    private ThresholdOption _threshold = Thresholds[1];
    private bool _showFdaHint;

    public CleanerViewModel(CleanerService cleaner, DiskService disks, SpaceAnalyzer analyzer, ITrash trash, MacShell shell,
        Func<FdaState> fda, string home, Func<DateTime>? nowUtc = null)
    {
        _cleaner = cleaner;
        _disks = disks;
        _analyzer = analyzer;
        _trash = trash;
        _shell = shell;
        _fda = fda;
        _home = home;
        _nowUtc = nowUtc ?? (() => DateTime.UtcNow);

        foreach (var category in CleanerCatalog.All.Where(c => c.Action == CleanAction.Delete))
            Categories.Add(new CategoryItem(category, category != CleanerCatalog.Developer, OnSelectionChanged));
        TrashCategory = new CategoryItem(CleanerCatalog.Trash, false, () => { });

        ScanCommand = new AsyncRelayCommand(() => RunBusyAsync("Проверяем, что можно очистить…", RefreshAsync), () => !IsBusy);
        CleanCommand = new AsyncRelayCommand(CleanAsync, () => !IsBusy && SelectedBytes > 0);
        EmptyTrashCommand = new AsyncRelayCommand(EmptyTrashAsync, () => !IsBusy);
        OpenFdaSettingsCommand = new AsyncRelayCommand(() => _shell.OpenAsync(SystemSettingsLinks.FullDiskAccess));
        FindTopFoldersCommand = new AsyncRelayCommand(() => RunBusyAsync("Считаем размер папок…", FindTopFoldersAsync), () => !IsBusy);
        FindLargeFilesCommand = new AsyncRelayCommand(() => RunBusyAsync("Ищем большие файлы…", FindLargeFilesAsync), () => !IsBusy);
        FindOldDownloadsCommand = new AsyncRelayCommand(() => RunBusyAsync("Ищем старые загрузки…", FindOldDownloadsAsync), () => !IsBusy);
        TopFolders.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasTopFolders));
        LargeFiles.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasLargeFiles));
        OldDownloads.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasOldDownloads));
    }

    public override string Title => "Очистка и диски";

    public override string Subtitle => "Кэши, журналы, Корзина и место на дисках";

    public override string Icon => "Broom";

    public override string Key => "Cleaner";

    // ===== «Очистка»

    public ObservableCollection<CategoryItem> Categories { get; } = new();

    public CategoryItem TrashCategory { get; }

    public bool HasScan
    {
        get => _hasScan;
        private set => Set(ref _hasScan, value);
    }

    public long SelectedBytes => Categories.Where(c => c.IsChecked).Sum(c => c.Bytes);

    public string CleanButtonText => SelectedBytes > 0 ? $"Очистить выбранное ({Format.Bytes(SelectedBytes)})" : "Очистить выбранное";

    /// <summary>What the last cleaning or Trash emptying did.</summary>
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

    public bool ShowFdaHint
    {
        get => _showFdaHint;
        private set => Set(ref _showFdaHint, value);
    }

    public AsyncRelayCommand ScanCommand { get; }

    public AsyncRelayCommand CleanCommand { get; }

    public AsyncRelayCommand EmptyTrashCommand { get; }

    public AsyncRelayCommand OpenFdaSettingsCommand { get; }

    // ===== «Диски и место»

    public ObservableCollection<VolumeItem> Volumes { get; } = new();

    public string SnapshotsText
    {
        get => _snapshotsText;
        private set => Set(ref _snapshotsText, value);
    }

    public ObservableCollection<FolderItem> TopFolders { get; } = new();

    public ObservableCollection<UserItemRow> LargeFiles { get; } = new();

    public ObservableCollection<UserItemRow> OldDownloads { get; } = new();

    public bool HasTopFolders => TopFolders.Count > 0;

    public bool HasLargeFiles => LargeFiles.Count > 0;

    public bool HasOldDownloads => OldDownloads.Count > 0;

    public IReadOnlyList<ThresholdOption> ThresholdChoices => Thresholds;

    public ThresholdOption Threshold
    {
        get => _threshold;
        set => Set(ref _threshold, value);
    }

    public string? AnalysisStatus
    {
        get => _analysisStatus;
        private set
        {
            if (Set(ref _analysisStatus, value))
                OnPropertyChanged(nameof(HasAnalysisStatus));
        }
    }

    public bool HasAnalysisStatus => !string.IsNullOrEmpty(AnalysisStatus);

    public AsyncRelayCommand FindTopFoldersCommand { get; }

    public AsyncRelayCommand FindLargeFilesCommand { get; }

    public AsyncRelayCommand FindOldDownloadsCommand { get; }

    // ===== Navigation

    public override async Task OnNavigatedToAsync()
    {
        if (_loaded)
            return;
        _loaded = true;
        await RunBusyAsync("Проверяем, что можно очистить…", RefreshAsync);
    }

    /// <summary>The categories and the volumes (read-only; the user's folders are not walked).</summary>
    public override Task PreviewScanAsync(CancellationToken ct) => RefreshAsync(ct);

    public async Task RefreshAsync(CancellationToken ct)
    {
        var scans = await _cleaner.ScanAllAsync(ct);
        var disks = await _disks.LoadAsync(ct);
        Ui.Invoke(() =>
        {
            foreach (var scan in scans)
            {
                var item = scan.Category == CleanerCatalog.Trash ? TrashCategory : Categories.FirstOrDefault(c => c.Category == scan.Category);
                item?.Update(scan);
            }
            ShowFdaHint = _fda() != FdaState.Granted;
            HasScan = true;
            Volumes.Clear();
            foreach (var volume in disks.Volumes)
                Volumes.Add(VolumeItem.From(volume));
            SnapshotsText = SnapshotText(disks);
            OnSelectionChanged();
        });
    }

    // ===== Actions

    /// <summary>«Очистить выбранное»: one confirmation that lists the categories, then the deletion.</summary>
    public async Task CleanAsync()
    {
        var chosen = Categories.Where(c => c.IsChecked && c.Bytes > 0).ToList();
        if (chosen.Count == 0)
        {
            await Dialogs.InfoAsync(Title, "Нечего очищать: отметьте категории, в которых найдены файлы.");
            return;
        }
        var message = new StringBuilder("Будут удалены без Корзины — программы создадут эти файлы заново:\n\n");
        foreach (var item in chosen)
            message.Append("• ").Append(item.Title).Append(" — ").Append(item.SizeText).Append('\n');
        message.Append("\nПароли, история браузеров и ваши документы не затрагиваются. Продолжить?");
        if (!await Dialogs.ConfirmAsync("Очистить выбранное", message.ToString()))
        {
            Log.Info("Очистка отменена — ничего не изменено");
            return;
        }
        await RunBusyAsync("Очищаем…", async ct =>
        {
            var result = await _cleaner.CleanAsync(chosen.Select(c => c.Category).ToList(), new Progress<double>(p => ReportProgress(p)), ct);
            LastResult = result.Message;
            await RefreshAsync(CancellationToken.None);
        });
    }

    /// <summary>«Очистить корзину»: its own confirmation, because nothing can be brought back after it.</summary>
    public async Task EmptyTrashAsync()
    {
        var size = TrashCategory.Bytes > 0 ? $"В Корзине {TrashCategory.SizeText}. " : "";
        if (!await Dialogs.ConfirmAsync("Очистить корзину",
                $"{size}Файлы из Корзины будут удалены безвозвратно — вернуть их будет нельзя.\n\nmacOS может спросить разрешение " +
                "программе управлять Finder — это нужно, чтобы Finder очистил Корзину.\n\nОчистить Корзину?"))
        {
            Log.Info("Очистка Корзины отменена — ничего не изменено");
            return;
        }
        await RunBusyAsync("Очищаем Корзину…", async ct =>
        {
            LastResult = await _cleaner.EmptyTrashAsync(ct);
            await RefreshAsync(CancellationToken.None);
        });
    }

    public async Task MoveToTrashAsync(UserItemRow row)
    {
        var what = row.Item.IsFolder ? "папку" : "файл";
        if (!await Dialogs.ConfirmAsync("В Корзину",
                $"Переместить {what} в Корзину?\n\n{row.Item.Path}\n{row.SizeText}\n\nЕго можно будет вернуть из Корзины (Finder → «Вернуть»)."))
            return;
        var outcome = await _trash.MoveAsync(row.Item.Path, CancellationToken.None);
        if (outcome.Ok)
        {
            Ui.Invoke(() =>
            {
                LargeFiles.Remove(row);
                OldDownloads.Remove(row);
                AnalysisStatus = $"Перемещено в Корзину: {row.Name} ({row.SizeText}).";
            });
        }
        else
        {
            await Dialogs.ErrorAsync("В Корзину", $"Не удалось переместить «{row.Name}» в Корзину: {outcome.Error}.");
        }
    }

    private async Task FindTopFoldersAsync(CancellationToken ct)
    {
        var folders = await _analyzer.TopFoldersAsync(ct);
        var biggest = folders.Count > 0 ? folders.Max(f => f.Bytes) : 0;
        Ui.Invoke(() =>
        {
            TopFolders.Clear();
            foreach (var folder in folders)
                TopFolders.Add(new FolderItem(folder, biggest, path => _shell.OpenAsync(path)));
            AnalysisStatus = folders.Count == 0 ? "В домашней папке нет папок." : null;
        });
    }

    private async Task FindLargeFilesAsync(CancellationToken ct)
    {
        var files = await _analyzer.LargeFilesAsync(Threshold.Bytes, ct);
        Ui.Invoke(() =>
        {
            Fill(LargeFiles, files);
            AnalysisStatus = files.Count == 0 ? $"Файлов {Threshold.Text} не найдено." : null;
        });
    }

    private async Task FindOldDownloadsAsync(CancellationToken ct)
    {
        var items = await _analyzer.OldDownloadsAsync(90, _nowUtc(), ct);
        Ui.Invoke(() =>
        {
            Fill(OldDownloads, items);
            AnalysisStatus = items.Count == 0 ? "В «Загрузках» нет того, что не открывалось больше 90 дней." : null;
        });
    }

    private void Fill(ObservableCollection<UserItemRow> rows, IReadOnlyList<UserItem> items)
    {
        rows.Clear();
        foreach (var item in items)
            rows.Add(new UserItemRow(item, _home, path => _shell.RevealAsync(path), MoveToTrashAsync));
    }

    private void OnSelectionChanged()
    {
        OnPropertyChanged(nameof(SelectedBytes));
        OnPropertyChanged(nameof(CleanButtonText));
        CleanCommand.RaiseCanExecuteChanged();
    }

    private static string SnapshotText(DisksInfo disks)
    {
        if (!disks.SnapshotsKnown)
            return "Не удалось узнать про локальные снимки Time Machine.";
        if (disks.Snapshots.Count == 0)
            return "Локальных снимков Time Machine нет.";
        return $"Локальных снимков Time Machine: {disks.Snapshots.Count} (последний — " +
               $"{HealthScoring.RelativeTime(disks.Snapshots[^1], DateTime.Now)}). " +
               "macOS удаляет их сама, когда нужно место; их место входит в «может освободить сама».";
    }
}
