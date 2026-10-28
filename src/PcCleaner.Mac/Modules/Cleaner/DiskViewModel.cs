using System.Collections.ObjectModel;
using PcCleaner.Core;
using PcCleaner.Modules.Dashboard;

namespace PcCleaner.Modules.Cleaner;

/// <summary>
/// «Диски» (menu «Система»): the volumes and Time Machine snapshots, the biggest folders of the home folder, large files and old
/// downloads, each moved to the Trash only after a «Да». Was the second tab of «Очистка и диски».
/// </summary>
public sealed class DiskViewModel : PageViewModel
{
    public static readonly IReadOnlyList<ThresholdOption> Thresholds = new[]
    {
        new ThresholdOption("от 100 МБ", 100L << 20),
        new ThresholdOption("от 500 МБ", 500L << 20),
        new ThresholdOption("от 1 ГБ", 1L << 30),
    };

    private readonly DiskService _disks;
    private readonly SpaceAnalyzer _analyzer;
    private readonly ITrash _trash;
    private readonly MacShell _shell;
    private readonly Func<DateTime> _nowUtc;
    private readonly string _home;
    private Task? _loading;
    private string _snapshotsText = "";
    private string? _analysisStatus;
    private ThresholdOption _threshold = Thresholds[1];

    public DiskViewModel(DiskService disks, SpaceAnalyzer analyzer, ITrash trash, MacShell shell, string home, Func<DateTime>? nowUtc = null)
    {
        _disks = disks;
        _analyzer = analyzer;
        _trash = trash;
        _shell = shell;
        _home = home;
        _nowUtc = nowUtc ?? (() => DateTime.UtcNow);
        RefreshCommand = new AsyncRelayCommand(() => RunBusyAsync("Смотрим диски…", RefreshAsync), () => !IsBusy);
        FindTopFoldersCommand = new AsyncRelayCommand(() => RunBusyAsync("Считаем размер папок…", FindTopFoldersAsync), () => !IsBusy);
        FindLargeFilesCommand = new AsyncRelayCommand(() => RunBusyAsync("Ищем большие файлы…", FindLargeFilesAsync), () => !IsBusy);
        FindOldDownloadsCommand = new AsyncRelayCommand(() => RunBusyAsync("Ищем старые загрузки…", FindOldDownloadsAsync), () => !IsBusy);
        TopFolders.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasTopFolders));
        LargeFiles.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasLargeFiles));
        OldDownloads.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasOldDownloads));
    }

    public override string Title => "Диски";

    public override string Subtitle => "Место на дисках, самые большие папки, большие файлы и старые загрузки";

    public override string Icon => "HardDrive";

    public override string Key => "Disk";

    public AsyncRelayCommand RefreshCommand { get; }

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

    /// <summary>The first visit reads the volumes once; later visits (and callers) wait for that.</summary>
    public override Task OnNavigatedToAsync() => _loading ??= RunBusyAsync("Смотрим диски…", RefreshAsync);

    /// <summary>The volumes and snapshots (read-only; the user's folders are walked only on a button).</summary>
    public override Task PreviewScanAsync(CancellationToken ct) => RefreshAsync(ct);

    public async Task RefreshAsync(CancellationToken ct)
    {
        var disks = await _disks.LoadAsync(ct);
        Ui.Invoke(() =>
        {
            Volumes.Clear();
            foreach (var volume in disks.Volumes)
                Volumes.Add(VolumeItem.From(volume));
            SnapshotsText = SnapshotText(disks);
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
