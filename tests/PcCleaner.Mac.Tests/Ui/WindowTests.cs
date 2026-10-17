using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using PcCleaner.Core;
using PcCleaner.Mac.Tests.Dashboard;
using PcCleaner.Mac.Tests.Support;
using PcCleaner.Modules.Startup;
using PcCleaner.Shell;
using Xunit;

namespace PcCleaner.Mac.Tests.Ui;

[Collection(UiCollection.Name)]
public class WindowTests
{
    /// <summary>The real pages on a fake Mac (fixture output): what the screenshots show.</summary>
    /// <summary>Empty startup folders in the test root: the window tests never read the machine's own launchd plists.</summary>
    internal static readonly StartupFolders EmptyStartup = MakeEmptyStartup();

    internal static MainViewModel DemoViewModel(FakeCommandRunner runner) => PageFactory.CreateMain(runner, new AppSettings(), EmptyStartup);

    private static StartupFolders MakeEmptyStartup()
    {
        string Dir(string name) => Directory.CreateDirectory(Path.Combine(TestEnvironment.Root, "startup", name)).FullName;
        return new StartupFolders(Dir("user"), Dir("global"), Dir("daemons"), Dir("apple-agents"), Dir("apple-daemons"));
    }

    /// <summary>Shows the main window; disposing closes it, so its bindings let go of the shared log and commands.</summary>
    internal sealed class ShownWindow(MainViewModel vm) : IDisposable
    {
        public MainWindow Window { get; } = Open(vm);

        public void Dispose() => Window.Close();

        private static MainWindow Open(MainViewModel vm)
        {
            var window = new MainWindow { DataContext = vm };
            window.Show();
            return window;
        }
    }

    internal static List<string?> Texts(Window window)
        => window.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();

    [AvaloniaFact]
    public void Sidebar_ListsThePages_AndShowsTheFirst()
    {
        using var shown = new ShownWindow(DemoViewModel(MacInfoServiceTests.Mac()));
        var window = shown.Window;
        Dispatcher.UIThread.RunJobs();
        var texts = Texts(window);
        Assert.Contains("Главная", texts);
        Assert.Contains("Очистка и диски", texts);
        Assert.Contains("Безопасность и сеть", texts);
        Assert.Contains("Настройки", texts);
        Assert.Contains("Mac ещё не проверялся", texts);
        Assert.NotNull(window.CaptureRenderedFrame());
    }

    [AvaloniaFact]
    public async Task Dashboard_AfterScan_ShowsTheScoreAndTheMac()
    {
        var vm = DemoViewModel(MacInfoServiceTests.Mac());
        using var shown = new ShownWindow(vm);
        var window = shown.Window;
        var dashboard = (PcCleaner.Modules.Dashboard.DashboardViewModel)vm.Pages[0];
        await dashboard.OnNavigatedToAsync();
        await dashboard.ScanAsync(CancellationToken.None);
        Dispatcher.UIThread.RunJobs();
        var texts = Texts(window);
        Assert.Contains("100", texts);
        Assert.Contains("Отлично", texts);
        Assert.Contains("Apple M2", texts);
        Assert.Contains("Проверить снова", texts);
    }

    [AvaloniaFact]
    public async Task Dashboard_LowDisk_ShowsTheFinding()
    {
        var runner = MacInfoServiceTests.Mac().OnFixture("/bin/df", new[] { "-k", "-P", "/System/Volumes/Data" }, "df/low-space.txt");
        var vm = DemoViewModel(runner);
        using var shown = new ShownWindow(vm);
        var window = shown.Window;
        await ((PcCleaner.Modules.Dashboard.DashboardViewModel)vm.Pages[0]).ScanAsync(CancellationToken.None);
        Dispatcher.UIThread.RunJobs();
        var texts = Texts(window);
        Assert.Contains("Мало свободного места на диске", texts);
        Assert.Contains("Важно", texts);
        Assert.Contains("88", texts);
    }

    [AvaloniaFact]
    public void Navigation_ShowsTheSettingsPage()
    {
        var vm = DemoViewModel(MacInfoServiceTests.Mac());
        using var shown = new ShownWindow(vm);
        var window = shown.Window;
        vm.NavigateTo("Settings");
        Dispatcher.UIThread.RunJobs();
        var texts = Texts(window);
        Assert.Contains("Полный доступ к диску", texts);
        Assert.Contains("Журнал", texts);
    }

    [AvaloniaFact]
    public void Navigation_ShowsAnUpcomingSection()
    {
        var vm = DemoViewModel(MacInfoServiceTests.Mac());
        using var shown = new ShownWindow(vm);
        var window = shown.Window;
        vm.NavigateTo("Programs");
        Dispatcher.UIThread.RunJobs();
        Assert.Contains("Раздел появится в следующей сборке", Texts(window));
    }
}
