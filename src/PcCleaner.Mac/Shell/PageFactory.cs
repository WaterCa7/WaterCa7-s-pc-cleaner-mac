using PcCleaner.Core;
using PcCleaner.Modules.Cleaner;
using PcCleaner.Modules.Dashboard;
using PcCleaner.Modules.Processes;
using PcCleaner.Modules.Settings;
using PcCleaner.Modules.Startup;

namespace PcCleaner.Shell;

/// <summary>Builds the window's pages in sidebar order.</summary>
public static class PageFactory
{
    /// <param name="startupFolders">Where launchd plists are read from (tests point these at a temporary folder).</param>
    public static MainViewModel CreateMain(ICommandRunner runner, AppSettings settings, StartupFolders? startupFolders = null)
    {
        var navigator = new DeferredNavigator();
        var info = new MacInfoService(runner);
        var shell = new MacShell(runner);
        Func<FdaState> fda = () => FullDiskAccess.Check(AppPaths.Home);
        var cleaner = new CleanerService(AppPaths.Home, runner, FileRemover.ForCurrentOs(), fda);
        var trash = new MacTrash(runner);

        var uid = MacNative.UserId;
        var signatures = new CodeSignatureService(runner);
        var backups = new BackupStore(AppPaths.BackupsDir);
        var admin = new AdminPrompt(runner);
        var processes = new ProcessesService(runner, signatures, Environment.ProcessId, uid);
        var startup = new StartupService(startupFolders ?? StartupFolders.For(AppPaths.Home), uid, runner, signatures, backups, admin, trash);
        var loginItems = new LoginItemsService(runner, backups);

        var pages = new PageViewModel[]
        {
            new DashboardViewModel(new IHealthContributor[]
            {
                new CleanerContributor(cleaner, info),
                new ProcessesContributor(processes, startup, AppPaths.Home),
            }, info, settings, navigator),
            new CleanerViewModel(cleaner, new DiskService(runner), new SpaceAnalyzer(AppPaths.Home), trash, shell, fda, AppPaths.Home),
            new ProcessesViewModel(processes, startup, loginItems, shell),
            new UpcomingPageViewModel("Programs", "Программы", "Установленные программы и их остатки", "Apps",
                "Список программ, удаление вместе с остатками в ~/Library (всё уходит в Корзину) и нежелательные программы."),
            new UpcomingPageViewModel("Security", "Безопасность и сеть", "Защита macOS и настройки сети", "Shield",
                "Gatekeeper, SIP, FileVault, брандмауэр, обновления macOS, профили конфигурации, прокси, DNS и файл hosts."),
            new SettingsViewModel(shell, fda, new BackupRestorer(backups, AppPaths.Home, uid, runner, admin, loginItems)),
        };
        var main = new MainViewModel(pages);
        navigator.Target = main;
        return main;
    }
}
