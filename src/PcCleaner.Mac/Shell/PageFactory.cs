using PcCleaner.Core;
using PcCleaner.Modules.Cleaner;
using PcCleaner.Modules.Dashboard;
using PcCleaner.Modules.Settings;

namespace PcCleaner.Shell;

/// <summary>Builds the window's pages in sidebar order.</summary>
public static class PageFactory
{
    public static MainViewModel CreateMain(ICommandRunner runner, AppSettings settings)
    {
        var navigator = new DeferredNavigator();
        var info = new MacInfoService(runner);
        var shell = new MacShell(runner);
        Func<FdaState> fda = () => FullDiskAccess.Check(AppPaths.Home);
        var cleaner = new CleanerService(AppPaths.Home, runner, FileRemover.ForCurrentOs(), fda);
        var pages = new PageViewModel[]
        {
            new DashboardViewModel(new IHealthContributor[] { new CleanerContributor(cleaner, info) }, info, settings, navigator),
            new CleanerViewModel(cleaner, new DiskService(runner), new SpaceAnalyzer(AppPaths.Home), new MacTrash(runner), shell, fda,
                AppPaths.Home),
            new UpcomingPageViewModel("Processes", "Процессы и автозагрузка", "Что работает на Mac и что запускается вместе с ним", "Pulse",
                "Запущенные программы с проверкой подписи, LaunchAgents и LaunchDaemons, объекты входа, сломанные записи " +
                "автозагрузки — с отключением, которое можно отменить."),
            new UpcomingPageViewModel("Programs", "Программы", "Установленные программы и их остатки", "Apps",
                "Список программ, удаление вместе с остатками в ~/Library (всё уходит в Корзину) и нежелательные программы."),
            new UpcomingPageViewModel("Security", "Безопасность и сеть", "Защита macOS и настройки сети", "Shield",
                "Gatekeeper, SIP, FileVault, брандмауэр, обновления macOS, профили конфигурации, прокси, DNS и файл hosts."),
            new SettingsViewModel(shell, fda),
        };
        var main = new MainViewModel(pages);
        navigator.Target = main;
        return main;
    }
}
