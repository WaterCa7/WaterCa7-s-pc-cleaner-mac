using System.Text;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;
using PcCleaner.Core;

namespace PcCleaner.Shell;

/// <summary>
/// The --screenshots mode: the real system is only read (every question is answered «Нет»), the program's own data and
/// logs go to a temp folder, pages are rendered without a screen. Exit code 1 when a page failed or the log has errors.
/// </summary>
public static class ScreenshotRunner
{
    public static int Run(ScreenshotOptions options)
    {
        Console.OutputEncoding = Encoding.UTF8;
        var root = Path.Combine(Path.GetTempPath(), "WaterCa7sScreenshots-" + Guid.NewGuid().ToString("N")[..8]);
        AppPaths.UseRoot(root);
        Dialogs.UseSilentMode();
        AppBuilder.Configure<App>()
            .UseSkia()
            .UseHarfBuzz()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .WithInterFont()
            .SetupWithoutStarting();
        Ui.Attach(Dispatcher.UIThread);
        Log.Info($"{AppInfo.Name} {AppInfo.Version}: скриншоты в {Path.GetFullPath(options.Dir)}");

        var exitCode = 0;
        using var done = new CancellationTokenSource();
        Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                var vm = PageFactory.CreateMain(new ProcessCommandRunner(), new AppSettings());
                var window = new MainWindow { DataContext = vm };
                window.Show();
                var files = await PageCapture.CaptureAsync(window, vm, options, CancellationToken.None);
                var expected = PageCapture.PagesToShoot(vm, options.Pages).Count;
                if (files.Count != expected)
                    exitCode = 1;
                Console.WriteLine($"Сохранено снимков: {files.Count} из {expected}");
                window.Close();
            }
            catch (Exception ex)
            {
                Log.Error("Скриншоты: сбой", ex);
                exitCode = 1;
            }
            finally
            {
                done.Cancel();
            }
        });
        Dispatcher.UIThread.MainLoop(done.Token);
        Dispatcher.UIThread.RunJobs();

        var log = Log.Snapshot();
        File.WriteAllLines(Path.Combine(options.Dir, "log.txt"), log.Select(e => $"{e.TimeText} [{e.LevelText}] {e.Message}"));
        if (log.Any(e => e.Level == LogLevel.Error))
            exitCode = 1;
        try
        {
            Directory.Delete(root, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A temp folder left behind is harmless.
        }
        return exitCode;
    }
}
