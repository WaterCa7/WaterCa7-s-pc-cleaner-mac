using PcCleaner.Core;
using PcCleaner.Mac.Tests.Support;
using PcCleaner.Modules.Dashboard;
using Xunit;

namespace PcCleaner.Mac.Tests.Dashboard;

[Collection("dialogs")]
public class ReportTests
{
    private sealed class FakeContributor(string module, params HealthIssue[] issues) : IHealthContributor
    {
        public string ModuleName => module;

        public Task<IReadOnlyList<HealthIssue>> AnalyzeAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<HealthIssue>>(issues);
    }

    private sealed class FakeOutput(string? path) : IReportOutput
    {
        public string? Copied { get; private set; }

        public Task<string?> PickSavePathAsync(string suggestedName) => Task.FromResult(path);

        public Task CopyTextAsync(string text)
        {
            Copied = text;
            return Task.CompletedTask;
        }
    }

    private static async Task<DashboardViewModel> ScannedAsync()
    {
        var vm = new DashboardViewModel(new IHealthContributor[]
            {
                new FakeContributor("Безопасность и сеть",
                    new HealthIssue { Module = "Безопасность и сеть", Title = "Брандмауэр: выключен", Details = "Программы <script>alert(1)</script> & сеть", Severity = IssueSeverity.Medium }),
                new FakeContributor("Очистка и диски",
                    new HealthIssue { Module = "Очистка и диски", Title = "Кэши занимают 150 МБ", Severity = IssueSeverity.Low, ReclaimableBytes = 150L << 20 }),
            },
            new MacInfoService(MacInfoServiceTests.Mac()), new AppSettings(), null);
        await vm.OnNavigatedToAsync();
        await vm.ScanAsync(CancellationToken.None);
        return vm;
    }

    [Fact]
    public async Task Html_IsSelfContained_Encoded_AndAboutTheMac()
    {
        var vm = await ScannedAsync();
        Assert.True(vm.HasReport);
        var html = ReportBuilder.BuildHtml(vm.BuildReport(new DateTime(2026, 10, 18, 12, 0, 0)));
        Assert.StartsWith("<!DOCTYPE html>", html, StringComparison.Ordinal);
        Assert.Contains("<h1>Отчёт о проверке Mac</h1>", html, StringComparison.Ordinal);
        Assert.Contains("<h2>Об этом Mac</h2>", html, StringComparison.Ordinal);
        Assert.Contains("Apple M2", html, StringComparison.Ordinal);
        Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt; &amp; сеть", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<script", html, StringComparison.Ordinal);
        Assert.DoesNotContain("src=\"http", html, StringComparison.Ordinal);
        Assert.DoesNotContain("омпьютер", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Windows", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Text_ForTheClipboard()
    {
        var vm = await ScannedAsync();
        var text = ReportBuilder.BuildText(vm.BuildReport(DateTime.Now));
        Assert.StartsWith("Отчёт о проверке Mac — WaterCa7s PC Cleaner", text, StringComparison.Ordinal);
        Assert.Contains("ОБ ЭТОМ MAC", text, StringComparison.Ordinal);
        Assert.Contains("[Безопасность и сеть] Брандмауэр: выключен", text, StringComparison.Ordinal);
        Assert.DoesNotContain("\r\n", text, StringComparison.Ordinal);
        Assert.DoesNotContain("омпьютер", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SaveAndCopy_GoThroughTheOutput()
    {
        using var dir = new TempDir();
        var vm = await ScannedAsync();
        var path = Path.Combine(dir.Path, "Отчёт.html");
        var output = new FakeOutput(path);
        vm.ReportOutput = output;
        await vm.SaveReportCommand.ExecuteAsync();
        Assert.Contains("Брандмауэр: выключен", await File.ReadAllTextAsync(path));
        Assert.NotEqual(0xEF, File.ReadAllBytes(path)[0]);
        await vm.CopyReportCommand.ExecuteAsync();
        Assert.Contains("Кэши занимают 150 МБ", output.Copied, StringComparison.Ordinal);

        vm.ReportOutput = new FakeOutput(null);
        await vm.SaveReportCommand.ExecuteAsync();
        Assert.Single(Directory.GetFiles(dir.Path));
    }

    [Fact]
    public async Task NoReport_BeforeAScanInThisSession()
    {
        var vm = new DashboardViewModel(Array.Empty<IHealthContributor>(), new MacInfoService(MacInfoServiceTests.Mac()), new AppSettings(), null);
        await vm.OnNavigatedToAsync();
        Assert.False(vm.HasReport);
        Assert.False(vm.SaveReportCommand.CanExecute(null));
    }
}
