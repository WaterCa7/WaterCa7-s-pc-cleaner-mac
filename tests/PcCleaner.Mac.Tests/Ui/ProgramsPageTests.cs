using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using PcCleaner.Core;
using PcCleaner.Mac.Tests.Programs;
using PcCleaner.Mac.Tests.Support;
using Xunit;

namespace PcCleaner.Mac.Tests.Ui;

/// <summary>«Программы» in the window, on fake apps with leftovers in a temporary home folder.</summary>
[Collection(UiCollection.Name)]
public class ProgramsPageTests
{
    private static string ShotsDir()
        => Environment.GetEnvironmentVariable("MAC_SCREENSHOTS_DIR") is { Length: > 0 } d
            ? Path.Combine(d, "programs")
            : Path.Combine(TestEnvironment.Root, "shots-programs");

    [AvaloniaFact]
    public async Task ListAndDetails_Render()
    {
        using var page = new ProgramsViewModelTests.Page();
        var main = new MainViewModel(new PageViewModel[] { page.Vm });
        using var shown = new WindowTests.ShownWindow(main);
        var window = shown.Window;
        window.Width = 1100;
        window.Height = 720;
        await page.LoadAsync();
        await page.SelectAsync("Writer");
        Dispatcher.UIThread.RunJobs();

        var texts = WindowTests.Texts(window);
        Assert.Contains("Программы", texts);
        Assert.Contains("MacKeeper", texts);
        Assert.Contains("Версия 3.2 · com.vendor.writer", texts);
        Assert.Contains("~/Library/Caches/com.vendor.writer", texts);
        Assert.Contains("Процессор: Apple Silicon", texts);
        Directory.CreateDirectory(ShotsDir());
        window.CaptureRenderedFrame()!.Save(Path.Combine(ShotsDir(), "04-Programs.png"), new PngBitmapEncoderOptions());

        await page.SelectAsync("MacKeeper");
        Dispatcher.UIThread.RunJobs();
        Assert.Contains(WindowTests.Texts(window), t => t?.StartsWith("MacKeeper — навязчивый", StringComparison.Ordinal) == true);
        window.CaptureRenderedFrame()!.Save(Path.Combine(ShotsDir(), "04-Programs-unwanted.png"), new PngBitmapEncoderOptions());
    }
}
