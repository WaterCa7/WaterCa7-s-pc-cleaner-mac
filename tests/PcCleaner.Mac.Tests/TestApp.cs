using Avalonia;
using Avalonia.Headless;
using Xunit;

[assembly: AvaloniaTestApplication(typeof(PcCleaner.Mac.Tests.TestAppBuilder))]

namespace PcCleaner.Mac.Tests;

/// <summary>
/// Tests that show windows. They run after all other tests, never alongside them: the log and the commands are shared
/// static objects, and a window bound to them may only be touched from its own UI thread.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class UiCollection
{
    public const string Name = "ui";
}

/// <summary>The real App (theme, styles, page templates) on the headless platform with Skia, so frames can be captured.</summary>
public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<PcCleaner.App>()
        .UseSkia()
        .UseHarfBuzz()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
        .WithInterFont();
}
