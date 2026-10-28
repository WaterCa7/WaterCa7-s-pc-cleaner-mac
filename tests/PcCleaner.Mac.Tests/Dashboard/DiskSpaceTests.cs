using PcCleaner.Core;
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
            Assert.Equal("Disk", issue.TargetPageKey);
            Assert.Null(issue.FixAsync);
        }
    }
}
