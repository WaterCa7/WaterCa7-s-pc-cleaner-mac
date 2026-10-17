using Avalonia;
using Avalonia.Headless;

[assembly: AvaloniaTestApplication(typeof(PcCleaner.Mac.Tests.TestAppBuilder))]

namespace PcCleaner.Mac.Tests;

/// <summary>The real App (theme, styles, page templates) on the headless platform with Skia, so frames can be captured.</summary>
public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<PcCleaner.App>()
        .UseSkia()
        .UseHarfBuzz()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
        .WithInterFont();
}
