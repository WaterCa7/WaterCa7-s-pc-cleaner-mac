using PcCleaner.Mac.Tests.Support;
using PcCleaner.Modules.Processes;
using Xunit;

namespace PcCleaner.Mac.Tests.Processes;

public class ProcessParsersTests
{
    [Fact]
    public void Ci_EveryLineParses_IncludingNamesWithSpacesAndBareNames()
    {
        var all = ProcessParsers.ParseComm(Fixture.Text("ps/ci-comm.txt"));
        Assert.Equal(542, all.Count);
        var launchd = all[0];
        Assert.Equal((1, 0, 0, "/sbin/launchd", "launchd"), (launchd.Pid, launchd.Ppid, launchd.Uid, launchd.Command, launchd.Name));
        Assert.Equal(13024L * 1024, launchd.MemoryBytes);
        Assert.Equal(new TimeSpan(0, 4, 59), launchd.Elapsed);
        var audio = all.Single(p => p.Pid == 241);
        Assert.Equal(("Core Audio Driver (NullAudio.driver)", false, 202), (audio.Command, audio.HasPath, audio.Uid));
        Assert.Equal(56.5, all.Single(p => p.Pid == 4430).CpuPercent);
    }

    [Fact]
    public void Join_AddsCommandLines()
    {
        var all = ProcessParsers.Join(Fixture.Text("ps/ci-comm.txt"), Fixture.Text("ps/ci-args.txt"));
        Assert.Equal("/usr/libexec/UserEventAgent (System)", all.Single(p => p.Pid == 88).CommandLine);
        Assert.Equal("/usr/sbin/systemstats --daemon", all.Single(p => p.Pid == 93).CommandLine);
        Assert.Null(ProcessParsers.Join(Fixture.Text("ps/ci-comm.txt"), "").Single(p => p.Pid == 88).CommandLine);
    }

    [Theory]
    [InlineData("05", 0, 0, 0, 5)]
    [InlineData("04:59", 0, 0, 4, 59)]
    [InlineData("01:02:03", 0, 1, 2, 3)]
    [InlineData("2-03:04:05", 2, 3, 4, 5)]
    public void Elapsed(string text, int days, int hours, int minutes, int seconds)
        => Assert.Equal(new TimeSpan(days, hours, minutes, seconds), ProcessParsers.ParseElapsed(text));

    [Fact]
    public void AppBundle_IsTheOuterApp_EvenForHelpers()
    {
        var all = ProcessParsers.ParseComm(Fixture.Text("ps/mixed-comm.txt"));
        var helper = all.Single(p => p.Pid == 402);
        Assert.Equal(("/Applications/Google Chrome.app", "Google Chrome Helper (Renderer)"), (helper.AppBundle, helper.Name));
        Assert.Equal(new TimeSpan(2, 3, 4, 5), all.Single(p => p.Pid == 401).Elapsed);
        Assert.Equal("/private/var/folders/zz/abc/T/AppTranslocation/1234/d/Some App.app", all.Single(p => p.Pid == 500).AppBundle);
        var bare = all.Single(p => p.Pid == 560);
        Assert.Equal((false, (string?)null, "dotnet"), (bare.HasPath, bare.AppBundle, bare.Name));
        Assert.Null(all.Single(p => p.Pid == 530).AppBundle);
    }
}
