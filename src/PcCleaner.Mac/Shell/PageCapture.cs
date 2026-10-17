using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using PcCleaner.Core;

namespace PcCleaner.Shell;

/// <summary>Shows each page in the window, runs its read-only scan and saves a frame. Used by --screenshots and the tests.</summary>
public static class PageCapture
{
    public static readonly TimeSpan PageTimeout = TimeSpan.FromSeconds(90);

    public static async Task<IReadOnlyList<string>> CaptureAsync(MainWindow window, MainViewModel vm, ScreenshotOptions options, CancellationToken ct)
    {
        Directory.CreateDirectory(options.Dir);
        window.Width = options.Width;
        window.Height = options.Height;
        var files = new List<string>();
        for (var index = 0; index < vm.Pages.Count; index++)
        {
            var page = vm.Pages[index];
            if (options.Pages.Count > 0 && !options.Pages.Contains(page.Key, StringComparer.OrdinalIgnoreCase))
                continue;
            vm.SelectedPage = page;
            await page.OnNavigatedToAsync();
            if (options.Scan)
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(PageTimeout);
                try
                {
                    await page.PreviewScanAsync(timeout.Token);
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    Log.Warn($"Скриншоты: «{page.Title}» не уложилась в {PageTimeout.TotalSeconds:0} с");
                }
            }
            Dispatcher.UIThread.RunJobs();
            var frame = window.CaptureRenderedFrame();
            if (frame is null)
            {
                Log.Error($"Скриншоты: не удалось снять страницу «{page.Title}»");
                continue;
            }
            var file = Path.Combine(options.Dir, $"{index + 1:00}-{page.Key}.png");
            frame.Save(file, new PngBitmapEncoderOptions());
            files.Add(file);
        }
        return files;
    }
}
