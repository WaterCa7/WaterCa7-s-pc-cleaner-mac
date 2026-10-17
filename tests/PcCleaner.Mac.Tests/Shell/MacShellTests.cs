using PcCleaner.Core;
using PcCleaner.Mac.Tests.Support;
using Xunit;

namespace PcCleaner.Mac.Tests.Shell;

public class MacShellTests
{
    [Fact]
    public async Task Open_And_Reveal_UseOpen()
    {
        var fake = new FakeCommandRunner()
            .On("/usr/bin/open", new[] { SystemSettingsLinks.FullDiskAccess }, new CommandResult(0, "", ""))
            .On("/usr/bin/open", new[] { "-R", "/Users/x/file.txt" }, new CommandResult(0, "", ""));
        var shell = new MacShell(fake);
        Assert.True(await shell.OpenAsync(SystemSettingsLinks.FullDiskAccess));
        Assert.True(await shell.RevealAsync("/Users/x/file.txt"));
    }

    [Fact]
    public async Task Failure_IsFalse() => Assert.False(await new MacShell(new FakeCommandRunner()).OpenAsync("https://example.com"));
}
