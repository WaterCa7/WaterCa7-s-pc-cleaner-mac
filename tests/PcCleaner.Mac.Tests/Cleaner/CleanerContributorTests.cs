using PcCleaner.Core;
using PcCleaner.Mac.Tests.Dashboard;
using PcCleaner.Mac.Tests.Support;
using PcCleaner.Modules.Cleaner;
using PcCleaner.Modules.Dashboard;
using Xunit;

namespace PcCleaner.Mac.Tests.Cleaner;

public class CleanerContributorTests
{
    private const long Mb = 1L << 20;

    private static CategoryScan Scan(CleanerCategory category, long bytes)
        => new(category, new[] { new CleanTarget("t", "/x", new[] { new FileEntry("/x/f", "f", bytes, FakeHome.Now) }) }, ScanState.Ready);

    private static IReadOnlyList<HealthIssue> Evaluate(params CategoryScan[] scans)
        => CleanerRules.Evaluate(scans, _ => Task.FromResult("Удалено"));

    [Theory]
    [InlineData(50, null)]
    [InlineData(150, IssueSeverity.Low)]
    [InlineData(600, IssueSeverity.Medium)]
    public void Caches_Thresholds(long mb, IssueSeverity? expected)
    {
        var issue = Evaluate(Scan(CleanerCatalog.AppCaches, mb * Mb / 2), Scan(CleanerCatalog.Browsers, mb * Mb / 2)).SingleOrDefault();
        Assert.Equal(expected, issue?.Severity);
        if (issue is not null)
        {
            Assert.True(issue.AutoFixSafe);
            Assert.NotNull(issue.FixAsync);
            Assert.Equal(mb * Mb, issue.ReclaimableBytes);
        }
    }

    [Fact]
    public void Developer_And_Trash_AreTipsWithoutAFix()
    {
        var issues = Evaluate(Scan(CleanerCatalog.Developer, 3L << 30), Scan(CleanerCatalog.Trash, 2L << 30));
        Assert.Equal(2, issues.Count);
        Assert.All(issues, i =>
        {
            Assert.Equal(IssueSeverity.Low, i.Severity);
            Assert.Null(i.FixAsync);
            Assert.Equal("Cleaner", i.TargetPageKey);
            Assert.Contains("«Очистка»", i.Details, StringComparison.Ordinal);
            Assert.DoesNotContain("Очистка и диски", i.Details, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void SmallDeveloperAndTrash_AreNotFindings()
        => Assert.Empty(Evaluate(Scan(CleanerCatalog.Developer, 1L << 30), Scan(CleanerCatalog.Trash, 500 * Mb)));

    [Fact]
    public async Task Contributor_FindsLowDiskAndCaches_AndTheFixCleans()
    {
        using var home = new FakeHome();
        var cache = home.Old("Library/Caches/com.example.app/big.bin", 200 * 1024 * 1024);
        var runner = MacInfoServiceTests.Mac().OnFixture("/bin/df", new[] { "-k", "-P", "/System/Volumes/Data" }, "df/low-space.txt");
        var cleaner = new CleanerService(home.Path, runner, new ManagedFileRemover(), () => FdaState.Denied, () => FakeHome.Now);
        var issues = await new CleanerContributor(cleaner, new MacInfoService(runner)).AnalyzeAsync(CancellationToken.None);

        Assert.Equal(new[] { IssueSeverity.High, IssueSeverity.Low }, issues.Select(i => i.Severity));
        var fix = issues[1].FixAsync!;
        var message = await fix(CancellationToken.None);
        Assert.Equal("Удалено 200 МБ (1 файл).", message);
        Assert.False(File.Exists(cache));
        Assert.False(HealthScoring.LooksLikeFailure(message));
    }

    [Fact]
    public async Task Contributor_UnknownDisk_StillChecksTheCaches()
    {
        using var home = new FakeHome();
        var cleaner = new CleanerService(home.Path, new FakeCommandRunner(), new ManagedFileRemover(), () => FdaState.Denied, () => FakeHome.Now);
        var issues = await new CleanerContributor(cleaner, new MacInfoService(new FakeCommandRunner())).AnalyzeAsync(CancellationToken.None);
        Assert.Empty(issues);
    }
}
