using PcCleaner.Core;
using PcCleaner.Mac.Tests.Support;
using PcCleaner.Modules.Dashboard;
using Xunit;

namespace PcCleaner.Mac.Tests.Dashboard;

public class DiskSpaceTests
{
    private const long Gb = 1L << 30;

    [Theory]
    [InlineData(500, 200, null)]
    [InlineData(500, 90, IssueSeverity.Medium)]
    [InlineData(500, 40, IssueSeverity.High)]
    [InlineData(2000, 9, IssueSeverity.High)]
    public void Evaluate(long totalGb, long freeGb, IssueSeverity? expected)
    {
        var issue = DiskSpaceRules.Evaluate(new DiskUsage("/System/Volumes/Data", totalGb * Gb, (totalGb - freeGb) * Gb, freeGb * Gb));
        Assert.Equal(expected, issue?.Severity);
        if (issue is not null)
        {
            Assert.Equal("Очистка и диски", issue.Module);
            Assert.Equal("Cleaner", issue.TargetPageKey);
            Assert.Null(issue.FixAsync);
        }
    }

    [Fact]
    public async Task Contributor_ReadsTheDisk()
    {
        var fake = new FakeCommandRunner().OnFixture("/bin/df", new[] { "-k", "-P", "/System/Volumes/Data" }, "df/low-space.txt");
        var issues = await new DiskSpaceContributor(new MacInfoService(fake)).AnalyzeAsync(CancellationToken.None);
        var issue = Assert.Single(issues);
        Assert.Equal(IssueSeverity.High, issue.Severity);
        Assert.StartsWith("Свободно 20 ГБ из 460 ГБ (4,3%)", issue.Details);
    }

    [Fact]
    public async Task Contributor_NoDisk_Throws_SoTheScanShowsTheModuleAsFailed()
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => new DiskSpaceContributor(new MacInfoService(new FakeCommandRunner())).AnalyzeAsync(CancellationToken.None));
        Assert.Equal("не удалось узнать свободное место на диске", ErrorText.For(ex));
    }
}
