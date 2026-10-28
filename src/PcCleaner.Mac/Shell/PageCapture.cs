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
        foreach (var (number, page) in PagesToShoot(vm, options.Pages))
        {
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
            var file = Path.Combine(options.Dir, FileName(number, page, vm.Sections));
            frame.Save(file, new PngBitmapEncoderOptions());
            files.Add(file);
        }
        return files;
    }

    /// <summary>
    /// The tabs to photograph: all of them, or those <paramref name="requests"/> name (a page key, a section key or «Section/Tab»).
    /// <c>Number</c> is the place in <see cref="MainViewModel.Pages"/> (1-based), so a tab keeps its file name.
    /// </summary>
    public static List<(int Number, PageViewModel Page)> PagesToShoot(MainViewModel vm, IReadOnlyList<string> requests)
        => vm.Pages
            .Select((page, index) => (Number: index + 1, Page: page))
            .Where(p => requests.Count == 0 || requests.Any(r => Matches(r, vm.Sections.FirstOrDefault(s => s.Contains(p.Page)), p.Page)))
            .ToList();

    /// <summary>NN-Key.png for a section of one page with the same key (01-Dashboard.png), else NN-Section-Tab.png (06-System-Disk.png).</summary>
    public static string FileName(int number, PageViewModel page, IReadOnlyList<SectionViewModel> sections)
        => sections.FirstOrDefault(s => s.Contains(page)) is { } section && !string.Equals(section.Key, page.Key, StringComparison.OrdinalIgnoreCase)
            ? $"{number:00}-{section.Key}-{page.Key}.png"
            : $"{number:00}-{page.Key}.png";

    private static bool Matches(string request, SectionViewModel? section, PageViewModel page)
    {
        var parts = request.Split('/', 2, StringSplitOptions.TrimEntries);
        if (parts.Length == 2)
            return section is not null && Same(parts[0], section.Key) && Same(parts[1], page.Key);
        return Same(request.Trim(), page.Key) || (section is not null && Same(request.Trim(), section.Key));
    }

    private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
