using PcCleaner.Core;
using Xunit;

namespace PcCleaner.Mac.Tests.Core;

public class MacPupsTests
{
    [Theory]
    [InlineData("Search Marquis")]
    [InlineData("com.searchmarquis.agent")]
    [InlineData("/Applications/SearchMarquis.app")]
    [InlineData("com.zeobit.MacKeeper.AntiVirus")]
    [InlineData("Advanced Mac Cleaner")]
    public void Known(string name) => Assert.NotNull(MacPups.Match(name));

    [Fact]
    public void Ordinary_AndEmptyNames()
    {
        Assert.Null(MacPups.Match("Telegram", null, "", "/Applications/Telegram.app", "ru.keepcoder.Telegram"));
        Assert.Null(MacPups.Match("WaterCa7s PC Cleaner", "io.github.waterca7.pccleaner"));
        Assert.Null(MacPups.Match("/Users/anna/dev/advsearch/tool"));
    }
}
