using PcCleaner.Mac.Tests.Support;
using PcCleaner.Modules.Dashboard;
using Xunit;

namespace PcCleaner.Mac.Tests.Dashboard;

public class MacInfoParsersTests
{
    [Fact]
    public void SwVers()
    {
        var (name, version, build) = MacInfoParsers.ParseSwVers(Fixture.Text("sw_vers/macos15.txt"));
        Assert.Equal(("macOS", "15.1", "24B83"), (name, version, build));
    }

    [Fact]
    public void SwVers_Garbage_GivesNulls()
        => Assert.Equal(((string?)null, (string?)null, (string?)null), MacInfoParsers.ParseSwVers("что-то не то"));

    [Fact]
    public void BootTime()
        => Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1729152000), MacInfoParsers.ParseBootTime(Fixture.Text("sysctl/boottime.txt")));

    [Fact]
    public void BootTime_Garbage_IsNull() => Assert.Null(MacInfoParsers.ParseBootTime("sysctl: unknown oid"));

    [Fact]
    public void Hardware_AppleSilicon()
    {
        var hw = MacInfoParsers.ParseHardware(Fixture.Text("system_profiler/hardware-m2.json"))!;
        Assert.Equal("MacBook Air", hw.ModelName);
        Assert.Equal("Mac14,2", hw.ModelId);
        Assert.Equal("Apple M2", hw.Chip);
        Assert.Equal(16L << 30, hw.MemoryBytes);
        Assert.Equal("proc 8:4:4", hw.Cores);
    }

    [Fact]
    public void Hardware_Intel_UsesCpuType()
    {
        var hw = MacInfoParsers.ParseHardware(Fixture.Text("system_profiler/hardware-intel.json"))!;
        Assert.Equal("Quad-Core Intel Core i5", hw.Chip);
        Assert.Equal("4", hw.Cores);
    }

    // Real output of the GitHub Actions runners (macOS 14 and 26, Apple Silicon virtual machines).
    [Theory]
    [InlineData("sw_vers/runner-macos14.txt", "14.8.9", "23J631")]
    [InlineData("sw_vers/runner-macos26.txt", "26.6.2", "25G83")]
    public void SwVers_Runners(string fixture, string version, string build)
    {
        var (name, v, b) = MacInfoParsers.ParseSwVers(Fixture.Text(fixture));
        Assert.Equal(("macOS", version, build), (name, v, b));
    }

    [Fact]
    public void Hardware_VirtualMac_HasANumericProcessorCount()
    {
        var hw = MacInfoParsers.ParseHardware(Fixture.Text("system_profiler/hardware-runner-vm.json"))!;
        Assert.Equal(("Apple Virtual Machine 1", "VirtualMac2,1", "Apple M1 (Virtual)", "3"), (hw.ModelName, hw.ModelId, hw.Chip, hw.Cores));
        Assert.Equal(7L << 30, hw.MemoryBytes);
    }

    [Theory]
    [InlineData("df/runner-macos14.txt", 335032320L, 37839180L)]
    [InlineData("df/runner-macos26.txt", 335032320L, 96307596L)]
    public void Df_Runners(string fixture, long totalKb, long freeKb)
    {
        var disk = MacInfoParsers.ParseDf(Fixture.Text(fixture))!;
        Assert.Equal(("/System/Volumes/Data", totalKb * 1024, freeKb * 1024), (disk.Mount, disk.TotalBytes, disk.FreeBytes));
    }

    [Fact]
    public void BootTime_Runner() => Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1792262259), MacInfoParsers.ParseBootTime(Fixture.Text("sysctl/boottime-runner.txt")));

    [Theory]
    [InlineData("")]
    [InlineData("{}")]
    [InlineData("{\"SPHardwareDataType\":[]}")]
    [InlineData("не json")]
    public void Hardware_Garbage_IsNull(string json) => Assert.Null(MacInfoParsers.ParseHardware(json));

    [Theory]
    [InlineData("16 GB", 16L << 30)]
    [InlineData("8 GB", 8L << 30)]
    [InlineData("512 MB", 512L << 20)]
    [InlineData("1 TB", 1L << 40)]
    [InlineData("много", 0L)]
    public void MemoryText(string text, long bytes) => Assert.Equal(bytes, MacInfoParsers.ParseMemoryText(text));

    [Fact]
    public void Df()
    {
        var disk = MacInfoParsers.ParseDf(Fixture.Text("df/data-volume.txt"))!;
        Assert.Equal("/System/Volumes/Data", disk.Mount);
        Assert.Equal(482797652L * 1024, disk.TotalBytes);
        Assert.Equal(150123456L * 1024, disk.FreeBytes);
        Assert.Equal(31.1, Math.Round(disk.FreePercent, 1));
    }

    [Fact]
    public void Df_MountWithSpaces()
        => Assert.Equal("/Volumes/Мой диск", MacInfoParsers.ParseDf("Filesystem 1024-blocks Used Available Capacity Mounted on\n/dev/disk4s1 1000 400 600 40% /Volumes/Мой диск\n")!.Mount);

    [Fact]
    public void Df_Empty_IsNull() => Assert.Null(MacInfoParsers.ParseDf("Filesystem 1024-blocks Used Available Capacity Mounted on\n"));

    [Theory]
    [InlineData("15.1", "macOS Sequoia 15.1")]
    [InlineData("14.7.1", "macOS Sonoma 14.7.1")]
    [InlineData("26.0", "macOS Tahoe 26.0")]
    [InlineData("27.0", "macOS 27.0")]
    [InlineData(null, "macOS")]
    public void Names(string? version, string expected) => Assert.Equal(expected, MacOsNames.Marketing(version));
}
