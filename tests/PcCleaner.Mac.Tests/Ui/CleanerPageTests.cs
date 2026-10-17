using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using PcCleaner.Core;
using PcCleaner.Mac.Tests.Cleaner;
using PcCleaner.Mac.Tests.Dashboard;
using PcCleaner.Mac.Tests.Support;
using PcCleaner.Modules.Cleaner;
using PcCleaner.Shell;
using Xunit;

namespace PcCleaner.Mac.Tests.Ui;

/// <summary>The «Очистка и диски» page on a home folder with things to clean (window tests run alone, so the home may be swapped).</summary>
[Collection(UiCollection.Name)]
public class CleanerPageTests
{
    private static FakeHome FilledHome()
    {
        var home = new FakeHome();
        home.AddSized("Library/Caches/com.spotify.client/Data/a.bin", 110L << 20, TimeSpan.FromDays(30));
        home.Old("Library/Caches/org.telegram.desktop/media/b.bin", 1024 * 1024);
        home.Old("Library/Logs/DiagnosticReports/App-2026-09-01.ips", 200 * 1024);
        home.Old("Library/Caches/Google/Chrome/Default/Cache/Cache_Data/f_0001", 2 * 1024 * 1024);
        home.Old("Library/Developer/Xcode/DerivedData/App-abc/Build/x.o", 5 * 1024 * 1024);
        home.Old("Library/Developer/Xcode/Archives/2026-10-01/App.xcarchive/Info.plist", 1024);
        home.AddSized("Downloads/Установщик.dmg", 120L << 20, TimeSpan.FromDays(200));
        home.AddSized("Movies/Отпуск.mov", 130L << 20, TimeSpan.FromDays(30));
        return home;
    }

    private static FakeCommandRunner Runner() => CleanerViewModelTests.Runner()
        .OnFixture("/usr/sbin/scutil", new[] { "--get", "ComputerName" }, "scutil/computername.txt")
        .OnFixture("/usr/bin/sw_vers", Array.Empty<string>(), "sw_vers/macos15.txt")
        .OnFixture("/usr/sbin/sysctl", new[] { "-n", "kern.boottime" }, "sysctl/boottime.txt")
        .OnFixture("/usr/sbin/system_profiler", new[] { "SPHardwareDataType", "-json" }, "system_profiler/hardware-m2.json")
        .OnFixture("/bin/df", new[] { "-k", "-P", "/System/Volumes/Data" }, "df/data-volume.txt");

    private static async Task WithHome(FakeHome home, Func<Task> test)
    {
        var saved = AppPaths.Home;
        AppPaths.Home = home.Path;
        try
        {
            await test();
        }
        finally
        {
            AppPaths.Home = saved;
        }
    }

    private static string ShotsDir()
        => Environment.GetEnvironmentVariable("MAC_SCREENSHOTS_DIR") is { Length: > 0 } d
            ? Path.Combine(d, "cleaner")
            : Path.Combine(TestEnvironment.Root, "shots-cleaner");

    [AvaloniaFact]
    public async Task CleanerPage_ShowsCategoriesAndDisks()
    {
        using var home = FilledHome();
        await WithHome(home, async () =>
        {
            var vm = PageFactory.CreateMain(Runner(), new AppSettings(), WindowTests.EmptyStartup);
            using var shown = new WindowTests.ShownWindow(vm);
            var window = shown.Window;
            vm.NavigateTo("Cleaner");
            var page = (CleanerViewModel)vm.SelectedPage!;
            await page.OnNavigatedToAsync();
            page.Threshold = CleanerViewModel.Thresholds[0];
            await page.FindLargeFilesCommand.ExecuteAsync();
            Dispatcher.UIThread.RunJobs();

            var texts = WindowTests.Texts(window);
            Assert.Contains("Что можно очистить", texts);
            Assert.Contains("Кэши программ", texts);
            Assert.Contains("Кэши браузеров", texts);
            Assert.Contains("Очистить выбранное (113 МБ)", texts);

            Directory.CreateDirectory(ShotsDir());
            window.CaptureRenderedFrame()!.Save(Path.Combine(ShotsDir(), "02-Cleaner.png"), new PngBitmapEncoderOptions());

            window.GetVisualDescendants().OfType<TabControl>().First().SelectedIndex = 1;
            Dispatcher.UIThread.RunJobs();
            texts = WindowTests.Texts(window);
            Assert.Contains("Macintosh HD", texts);
            Assert.Contains("Мой диск", texts);
            Assert.Contains("Установщик.dmg", texts);
            window.CaptureRenderedFrame()!.Save(Path.Combine(ShotsDir(), "02-Cleaner-disks.png"), new PngBitmapEncoderOptions());
        });
    }

    [AvaloniaFact]
    public async Task Dashboard_FindsTheCaches()
    {
        using var home = FilledHome();
        await WithHome(home, async () =>
        {
            var vm = PageFactory.CreateMain(Runner(), new AppSettings(), WindowTests.EmptyStartup);
            using var shown = new WindowTests.ShownWindow(vm);
            var dashboard = (PcCleaner.Modules.Dashboard.DashboardViewModel)vm.Pages[0];
            await dashboard.OnNavigatedToAsync();
            await dashboard.ScanAsync(CancellationToken.None);
            Dispatcher.UIThread.RunJobs();
            Assert.Contains("Кэши и старые журналы занимают 113 МБ", WindowTests.Texts(shown.Window));
            Directory.CreateDirectory(ShotsDir());
            shown.Window.CaptureRenderedFrame()!.Save(Path.Combine(ShotsDir(), "01-Dashboard.png"), new PngBitmapEncoderOptions());
        });
    }
}
