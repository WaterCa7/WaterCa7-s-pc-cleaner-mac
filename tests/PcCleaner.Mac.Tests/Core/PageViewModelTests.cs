using PcCleaner.Core;
using Xunit;

namespace PcCleaner.Mac.Tests.Core;

public class PageViewModelTests
{
    private sealed class TestPage : PageViewModel
    {
        public override string Title => "Тест";

        public override string Subtitle => "";

        public override string Icon => "Home";

        public Task Run(Func<CancellationToken, Task> work) => RunBusyAsync("Работаю", work);
    }

    [Fact]
    public void Key_IsTheTypeNameWithoutViewModel() => Assert.Equal("TestPage", new TestPage().Key);

    [Fact]
    public async Task RunBusy_SetsAndClearsBusy()
    {
        var page = new TestPage();
        bool? busyInside = null;
        await page.Run(_ =>
        {
            busyInside = page.IsBusy;
            return Task.CompletedTask;
        });
        Assert.True(busyInside);
        Assert.False(page.IsBusy);
    }

    [Fact]
    public async Task RunBusy_Cancel_StopsTheWorkQuietly()
    {
        var page = new TestPage();
        var started = new TaskCompletionSource();
        var run = page.Run(async ct =>
        {
            started.SetResult();
            await Task.Delay(Timeout.Infinite, ct);
        });
        await started.Task;
        page.CancelCommand.Execute(null);
        await run;
        Assert.False(page.IsBusy);
    }
}
