using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using PcCleaner.Core;
using PcCleaner.Mac.Tests.Processes;
using PcCleaner.Mac.Tests.Support;
using Xunit;

namespace PcCleaner.Mac.Tests.Ui;

/// <summary>«Процессы и автозагрузка» in the window, on processes and startup items from the CI machines and hand-made odd ones.</summary>
[Collection(UiCollection.Name)]
public class ProcessesPageTests
{
    private static string ShotsDir()
        => Environment.GetEnvironmentVariable("MAC_SCREENSHOTS_DIR") is { Length: > 0 } d
            ? Path.Combine(d, "processes")
            : Path.Combine(TestEnvironment.Root, "shots-processes");

    [AvaloniaFact]
    public async Task BothTabs_Render()
    {
        using var page = new ProcessesViewModelTests.Page();
        var main = new MainViewModel(new PageViewModel[] { page.Vm });
        using var shown = new WindowTests.ShownWindow(main);
        var window = shown.Window;
        window.Width = 1100;
        window.Height = 720;
        await page.Vm.OnNavigatedToAsync();
        page.Vm.Selected = page.Vm.Processes.Single(r => r.Name == "updater");
        Dispatcher.UIThread.RunJobs();

        var texts = WindowTests.Texts(window);
        Assert.Contains("Процессы и автозагрузка", texts);
        Assert.Contains("MacKeeper", texts);
        Assert.Contains("/private/tmp/.x/updater", texts);
        Assert.Contains("запущена из временной папки", texts);
        Directory.CreateDirectory(ShotsDir());
        window.CaptureRenderedFrame()!.Save(Path.Combine(ShotsDir(), "03-Processes.png"), new PngBitmapEncoderOptions());

        window.GetVisualDescendants().OfType<TabControl>().First().SelectedIndex = 1;
        await page.Vm.LoadLoginItemsCommand.ExecuteAsync();
        Dispatcher.UIThread.RunJobs();
        texts = WindowTests.Texts(window);
        Assert.Contains("Ваши агенты (6)", texts);
        Assert.Contains("Сломана: программа не найдена: /Users/anna/missing/tool", texts);
        Assert.Contains("называется как служба Apple, но программа не от Apple", texts);
        Assert.Contains("Работает (PID 945)", texts);
        window.CaptureRenderedFrame()!.Save(Path.Combine(ShotsDir(), "03-Processes-startup.png"), new PngBitmapEncoderOptions());
    }
}
