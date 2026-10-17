using PcCleaner.Shell;
using Xunit;

namespace PcCleaner.Mac.Tests.Shell;

public class ScreenshotOptionsTests
{
    [Fact]
    public void NotAScreenshotRun() => Assert.False(ScreenshotOptions.TryParse(new[] { "--other" }, out _, out _));

    [Fact]
    public void Defaults()
    {
        Assert.True(ScreenshotOptions.TryParse(new[] { "--screenshots", "out" }, out var o, out var error));
        Assert.Null(error);
        Assert.Equal(("out", true, 1100, 720), (o!.Dir, o.Scan, o.Width, o.Height));
        Assert.Empty(o.Pages);
    }

    [Fact]
    public void AllOptions()
    {
        Assert.True(ScreenshotOptions.TryParse(new[] { "--screenshots", "out", "--page", "Dashboard,Settings", "--no-scan", "--size", "1040x660" }, out var o, out _));
        Assert.Equal(new[] { "Dashboard", "Settings" }, o!.Pages);
        Assert.False(o.Scan);
        Assert.Equal((1040, 660), (o.Width, o.Height));
    }

    [Theory]
    [InlineData("--screenshots")]
    [InlineData("--screenshots out --size 10x10")]
    [InlineData("--screenshots out --size большой")]
    [InlineData("--screenshots out --странно")]
    public void Errors(string line)
    {
        Assert.True(ScreenshotOptions.TryParse(line.Split(' '), out var o, out var error));
        Assert.Null(o);
        Assert.NotNull(error);
    }
}
