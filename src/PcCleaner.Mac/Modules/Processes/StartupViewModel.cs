using System.Collections.ObjectModel;
using PcCleaner.Core;
using PcCleaner.Modules.Startup;

namespace PcCleaner.Modules.Processes;

/// <summary>
/// «Автозагрузка» (menu «Система»): launchd agents and daemons by source with reversible «Отключить»/«Включить», «Удалить» for the
/// user's own, and the login items (read only on a button, since macOS asks for permission to control System Events);
/// every change only after a «Да». Was the second tab of «Процессы и автозагрузка».
/// </summary>
public sealed class StartupViewModel : PageViewModel
{
    private readonly StartupService _startup;
    private readonly LoginItemsService _loginItems;
    private readonly MacShell _shell;
    private Task? _loading;
    private string? _lastResult;
    private bool _showApple;
    private string? _startupProblems;
    private string? _loginItemsStatus;
    private bool _loginItemsNeedPermission;

    public StartupViewModel(StartupService startup, LoginItemsService loginItems, MacShell shell)
    {
        _startup = startup;
        _loginItems = loginItems;
        _shell = shell;
        RefreshCommand = new AsyncRelayCommand(() => RunBusyAsync("Обновляем автозагрузку…", LoadStartupAsync), () => !IsBusy);
        LoadLoginItemsCommand = new AsyncRelayCommand(LoadLoginItemsAsync, () => !IsBusy);
        OpenLoginItemsSettingsCommand = new AsyncRelayCommand(() => _shell.OpenAsync(SystemSettingsLinks.LoginItems));
        OpenAutomationSettingsCommand = new AsyncRelayCommand(() => _shell.OpenAsync(SystemSettingsLinks.Automation));
    }

    public override string Title => "Автозагрузка";

    public override string Subtitle => "Что запускается вместе с Mac";

    public override string Icon => "Sync";

    public override string Key => "Startup";

    public AsyncRelayCommand RefreshCommand { get; }

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

    /// <summary>The first visit reads the startup items once; later visits (and callers) wait for that.</summary>
    public override Task OnNavigatedToAsync() => _loading ??= RunBusyAsync("Смотрим автозагрузку…", LoadStartupAsync);

    public override Task PreviewScanAsync(CancellationToken ct) => LoadStartupAsync(ct);

    public async Task LoadStartupAsync(CancellationToken ct)
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
