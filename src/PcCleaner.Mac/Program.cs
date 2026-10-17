using Avalonia;
using PcCleaner.Shell;

namespace PcCleaner;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (ScreenshotOptions.TryParse(args, out var options, out var error))
        {
            if (options is null)
            {
                Console.Error.WriteLine(error);
                return 2;
            }
            return ScreenshotRunner.Run(options);
        }
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        return 0;
    }

    /// <summary>Also used by the Avalonia designer.</summary>
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
