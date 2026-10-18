using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using PcCleaner.Core;
using PcCleaner.Core.Updates;
using PcCleaner.Shell;

namespace PcCleaner;

public partial class App : Application
{
    private MainViewModel? _main;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    /// <summary>The normal start (with a window). Tests and the screenshot mode set up the app themselves.</summary>
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            Ui.Attach(Dispatcher.UIThread);
            AppDomain.CurrentDomain.UnhandledException += (_, e) => Log.Error("Необработанная ошибка", e.ExceptionObject as Exception);
            TaskScheduler.UnobservedTaskException += (_, e) =>
            {
                Log.Error("Ошибка фоновой задачи", e.Exception);
                e.SetObserved();
            };
            Dialogs.ConfirmHandler = DialogWindow.ConfirmAsync;
            Dialogs.MessageHandler = DialogWindow.ShowAsync;
            AppServices.Init();
            Log.Info($"{AppInfo.Name} {AppInfo.Version} запущен");

            // A downloaded update goes in before the window: the new version starts and this one quits.
            var args = desktop.Args ?? Array.Empty<string>();
            var updates = new UpdateService(UpdateEnvironment.ForCurrentProcess(AppServices.Runner), () => AppServices.Settings);
            if (Task.Run(() => updates.TryInstallStagedAsync(args)).GetAwaiter().GetResult())
            {
                Environment.Exit(0);
                return;
            }
            if (UpdatedFrom(args) is { } from)
                updates.CompleteUpdate(from);

            _main = PageFactory.CreateMain(AppServices.Runner, AppServices.Settings);
            var shell = new MacShell(AppServices.Runner);
            _main.Updates = updates;
            _main.OpenUrl = url => _ = shell.OpenAsync(url);
            _main.InstallUpdateNow = async () =>
            {
                if (await Task.Run(() => updates.InstallNowAsync(args)))
                    desktop.Shutdown();
            };
            foreach (var settings in _main.Pages.OfType<Modules.Settings.SettingsViewModel>())
                settings.UseUpdates(updates, AppServices.Settings);

            var window = new MainWindow { DataContext = _main };
            foreach (var dashboard in _main.Pages.OfType<Modules.Dashboard.DashboardViewModel>())
                dashboard.ReportOutput = new WindowReportOutput(window);
            window.Opened += (_, _) => _ = Task.Run(() => updates.CheckAsync(manual: false, CancellationToken.None));
            desktop.MainWindow = window;
        }
        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>"--updated-from 1.0.0": the previous version started this one after installing it.</summary>
    private static string? UpdatedFrom(IReadOnlyList<string> args)
    {
        for (var i = 0; i + 1 < args.Count; i++)
        {
            if (args[i] == UpdateService.UpdatedFromArg)
                return args[i + 1];
        }
        return null;
    }

    private void OnAbout(object? sender, EventArgs e)
        => _ = Dialogs.InfoAsync("О программе",
            $"{AppInfo.Name} {AppInfo.Version} для macOS\n\nОчистка и проверка безопасности Mac.\n{AppInfo.RepoUrl}\n\n© 2026 WaterCa7, лицензия MIT.");

    private void OnSettings(object? sender, EventArgs e) => _main?.NavigateTo("Settings");
}
