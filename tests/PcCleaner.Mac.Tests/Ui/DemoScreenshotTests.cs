using Avalonia.Headless.XUnit;
using PcCleaner.Mac.Tests.Dashboard;
using PcCleaner.Mac.Tests.Support;
using PcCleaner.Shell;
using Xunit;

namespace PcCleaner.Mac.Tests.Ui;

/// <summary>Pages on fixture data; saved to MAC_SCREENSHOTS_DIR when it is set (CI artifact, layout checks on any OS).</summary>
public class DemoScreenshotTests
{
    private static string Dir(string name)
        => Environment.GetEnvironmentVariable("MAC_SCREENSHOTS_DIR") is { Length: > 0 } d
            ? Path.Combine(d, name)
            : Path.Combine(TestEnvironment.Root, "shots-" + name);

    [AvaloniaFact]
    public async Task EveryPage_Renders()
    {
        var vm = WindowTests.DemoViewModel(MacInfoServiceTests.Mac());
        var window = new MainWindow { DataContext = vm };
        window.Show();
        var files = await PageCapture.CaptureAsync(window, vm, new ScreenshotOptions(Dir("demo"), Array.Empty<string>(), true, 1100, 720), CancellationToken.None);
        Assert.Equal(vm.Pages.Count, files.Count);
        Assert.All(files, f => Assert.True(new FileInfo(f).Length > 10_000, f));
    }

    [AvaloniaFact]
    public async Task Dashboard_WithAFinding_Renders()
    {
        var runner = MacInfoServiceTests.Mac().OnFixture("/bin/df", new[] { "-k", "-P", "/System/Volumes/Data" }, "df/low-space.txt");
        var vm = WindowTests.DemoViewModel(runner);
        var window = new MainWindow { DataContext = vm };
        window.Show();
        var files = await PageCapture.CaptureAsync(window, vm, new ScreenshotOptions(Dir("low-disk"), new[] { "Dashboard" }, true, 1100, 720), CancellationToken.None);
        Assert.Single(files);
    }
}
