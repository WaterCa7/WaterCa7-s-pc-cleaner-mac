using PcCleaner.Core;
using PcCleaner.Modules.Cleaner;
using PcCleaner.Modules.Dashboard;
using PcCleaner.Modules.Processes;
using PcCleaner.Modules.Programs;
using PcCleaner.Modules.Security;
using PcCleaner.Modules.Settings;
using PcCleaner.Modules.Startup;

namespace PcCleaner.Shell;

/// <summary>Builds the window's pages in sidebar order.</summary>
public static class PageFactory
{
    /// <param name="startupFolders">Where launchd plists are read from (tests point these at a temporary folder).</param>
    /// <param name="appFolders">Where apps are looked for (tests point these at a temporary folder).</param>
    /// <param name="securityPaths">The files the security checks read (tests point these at a temporary folder).</param>
    public static MainViewModel CreateMain(ICommandRunner runner, AppSettings settings, StartupFolders? startupFolders = null,
        AppFolders? appFolders = null, SecurityPaths? securityPaths = null)
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
        var security = new SecurityService(runner, securityPaths);
        var fixes = new SecurityFixes(admin, backups, Path.Combine(AppPaths.DataDir, "tmp"));
        var programs = new ProgramsService(new AppCatalog(appFolders ?? AppFolders.For(AppPaths.Home), runner, signatures), trash, processes,
            AppPaths.Home, fda);

        var pages = new PageViewModel[]
        {
            new DashboardViewModel(new IHealthContributor[]
            {
                new CleanerContributor(cleaner, info),
                new ProcessesContributor(processes, startup, AppPaths.Home),
                new ProgramsContributor(programs),
                new SecurityContributor(security, fixes),
            }, info, settings, navigator),
            new CleanerViewModel(cleaner, shell, fda),
            new ProcessesViewModel(processes, shell),
            new StartupViewModel(startup, loginItems, shell),
            new ProgramsViewModel(programs, shell, AppPaths.Home),
            new DiskViewModel(new DiskService(runner), new SpaceAnalyzer(AppPaths.Home), trash, shell, AppPaths.Home),
            new SecurityViewModel(security, fixes, shell),
            new SettingsViewModel(shell, fda, new BackupRestorer(backups, AppPaths.Home, uid, runner, admin, loginItems)),
        };
        var main = new MainViewModel(pages);
        navigator.Target = main;
        return main;
    }
}
