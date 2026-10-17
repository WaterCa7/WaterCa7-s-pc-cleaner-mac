using PcCleaner.Core;
using PcCleaner.Mac.Tests.Support;
using PcCleaner.Modules.Cleaner;
using Xunit;

namespace PcCleaner.Mac.Tests.Cleaner;

public class DiskServiceTests
{
    [Fact]
    public void Volumes_OnlyRealDevices()
    {
        var all = DiskParsers.ParseVolumes(Fixture.Text("df/all-volumes.txt"));
        Assert.Equal(9, all.Count);
        Assert.DoesNotContain(all, v => v.Mount is "/dev" or "/System/Volumes/Data/home");
    }

    [Fact]
    public void UserVolumes_AreTheDataVolumeAndExternalDrives()
    {
        var user = DiskParsers.UserVolumes(DiskParsers.ParseVolumes(Fixture.Text("df/all-volumes.txt")));
        Assert.Equal(new[] { "/System/Volumes/Data", "/Volumes/Мой диск" }, user.Select(v => v.Mount));
        Assert.Equal(576490576L * 1024, user[1].FreeBytes);
    }

    [Fact]
    public void UserVolumes_WithoutADataVolume_UseTheRoot()
        => Assert.Equal("/", Assert.Single(DiskParsers.UserVolumes(new[] { new VolumeUsage("/dev/disk1s1", "/", 100, 50, 50) })).Mount);

    [Fact]
    public void Snapshots_AreSortedTimes()
        => Assert.Equal(new[]
            {
                new DateTime(2026, 10, 16, 22, 10, 4),
                new DateTime(2026, 10, 17, 10, 15, 30),
                new DateTime(2026, 10, 17, 11, 15, 31),
            },
            DiskParsers.ParseSnapshots(Fixture.Text("tmutil/three.txt")));

    [Theory]
    [InlineData("tmutil/none-runner.txt")]
    [InlineData("df/all-volumes.txt")]
    public void Snapshots_None(string fixture) => Assert.Empty(DiskParsers.ParseSnapshots(Fixture.Text(fixture)));

    [Fact]
    public async Task Load_NamesTheStartupDisk_AndKnowsPurgeableSpace()
    {
        var runner = new FakeCommandRunner()
            .OnFixture("/bin/df", new[] { "-k", "-P" }, "df/all-volumes.txt")
            .OnFixture("/usr/sbin/diskutil", new[] { "info", "-plist", "/" }, "diskutil/root-runner.plist")
            .OnFixture("/usr/bin/tmutil", new[] { "listlocalsnapshots", "/" }, "tmutil/three.txt");
        var info = await new DiskService(runner, mount => mount == "/System/Volumes/Data" ? 160123456L * 1024 : 100).LoadAsync(CancellationToken.None);
        Assert.Equal(new[] { "Macintosh HD", "Мой диск" }, info.Volumes.Select(v => v.Name));
        Assert.Equal(10000000L * 1024, info.Volumes[0].PurgeableBytes);
        Assert.Equal(0, info.Volumes[1].PurgeableBytes);
        Assert.Equal(69, Math.Round(info.Volumes[0].UsedPercent));
        Assert.True(info.SnapshotsKnown);
        Assert.Equal(3, info.Snapshots.Count);
    }

    [Fact]
    public async Task Load_NothingWorks_IsEmpty()
    {
        var info = await new DiskService(new FakeCommandRunner(), _ => null).LoadAsync(CancellationToken.None);
        Assert.Empty(info.Volumes);
        Assert.False(info.SnapshotsKnown);
    }

    [MacFact]
    public async Task RealMac_HasTheStartupDisk()
    {
        var info = await new DiskService(new ProcessCommandRunner()).LoadAsync(CancellationToken.None);
        Assert.True(info.Volumes[0].TotalBytes > 0);
        Assert.NotNull(info.Volumes[0].AvailableBytes);
        Assert.True(info.SnapshotsKnown);
    }
}
