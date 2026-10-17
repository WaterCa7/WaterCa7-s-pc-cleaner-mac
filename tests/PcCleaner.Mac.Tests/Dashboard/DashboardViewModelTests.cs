using PcCleaner.Core;
using PcCleaner.Modules.Dashboard;
using Xunit;

namespace PcCleaner.Mac.Tests.Dashboard;

[Collection("dialogs")]
public class DashboardViewModelTests
{
    private sealed class FakeContributor(string module, params HealthIssue[] issues) : IHealthContributor
    {
        public string ModuleName => module;

        public Task<IReadOnlyList<HealthIssue>> AnalyzeAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<HealthIssue>>(issues);
    }

    private sealed class FailingContributor : IHealthContributor
    {
        public string ModuleName => "Программы";

        public Task<IReadOnlyList<HealthIssue>> AnalyzeAsync(CancellationToken ct) => throw new InvalidOperationException("сломалось");
    }

    private sealed class RecordingNavigator : INavigator
    {
        public string? Last { get; private set; }

        public IEnumerable<PageViewModel> Pages => Array.Empty<PageViewModel>();

        public void NavigateTo(string pageKey) => Last = pageKey;
    }

    private static HealthIssue Issue(string title, IssueSeverity severity, Func<CancellationToken, Task<string>>? fix = null, bool autoFix = false)
        => new() { Module = "Очистка и диски", Title = title, Severity = severity, FixAsync = fix, AutoFixSafe = autoFix };

    private static DashboardViewModel Create(params IHealthContributor[] contributors)
        => new(contributors, new MacInfoService(MacInfoServiceTests.Mac()), new AppSettings(), null);

    [Fact]
    public async Task BeforeTheFirstScan_SaysSo()
    {
        var vm = Create();
        await vm.OnNavigatedToAsync();
        Assert.False(vm.HasScan);
        Assert.Equal("—", vm.ScoreText);
        Assert.Equal("Mac ещё не проверялся", vm.Verdict);
        Assert.Contains(vm.InfoRows, r => r.Label == "Чип" && r.Value == "Apple M2");
    }

    [Fact]
    public async Task Scan_ScoresAndGroups()
    {
        var vm = Create(new FakeContributor("Очистка и диски", Issue("Мало места", IssueSeverity.High), Issue("Совет", IssueSeverity.Info)));
        await vm.ScanAsync(CancellationToken.None);
        Assert.Equal(88, vm.Score);
        Assert.Equal("Хорошо", vm.Verdict);
        Assert.Equal(new[] { "Важно", "Советы" }, vm.Groups.Select(g => g.Title));
        Assert.Equal("1 проблема и 1 совет", vm.FindingsText);
        Assert.Equal("1 проблема и 1 совет", Assert.Single(vm.Modules).StateText);
    }

    [Fact]
    public async Task Texts_SayMacNotComputer()
    {
        var vm = Create(new FakeContributor("Очистка и диски"));
        await vm.ScanAsync(CancellationToken.None);
        Assert.DoesNotContain("омпьютер", vm.Verdict + vm.VerdictDescription);
        Assert.Contains("Mac", vm.VerdictDescription);
    }

    [Fact]
    public async Task Scan_FailedModule_IsIncomplete()
    {
        var vm = Create(new FakeContributor("Очистка и диски"), new FailingContributor());
        await vm.ScanAsync(CancellationToken.None);
        Assert.Equal(HealthScoring.IncompleteVerdict, vm.Verdict);
        var failed = Assert.Single(vm.Modules, m => m.IsFailed);
        Assert.Equal("Программы", failed.Name);
        Assert.Equal("не удалось проверить: сломалось", failed.StateText);
        Assert.True(vm.HasScanWarning);
    }

    [Fact]
    public async Task FixAll_Declined_ChangesNothing()
    {
        Dialogs.UseSilentMode();
        var fixedIt = false;
        var vm = Create(new FakeContributor("Очистка и диски",
            Issue("Кэши", IssueSeverity.Medium, _ =>
            {
                fixedIt = true;
                return Task.FromResult("Удалено 1 ГБ");
            }, autoFix: true)));
        await vm.ScanAsync(CancellationToken.None);
        await vm.FixAllAsync();
        Assert.False(fixedIt);
        Assert.Equal(1, vm.FixableCount);
    }

    [Fact]
    public async Task FixAll_Confirmed_FixesOnlyAutoFixSafe_AndRescores()
    {
        string? question = null;
        Dialogs.ConfirmHandler = r =>
        {
            question = r.Message;
            return Task.FromResult(true);
        };
        try
        {
            var manual = false;
            var vm = Create(new FakeContributor("Очистка и диски",
                Issue("Кэши", IssueSeverity.Medium, _ => Task.FromResult("Удалено 1 ГБ"), autoFix: true),
                Issue("Ручное", IssueSeverity.Medium, _ =>
                {
                    manual = true;
                    return Task.FromResult("ok");
                })));
            await vm.ScanAsync(CancellationToken.None);
            Assert.Equal(88, vm.Score);
            await vm.FixAllAsync();
            Assert.Contains("• Кэши", question);
            Assert.DoesNotContain("Ручное", question);
            Assert.False(manual);
            Assert.Equal(94, vm.Score);
            Assert.Equal(0, vm.FixableCount);
        }
        finally
        {
            Dialogs.UseSilentMode();
        }
    }

    [Fact]
    public async Task FixAll_FailureText_IsNotMarkedFixed()
    {
        Dialogs.ConfirmHandler = _ => Task.FromResult(true);
        try
        {
            var vm = Create(new FakeContributor("Очистка и диски",
                Issue("Кэши", IssueSeverity.Medium, _ => Task.FromResult("Не удалось удалить файлы"), autoFix: true)));
            await vm.ScanAsync(CancellationToken.None);
            await vm.FixAllAsync();
            var item = vm.Groups.Single().Items.Single();
            Assert.False(item.IsFixed);
            Assert.Equal("Не удалось удалить файлы", item.FixResult);
        }
        finally
        {
            Dialogs.UseSilentMode();
        }
    }

    [Fact]
    public async Task FixAll_Exception_BecomesTheResult()
    {
        Dialogs.ConfirmHandler = _ => Task.FromResult(true);
        try
        {
            var vm = Create(new FakeContributor("Очистка и диски",
                Issue("Кэши", IssueSeverity.Medium, _ => throw new UnauthorizedAccessException(), autoFix: true)));
            await vm.ScanAsync(CancellationToken.None);
            await vm.FixAllAsync();
            var item = vm.Groups.Single().Items.Single();
            Assert.False(item.IsFixed);
            Assert.StartsWith("Не удалось: нет доступа", item.FixResult);
        }
        finally
        {
            Dialogs.UseSilentMode();
        }
    }

    [Fact]
    public async Task SecondScan_ShowsTheDelta()
    {
        var settings = new AppSettings();
        var first = new DashboardViewModel(new IHealthContributor[] { new FakeContributor("Очистка и диски", Issue("Мало места", IssueSeverity.High)) },
            new MacInfoService(MacInfoServiceTests.Mac()), settings, null);
        await first.ScanAsync(CancellationToken.None);
        Assert.False(first.HasDelta);
        var second = new DashboardViewModel(new IHealthContributor[] { new FakeContributor("Очистка и диски") },
            new MacInfoService(MacInfoServiceTests.Mac()), settings, null);
        await second.OnNavigatedToAsync();
        Assert.Equal(88, second.Score);
        await second.ScanAsync(CancellationToken.None);
        Assert.True(second.HasDelta);
        Assert.Equal("Было 88 → стало 100 (+12)", second.DeltaText);
    }

    [Fact]
    public async Task Open_NavigatesToTheTargetPage()
    {
        var nav = new RecordingNavigator();
        var vm = new DashboardViewModel(new IHealthContributor[]
            {
                new FakeContributor("Очистка и диски",
                    new HealthIssue { Module = "Очистка и диски", Title = "Мало места", Severity = IssueSeverity.High, TargetPageKey = "Cleaner" }),
            },
            new MacInfoService(MacInfoServiceTests.Mac()), new AppSettings(), nav);
        await vm.ScanAsync(CancellationToken.None);
        var item = vm.Groups[0].Items[0];
        Assert.True(item.CanOpen);
        Assert.False(item.CanFix);
        item.OpenCommand.Execute(null);
        Assert.Equal("Cleaner", nav.Last);
    }
}
