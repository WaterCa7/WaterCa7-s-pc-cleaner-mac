using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using PcCleaner.Core;
using PcCleaner.Mac.Tests.Security;
using PcCleaner.Mac.Tests.Support;
using PcCleaner.Modules.Security;
using Xunit;

namespace PcCleaner.Mac.Tests.Ui;

/// <summary>«Безопасность» and «Сеть» in the window: the CI runner as it is, and a Mac with an infected network.</summary>
[Collection(UiCollection.Name)]
public class SecurityPageTests
{
    private static string ShotsDir()
        => Environment.GetEnvironmentVariable("MAC_SCREENSHOTS_DIR") is { Length: > 0 } d
            ? Path.Combine(d, "security")
            : Path.Combine(TestEnvironment.Root, "shots-security");

    [AvaloniaFact]
    public async Task ChecksRender()
    {
        using var page = new SecurityViewModelTests.Page(infected: true);
        var main = new MainViewModel(new PageViewModel[] { page.Vm });
        using var shown = new WindowTests.ShownWindow(main);
        var window = shown.Window;
        window.Width = 1100;
        window.Height = 720;
        await page.LoadAsync();
        Dispatcher.UIThread.RunJobs();

        var texts = WindowTests.Texts(window);
        Assert.Contains("Защита macOS", texts);
        Assert.Contains("Защита целостности системы (SIP)", texts);
        Assert.Contains("Включить брандмауэр", texts);
        // Hidden parts stay in the visual tree, so check for the lines themselves, not for the heading.
        Assert.DoesNotContain("185.10.10.10 online.sberbank.ru", texts);
        Directory.CreateDirectory(ShotsDir());
        window.CaptureRenderedFrame()!.Save(Path.Combine(ShotsDir(), "05-Security.png"), new PngBitmapEncoderOptions());
    }

    [AvaloniaFact]
    public async Task NetworkPageRenders()
    {
        using var page = new SecurityViewModelTests.Page(infected: true, part: SecurityPart.Network);
        var main = new MainViewModel(new PageViewModel[] { page.Vm });
        using var shown = new WindowTests.ShownWindow(main);
        var window = shown.Window;
        window.Width = 1100;
        window.Height = 720;
        await page.LoadAsync();
        Dispatcher.UIThread.RunJobs();

        var texts = WindowTests.Texts(window);
        Assert.Contains("Сеть Mac", texts);
        Assert.Contains("Строки файла hosts", texts);
        Assert.Contains("185.10.10.10 online.sberbank.ru", texts);
        Assert.DoesNotContain("Защита целостности системы (SIP)", texts);
        Assert.DoesNotContain(texts, t => t?.Contains("Cafe", StringComparison.Ordinal) == true);
        Directory.CreateDirectory(ShotsDir());
        window.CaptureRenderedFrame()!.Save(Path.Combine(ShotsDir(), "06-Network.png"), new PngBitmapEncoderOptions());
    }
}
