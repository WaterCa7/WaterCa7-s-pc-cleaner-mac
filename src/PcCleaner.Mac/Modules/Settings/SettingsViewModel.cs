using System.Collections.ObjectModel;
using PcCleaner.Core;

namespace PcCleaner.Modules.Settings;

/// <summary>«Настройки»: Full Disk Access, the log and where the program keeps its data.</summary>
public sealed class SettingsViewModel : PageViewModel
{
    private readonly MacShell _shell;
    private readonly Func<FdaState> _checkFda;
    private FdaState _fda = FdaState.Unknown;

    public SettingsViewModel(MacShell shell, Func<FdaState> checkFda)
    {
        _shell = shell;
        _checkFda = checkFda;
        RefreshFdaCommand = new RelayCommand(RefreshFda);
        OpenFdaSettingsCommand = new AsyncRelayCommand(() => _shell.OpenAsync(SystemSettingsLinks.FullDiskAccess));
        OpenRepoCommand = new AsyncRelayCommand(() => _shell.OpenAsync(AppInfo.RepoUrl));
        OpenLogsCommand = new AsyncRelayCommand(() => _shell.OpenAsync(AppPaths.EnsureDir(AppPaths.LogsDir)));
        OpenDataCommand = new AsyncRelayCommand(() => _shell.OpenAsync(AppPaths.EnsureDir(AppPaths.DataDir)));
    }

    public override string Title => "Настройки";

    public override string Subtitle => "Полный доступ к диску, журнал и сведения о программе";

    public override string Icon => "Settings";

    public override string Key => "Settings";

    public string VersionText => $"{AppInfo.Name} {AppInfo.Version} для macOS";

    public string RepoUrl => AppInfo.RepoUrl;

    public string DataDir => AppPaths.DataDir;

    public string LogsDir => AppPaths.LogsDir;

    public ObservableCollection<LogEntry> LogEntries => Log.Entries;

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
        return Task.CompletedTask;
    }

    public override Task PreviewScanAsync(CancellationToken ct) => OnNavigatedToAsync();

    private void RefreshFda() => Fda = _checkFda();
}
