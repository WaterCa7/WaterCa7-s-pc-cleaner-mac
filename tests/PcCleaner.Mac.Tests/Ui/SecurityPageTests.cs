using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using PcCleaner.Core;
using PcCleaner.Mac.Tests.Security;
using PcCleaner.Mac.Tests.Support;
using Xunit;

namespace PcCleaner.Mac.Tests.Ui;

/// <summary>«Безопасность и сеть» in the window: the CI runner as it is, and a Mac with an infected network.</summary>
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
        Assert.Contains("Строки файла hosts", texts);
        Assert.Contains("185.10.10.10 online.sberbank.ru", texts);
        Assert.DoesNotContain(texts, t => t?.Contains("Cafe", StringComparison.Ordinal) == true);
        Directory.CreateDirectory(ShotsDir());
        window.CaptureRenderedFrame()!.Save(Path.Combine(ShotsDir(), "05-Security.png"), new PngBitmapEncoderOptions());
    }
}
