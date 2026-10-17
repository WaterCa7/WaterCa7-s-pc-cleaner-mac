using PcCleaner.Mac.Tests.Support;
using PcCleaner.Modules.Startup;
using Xunit;

namespace PcCleaner.Mac.Tests.Startup;

public class LaunchdParsersTests
{
    [Fact]
    public void List_RunningAndStopped()
    {
        var list = LaunchdParsers.ParseList(Fixture.Text("launchd/list.txt"));
        Assert.Equal(new LaunchdListItem(945, 0), list["testuser-provisioner"]);
        Assert.Equal(new LaunchdListItem(null, 0), list["com.openssh.ssh-agent"]);
        Assert.False(list.ContainsKey("Label"));
        Assert.Equal(new LaunchdListItem(null, -9), LaunchdParsers.ParseList("PID\tStatus\tLabel\n-\t-9\tcom.example.killed\n")["com.example.killed"]);
    }

    [Fact]
    public void PrintDisabled()
    {
        var gui = LaunchdParsers.ParsePrintDisabled(Fixture.Text("launchd/disabled-gui.txt"));
        Assert.True(gui["com.apple.Siri.agent"]);
        var system = LaunchdParsers.ParsePrintDisabled(Fixture.Text("launchd/disabled-system.txt"));
        Assert.False(system["com.veertu.anka.addons.ankaupd"]);
        Assert.True(LaunchdParsers.ParsePrintDisabled("\t\"com.example.old\" => true\n")["com.example.old"]);
    }

    [Fact]
    public void Print_Daemon_IgnoresNestedBlocks()
    {
        var print = LaunchdParsers.ParsePrint(Fixture.Text("launchd/print-system-ankaupd.txt"));
        Assert.Equal(
            new LaunchdPrint("/Library/LaunchDaemons/com.veertu.anka.addons.ankaupd.plist", "LaunchDaemon",
                "/Library/Application Support/Veertu/Anka/addons/ankaupd.sh", false, null, 0),
            print);
    }

    [Fact]
    public void Print_RunningAgent()
    {
        var print = LaunchdParsers.ParsePrint(Fixture.Text("launchd/print-gui-hca.txt"));
        Assert.Equal((true, 945, (int?)null, "/opt/hca/start_hca.sh"), (print.Running, print.Pid, print.LastExit, print.Program));
        Assert.Equal(78, LaunchdParsers.ParsePrint("x = {\n\tstate = not running\n\tlast exit code = 78: EX_CONFIG\n}\n").LastExit);
    }
}
