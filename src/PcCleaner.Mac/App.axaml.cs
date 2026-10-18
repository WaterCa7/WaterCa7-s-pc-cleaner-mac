using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using PcCleaner.Core;
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
            _main = PageFactory.CreateMain(AppServices.Runner, AppServices.Settings);
            var window = new MainWindow { DataContext = _main };
            foreach (var dashboard in _main.Pages.OfType<Modules.Dashboard.DashboardViewModel>())
                dashboard.ReportOutput = new WindowReportOutput(window);
            desktop.MainWindow = window;
        }
        base.OnFrameworkInitializationCompleted();
    }

    private void OnAbout(object? sender, EventArgs e)
        => _ = Dialogs.InfoAsync("О программе",
            $"{AppInfo.Name} {AppInfo.Version} для macOS\n\nОчистка и проверка безопасности Mac.\n{AppInfo.RepoUrl}\n\n© 2026 WaterCa7, лицензия MIT.");

    private void OnSettings(object? sender, EventArgs e) => _main?.NavigateTo("Settings");
}
