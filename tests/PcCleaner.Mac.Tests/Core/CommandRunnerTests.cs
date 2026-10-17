using PcCleaner.Core;
using PcCleaner.Mac.Tests.Support;
using Xunit;

namespace PcCleaner.Mac.Tests.Core;

public class CommandRunnerTests
{
    private static (string Exe, string[] Args) Echo(string text) => OperatingSystem.IsWindows()
        ? (Path.Combine(Environment.SystemDirectory, "cmd.exe"), new[] { "/c", "echo " + text })
        : ("/bin/echo", new[] { text });

    [Fact]
    public async Task Run_ReturnsStdoutAndExitCode()
    {
        var (exe, args) = Echo("hello");
        var result = await new ProcessCommandRunner().RunAsync(exe, args);
        Assert.True(result.Ok);
        Assert.Equal("hello", result.StdOut.Trim());
    }

    [Fact]
    public async Task Run_RelativePath_IsRefused()
        => await Assert.ThrowsAsync<ArgumentException>(() => new ProcessCommandRunner().RunAsync("echo", new[] { "x" }));

    [Fact]
    public async Task Run_MissingProgram_IsNotStarted()
    {
        var missing = Path.Combine(TestEnvironment.Root, "no-such-program");
        var result = await new ProcessCommandRunner().RunAsync(missing, Array.Empty<string>());
        Assert.Equal(CommandResult.NotStartedCode, result.ExitCode);
        Assert.False(result.Ok);
    }

    [MacFact]
    public async Task Run_Timeout_KillsTheProcess()
    {
        var result = await new ProcessCommandRunner().RunAsync("/bin/sleep", new[] { "30" }, TimeSpan.FromMilliseconds(300));
        Assert.True(result.TimedOut);
        Assert.False(result.Ok);
    }

    [MacFact]
    public async Task Run_Cancel_Throws()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => new ProcessCommandRunner().RunAsync("/bin/sleep", new[] { "30" }, ct: cts.Token));
    }

    [MacFact]
    public async Task Run_OutputIsEnglishUtf8()
    {
        var result = await new ProcessCommandRunner().RunAsync("/usr/bin/printenv", new[] { "LC_ALL" });
        Assert.Equal("en_US.UTF-8", result.StdOut.Trim());
    }

    [Fact]
    public async Task Fake_ReturnsTheFixtureAndRecordsTheCall()
    {
        var fake = new FakeCommandRunner().OnFixture("/usr/bin/sw_vers", Array.Empty<string>(), "sw_vers/macos15.txt");
        var result = await fake.RunAsync("/usr/bin/sw_vers", Array.Empty<string>());
        Assert.Contains("ProductVersion", result.StdOut);
        Assert.Equal(new[] { "/usr/bin/sw_vers" }, fake.Calls);
        Assert.Equal(CommandResult.NotStartedCode, (await fake.RunAsync("/bin/ls", new[] { "-l" })).ExitCode);
    }
}
