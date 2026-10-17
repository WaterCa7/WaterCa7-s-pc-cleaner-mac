using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using PcCleaner.Core;
using PcCleaner.Mac.Tests.Dashboard;
using PcCleaner.Mac.Tests.Support;
using PcCleaner.Shell;
using Xunit;

namespace PcCleaner.Mac.Tests.Ui;

public class WindowTests
{
    /// <summary>The real pages on a fake Mac (fixture output): what the screenshots show.</summary>
    internal static MainViewModel DemoViewModel(FakeCommandRunner runner) => PageFactory.CreateMain(runner, new AppSettings());

    internal static List<string?> Texts(Window window)
        => window.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();

    [AvaloniaFact]
    public void Sidebar_ListsThePages_AndShowsTheFirst()
    {
        var window = new MainWindow { DataContext = DemoViewModel(MacInfoServiceTests.Mac()) };
        window.Show();
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
        var window = new MainWindow { DataContext = vm };
        window.Show();
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
        var window = new MainWindow { DataContext = vm };
        window.Show();
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
        var window = new MainWindow { DataContext = vm };
        window.Show();
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
        var window = new MainWindow { DataContext = vm };
        window.Show();
        vm.NavigateTo("Programs");
        Dispatcher.UIThread.RunJobs();
        Assert.Contains("Раздел появится в следующей сборке", Texts(window));
    }
}
