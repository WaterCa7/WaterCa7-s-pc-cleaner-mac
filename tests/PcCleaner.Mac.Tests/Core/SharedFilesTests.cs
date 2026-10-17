using PcCleaner.Core;
using PcCleaner.Modules.Dashboard;
using Xunit;

namespace PcCleaner.Mac.Tests.Core;

/// <summary>The copies of the Windows files compile and behave the same on the Mac side.</summary>
public class SharedFilesTests
{
    [Fact]
    public void Format_IsRussian() => Assert.Equal("1,5 ГБ", Format.Bytes(1536L * 1024 * 1024));

    [Fact]
    public void HealthScoring_CapsAModuleAt40()
    {
        var issues = Enumerable.Range(0, 5).Select(i => new HealthIssue
        {
            Module = "Очистка и диски",
            Title = "x" + i,
            Severity = IssueSeverity.Critical,
        });
        Assert.Equal(60, HealthScoring.ComputeScore(issues));
    }

    [Fact]
    public void ScoreDelta_Text() => Assert.Equal("Было 52 → стало 78 (+26)", ScoreDelta.Create(52, 78)!.Text);
}
