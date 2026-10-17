using System.Collections.ObjectModel;
using PcCleaner.Core;
using PcCleaner.Modules.Startup;

namespace PcCleaner.Modules.Settings;

/// <summary>«Настройки»: Full Disk Access, backups of startup changes, the log and where the program keeps its data.</summary>
public sealed class SettingsViewModel : PageViewModel
{
    private readonly MacShell _shell;
    private readonly Func<FdaState> _checkFda;
    private readonly BackupRestorer? _restorer;
    private FdaState _fda = FdaState.Unknown;

    public SettingsViewModel(MacShell shell, Func<FdaState> checkFda, BackupRestorer? restorer = null)
    {
        _shell = shell;
        _checkFda = checkFda;
        _restorer = restorer;
        OpenBackupsCommand = new AsyncRelayCommand(() => _shell.OpenAsync(AppPaths.EnsureDir(_restorer?.Store.Dir ?? AppPaths.BackupsDir)));
        RefreshFdaCommand = new RelayCommand(RefreshFda);
        OpenFdaSettingsCommand = new AsyncRelayCommand(() => _shell.OpenAsync(SystemSettingsLinks.FullDiskAccess));
        OpenRepoCommand = new AsyncRelayCommand(() => _shell.OpenAsync(AppInfo.RepoUrl));
        OpenLogsCommand = new AsyncRelayCommand(() => _shell.OpenAsync(AppPaths.EnsureDir(AppPaths.LogsDir)));
        OpenDataCommand = new AsyncRelayCommand(() => _shell.OpenAsync(AppPaths.EnsureDir(AppPaths.DataDir)));
    }

    public override string Title => "Настройки";

    public override string Subtitle => "Полный доступ к диску, резервные копии, журнал и сведения о программе";

    public override string Icon => "Settings";

    public override string Key => "Settings";

    public string VersionText => $"{AppInfo.Name} {AppInfo.Version} для macOS";

    public string RepoUrl => AppInfo.RepoUrl;

    public string DataDir => AppPaths.DataDir;

    public string LogsDir => AppPaths.LogsDir;

    public ObservableCollection<LogEntry> LogEntries => Log.Entries;

    /// <summary>Copies made before changes to startup items, the newest first.</summary>
    public ObservableCollection<BackupItem> Backups { get; } = new();

    public bool HasBackups => Backups.Count > 0;

    public bool NoBackups => Backups.Count == 0;

    public AsyncRelayCommand OpenBackupsCommand { get; }

    public FdaState Fda
    {
        get => _fda;
        private set
        {
            if (Set(ref _fda, value))
            {
                OnPropertyChanged(nameof(IsFdaGranted));
                OnPropertyChanged(nameof(IsFdaMissing));
                OnPropertyChanged(nameof(FdaText));
            }
        }
    }

    public bool IsFdaGranted => Fda == FdaState.Granted;

    public bool IsFdaMissing => Fda != FdaState.Granted;

    public string FdaText => Fda switch
    {
        FdaState.Granted => "Полный доступ к диску разрешён — проверяются Safari, Корзина и контейнеры программ.",
        FdaState.Denied => "Полного доступа к диску нет — Safari, Корзина и контейнеры программ не проверяются. Остальное работает.",
        _ => "Не удалось узнать, есть ли полный доступ к диску.",
    };

    public RelayCommand RefreshFdaCommand { get; }

    public AsyncRelayCommand OpenFdaSettingsCommand { get; }

    public AsyncRelayCommand OpenRepoCommand { get; }

    public AsyncRelayCommand OpenLogsCommand { get; }

    public AsyncRelayCommand OpenDataCommand { get; }

    public override Task OnNavigatedToAsync()
    {
        RefreshFda();
        RefreshBackups();
        return Task.CompletedTask;
    }

    internal void RefreshBackups()
    {
        Backups.Clear();
        foreach (var record in _restorer?.Store.List() ?? Array.Empty<BackupRecord>())
            Backups.Add(new BackupItem(record, RestoreAsync, ShowAsync));
        OnPropertyChanged(nameof(HasBackups));
        OnPropertyChanged(nameof(NoBackups));
    }

    internal async Task RestoreAsync(BackupItem item)
    {
        if (_restorer is null)
            return;
        var question = $"{item.Title} ({item.WhatText}).\n\n{BackupRestorer.WhatRestoreDoes(item.Record)}";
        if (!await Dialogs.ConfirmAsync("Восстановить из резервной копии?", question))
            return;
        StartupResult? result = null;
        await RunBusyAsync("Восстанавливаю…", async ct => result = await _restorer.RestoreAsync(item.Record, ct));
        if (result is null)
            return;
        if (result.Ok)
            await Dialogs.InfoAsync("Восстановлено", result.Message);
        else
            await Dialogs.ErrorAsync("Не удалось восстановить", result.Message);
        RefreshBackups();
    }

    private Task ShowAsync(BackupItem item)
        => item.Record.StoredFile is { } file ? _shell.RevealAsync(file) : _shell.OpenAsync(AppPaths.EnsureDir(_restorer?.Store.Dir ?? AppPaths.BackupsDir));

    public override Task PreviewScanAsync(CancellationToken ct) => OnNavigatedToAsync();

    private void RefreshFda() => Fda = _checkFda();
}
