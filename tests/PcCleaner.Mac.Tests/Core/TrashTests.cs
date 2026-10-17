using PcCleaner.Core;
using PcCleaner.Mac.Tests.Support;
using Xunit;

namespace PcCleaner.Mac.Tests.Core;

public class TrashTests
{
    private static string[] FinderArgs(string path) => MacTrash.FinderScript.Append(path).ToArray();

    [Fact]
    public async Task NativeSuccess_DoesNotAskFinder()
    {
        var fake = new FakeCommandRunner();
        var outcome = await new MacTrash(fake, _ => new NativeTrashResult(true, null, false)).MoveAsync("/Users/x/file.txt", CancellationToken.None);
        Assert.True(outcome.Ok);
        Assert.Empty(fake.Calls);
    }

    [Fact]
    public async Task NoPermission_GoesThroughFinder_WithThePathAsAnArgument()
    {
        const string path = "/Applications/Чужая \"программа\".app";
        var fake = new FakeCommandRunner().On("/usr/bin/osascript", FinderArgs(path), new CommandResult(0, "", ""));
        var outcome = await new MacTrash(fake, _ => new NativeTrashResult(false, "нет прав", true)).MoveAsync(path, CancellationToken.None);
        Assert.True(outcome.Ok);
        Assert.Single(fake.Calls);
        Assert.All(MacTrash.FinderScript, line => Assert.DoesNotContain("Чужая", line));
    }

    [Fact]
    public async Task PasswordCancelled_IsReportedAsNothingChanged()
    {
        const string path = "/Applications/X.app";
        var fake = new FakeCommandRunner().On("/usr/bin/osascript", FinderArgs(path),
            new CommandResult(1, "", "execution error: User canceled. (-128)"));
        var outcome = await new MacTrash(fake, _ => new NativeTrashResult(false, "нет прав", true)).MoveAsync(path, CancellationToken.None);
        Assert.False(outcome.Ok);
        Assert.Equal("отменено — ничего не изменено", outcome.Error);
    }

    [Fact]
    public async Task OtherFailure_KeepsTheSystemText()
    {
        var outcome = await new MacTrash(new FakeCommandRunner(), _ => new NativeTrashResult(false, "Файл занят", false))
            .MoveAsync("/Users/x/a.txt", CancellationToken.None);
        Assert.Equal(new TrashOutcome(false, "Файл занят"), outcome);
    }

    [Fact]
    public async Task RelativePath_IsRefused()
        => await Assert.ThrowsAsync<ArgumentException>(() => new MacTrash(new FakeCommandRunner(), _ => new NativeTrashResult(true, null, false))
            .MoveAsync("a.txt", CancellationToken.None));

    [Fact]
    public void NotOnAMac_SaysSo()
    {
        if (OperatingSystem.IsMacOS())
            return;
        Assert.Equal("Корзина есть только в macOS", MacNative.TrashItem("/x").Error);
    }

    [MacFact]
    public void Bridge_KeepsCyrillic() => Assert.Equal("Привет, Mac ✓", MacNative.RoundTrip("Привет, Mac ✓"));

    [MacFact]
    public void Bridge_KnowsTheAvailableSpace() => Assert.True(MacNative.AvailableForImportantUsage("/") > 0);

    [MacFact]
    public async Task RealTrash_TakesATempFile()
    {
        using var dir = new TempDir();
        var file = dir.File($"WaterCa7s-test-{Guid.NewGuid():N}.txt", "тест Корзины");
        var outcome = await new MacTrash(new ProcessCommandRunner()).MoveAsync(file, CancellationToken.None);
        Assert.True(outcome.Ok, outcome.Error);
        Assert.False(File.Exists(file));
    }
}
