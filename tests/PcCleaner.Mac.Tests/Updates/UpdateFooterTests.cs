using PcCleaner.Core.Updates;
using Xunit;

namespace PcCleaner.Mac.Tests.Updates;

public class UpdateFooterTests
{
    private static readonly UpdateManifest M = UpdateTestKit.Manifest("1.3.0");

    private static readonly Version Current = new(1, 2, 0);

    [Theory]
    [InlineData(UpdateState.Checking, "v1.2.0 · проверка…")]
    [InlineData(UpdateState.UpToDate, "v1.2.0 · последняя версия")]
    [InlineData(UpdateState.Downloading, "v1.2.0 · скачивается 1.3.0")]
    [InlineData(UpdateState.Ready, "v1.2.0 · скачана 1.3.0")]
    [InlineData(UpdateState.ManualRequired, "v1.2.0 · есть версия 1.3.0")]
    [InlineData(UpdateState.Updated, "v1.2.0 · обновлена")]
    [InlineData(UpdateState.None, "v1.2.0")]
    [InlineData(UpdateState.Failed, "v1.2.0")]
    public void TheCorner_ShowsTheVersionAndWhatTheUpdaterKnows(UpdateState state, string expected)
        => Assert.Equal(expected, UpdateFooter.Text(state, M, Current));

    [Fact]
    public void WithoutAManifest_OnlyTheVersionIsShown()
        => Assert.Equal("v1.2.0", UpdateFooter.Text(UpdateState.Ready, null, Current));
}
