using PcCleaner.Core;
using PcCleaner.Mac.Tests.Support;
using PcCleaner.Modules.Dashboard;
using PcCleaner.Shell;
using Xunit;

namespace PcCleaner.Mac.Tests.Shell;

public class MainViewModelTests
{
    private sealed class Page(string key, string title) : PageViewModel
    {
        public override string Title => title;

        public override string Subtitle => "";

        public override string Icon => "Home";

        public override string Key => key;
    }

    private static MainViewModel Create() => new(new PageViewModel[] { new Page("Cleaner", "Очистка и диски"), new Page("Programs", "Программы") });

    [Fact]
    public void StartsOnTheFirstPage() => Assert.Equal("Cleaner", Create().SelectedPage!.Key);

    [Fact]
    public void NavigateTo_SelectsByKey()
    {
        var vm = Create();
        vm.NavigateTo("programs");
        Assert.Equal("Programs", vm.SelectedPage!.Key);
        vm.NavigateTo("НетТакой");
        Assert.Equal("Programs", vm.SelectedPage!.Key);
    }

    [Fact]
    public void StatusText_ShowsTheLatestLogEntry()
    {
        var vm = Create();
        // Other tests write to the shared log at the same time: listen only to this entry.
        Log.EntryAdded -= vm.OnLogEntry;
        vm.OnLogEntry(new LogEntry(DateTime.Now, LogLevel.Info, "Статус для теста"));
        Assert.Equal("Статус для теста", vm.StatusText);
    }

    [Fact]
    public void PageFactory_BuildsTheSidebar_AndTheDashboardCanNavigate()
    {
        var main = PageFactory.CreateMain(new FakeCommandRunner(), new AppSettings(), Ui.WindowTests.EmptyStartup, Ui.WindowTests.EmptyApps, Ui.WindowTests.TestSecurity);
        Assert.Equal(new[] { "Dashboard", "Cleaner", "Processes", "Programs", "Security", "Settings" }, main.Pages.Select(p => p.Key));
        Assert.Equal(new[] { "Главная", "Очистка и диски", "Процессы и автозагрузка", "Программы", "Безопасность и сеть", "Настройки" },
            main.Pages.Select(p => p.Title));
        Assert.IsType<DashboardViewModel>(main.SelectedPage);
    }
}
