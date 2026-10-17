using PcCleaner.Core;
using PcCleaner.Mac.Tests.Support;
using Xunit;

namespace PcCleaner.Mac.Tests.Core;

public class AdminPromptTests
{
    private const string Dangerous = "it's \"quoted\" $(touch /tmp/pwned); `id` && echo x";

    [Fact]
    public void Arguments_KeepTheScriptConstant_AndTheDataAsArguments()
    {
        var args = AdminPrompt.Arguments("Включить брандмауэр", new[]
        {
            new AdminCommand("/usr/libexec/ApplicationFirewall/socketfilterfw", "--setglobalstate", "on"),
            new AdminCommand("/bin/launchctl", "disable", "system/" + Dangerous),
        });
        Assert.Equal(AdminPrompt.Script, args.Take(AdminPrompt.Script.Length));
        Assert.Equal(new[]
            {
                "Включить брандмауэр", AdminPrompt.Separator,
                "/usr/libexec/ApplicationFirewall/socketfilterfw", "--setglobalstate", "on", AdminPrompt.Separator,
                "/bin/launchctl", "disable", "system/" + Dangerous,
            },
            args.Skip(AdminPrompt.Script.Length));
        Assert.All(AdminPrompt.Script, line => Assert.DoesNotContain("pwned", line));
    }

    [Fact]
    public void Arguments_RefuseTheSeparatorAndRelativePrograms()
    {
        Assert.Throws<ArgumentException>(() => AdminPrompt.Arguments("x", new[] { new AdminCommand("/bin/echo", AdminPrompt.Separator) }));
        Assert.Throws<ArgumentException>(() => AdminPrompt.Arguments("x", new[] { new AdminCommand("launchctl", "list") }));
        Assert.Throws<ArgumentException>(() => AdminPrompt.Arguments("x", Array.Empty<AdminCommand>()));
    }

    [Fact]
    public async Task Run_Ok_Cancelled_Failed()
    {
        var command = new[] { new AdminCommand("/bin/launchctl", "disable", "system/com.example") };
        var args = AdminPrompt.Arguments("Отключить", command);
        Assert.True((await new AdminPrompt(new FakeCommandRunner().On("/usr/bin/osascript", args, new CommandResult(0, "", "")))
            .RunAsync("Отключить", command, CancellationToken.None)).Ok);
        var cancelled = await new AdminPrompt(new FakeCommandRunner().On("/usr/bin/osascript", args, new CommandResult(1, "", "execution error: User canceled. (-128)")))
            .RunAsync("Отключить", command, CancellationToken.None);
        Assert.Equal(new AdminResult(false, true, "отменено — ничего не изменено"), cancelled);
        var failed = await new AdminPrompt(new FakeCommandRunner().On("/usr/bin/osascript", args, new CommandResult(1, "", "Operation not permitted")))
            .RunAsync("Отключить", command, CancellationToken.None);
        Assert.False(failed.Ok || failed.Cancelled);
    }

    [MacFact]
    public async Task RealMac_ScriptCompiles()
    {
        using var dir = new TempDir();
        var result = await new ProcessCommandRunner().RunAsync("/usr/bin/osacompile",
            new[] { "-o", Path.Combine(dir.Path, "admin.scpt") }.Concat(AdminPrompt.Script).ToArray());
        Assert.True(result.Ok, result.StdErr);
    }

    [MacFact]
    public async Task RealMac_ArgumentsStayText()
    {
        // The same script without the administrator part: the shell gets the quoted words and prints them back.
        var script = AdminPrompt.Script.Select(line => line.StartsWith("do shell script", StringComparison.Ordinal) ? "do shell script commandText" : line);
        var args = script.Concat(new[] { "prompt", AdminPrompt.Separator, "/bin/echo", Dangerous }).ToArray();
        var result = await new ProcessCommandRunner().RunAsync("/usr/bin/osascript", args);
        Assert.True(result.Ok, result.StdErr);
        Assert.Equal(Dangerous, result.StdOut.Trim());
        Assert.False(File.Exists("/tmp/pwned"));
    }
}
