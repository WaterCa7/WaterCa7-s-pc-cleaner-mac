using System.Collections.ObjectModel;
using System.Text;
using PcCleaner.Core;
using PcCleaner.Modules.Dashboard;

namespace PcCleaner.Modules.Cleaner;

/// <summary>
/// «Очистка»: categories with check boxes, «Очистить выбранное» (regenerable files only, after a confirmation that lists them)
/// and «Очистить корзину» (its own confirmation). The disks and the analysis of the user's folders are the page «Диски».
/// </summary>
public sealed class CleanerViewModel : PageViewModel
{
    private readonly CleanerService _cleaner;
    private readonly MacShell _shell;
    private readonly Func<FdaState> _fda;
    private Task? _loading;
    private bool _hasScan;
    private string? _lastResult;
    private bool _showFdaHint;

    public CleanerViewModel(CleanerService cleaner, ITrash trash, MacShell shell, Func<FdaState> fda, string home)
    {
        _cleaner = cleaner;
        _shell = shell;
        _fda = fda;

        foreach (var category in CleanerCatalog.All.Where(c => c.Action == CleanAction.Delete))
            Categories.Add(new CategoryItem(category, category != CleanerCatalog.Developer, OnSelectionChanged));
        TrashCategory = new CategoryItem(CleanerCatalog.Trash, false, () => { });

        ScanCommand = new AsyncRelayCommand(() => RunBusyAsync("Проверяем, что можно очистить…", RefreshAsync), () => !IsBusy);
        CleanCommand = new AsyncRelayCommand(CleanAsync, () => !IsBusy && SelectedBytes > 0);
        EmptyTrashCommand = new AsyncRelayCommand(EmptyTrashAsync, () => !IsBusy);
        OpenFdaSettingsCommand = new AsyncRelayCommand(() => _shell.OpenAsync(SystemSettingsLinks.FullDiskAccess));
    }

    public override string Title => "Очистка";

    public override string Subtitle => "Кэши, журналы и Корзина";

    public override string Icon => "Broom";

    public override string Key => "Cleaner";

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

    // ===== Navigation

    /// <summary>The first visit checks the categories once; later visits (and callers) wait for that check.</summary>
    public override Task OnNavigatedToAsync() => _loading ??= RunBusyAsync("Проверяем, что можно очистить…", RefreshAsync);

    /// <summary>The categories (read-only).</summary>
    public override Task PreviewScanAsync(CancellationToken ct) => RefreshAsync(ct);

    public async Task RefreshAsync(CancellationToken ct)
    {
        var scans = await _cleaner.ScanAllAsync(ct);
        Ui.Invoke(() =>
        {
            foreach (var scan in scans)
            {
                var item = scan.Category == CleanerCatalog.Trash ? TrashCategory : Categories.FirstOrDefault(c => c.Category == scan.Category);
                item?.Update(scan);
            }
            ShowFdaHint = _fda() != FdaState.Granted;
            HasScan = true;
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

    private void OnSelectionChanged()
    {
        OnPropertyChanged(nameof(SelectedBytes));
        OnPropertyChanged(nameof(CleanButtonText));
        CleanCommand.RaiseCanExecuteChanged();
    }
}
