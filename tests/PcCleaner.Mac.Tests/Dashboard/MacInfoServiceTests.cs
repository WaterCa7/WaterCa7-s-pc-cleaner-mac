using PcCleaner.Core;
using PcCleaner.Mac.Tests.Support;
using PcCleaner.Modules.Dashboard;
using Xunit;

namespace PcCleaner.Mac.Tests.Dashboard;

public class MacInfoServiceTests
{
    /// <summary>A MacBook Air M2 with macOS 15.1 and a third of the disk free.</summary>
    internal static FakeCommandRunner Mac() => new FakeCommandRunner()
        .OnFixture("/usr/sbin/scutil", new[] { "--get", "ComputerName" }, "scutil/computername.txt")
        .OnFixture("/usr/bin/sw_vers", Array.Empty<string>(), "sw_vers/macos15.txt")
        .OnFixture("/usr/sbin/sysctl", new[] { "-n", "kern.boottime" }, "sysctl/boottime.txt")
        .OnFixture("/usr/sbin/system_profiler", new[] { "SPHardwareDataType", "-json" }, "system_profiler/hardware-m2.json")
        .OnFixture("/bin/df", new[] { "-k", "-P", "/System/Volumes/Data" }, "df/data-volume.txt");

    [Fact]
    public async Task Load_FillsTheRows()
    {
        var info = await new MacInfoService(Mac()).LoadAsync(CancellationToken.None);
        var rows = info.Rows(DateTimeOffset.FromUnixTimeSeconds(1729152000).AddHours(5)).ToDictionary(r => r.Label, r => r.Value);
        Assert.Equal("Mac Тестовый", rows["Имя Mac"]);
        Assert.Equal("MacBook Air (Mac14,2)", rows["Модель"]);
        Assert.Equal("Apple M2", rows["Чип"]);
        Assert.Equal("16 ГБ", rows["Память"]);
        Assert.Equal("macOS Sequoia 15.1 (24B83)", rows["macOS"]);
        Assert.Equal("свободно 143 ГБ из 460 ГБ (31,1%)", rows["Диск"]);
        Assert.Equal("5 ч 0 мин", rows["Без перезагрузки"]);
    }

    [Fact]
    public async Task Load_NothingWorks_SaysNoData()
    {
        var info = await new MacInfoService(new FakeCommandRunner()).LoadAsync(CancellationToken.None);
        Assert.All(info.Rows(DateTimeOffset.Now), r => Assert.Equal("нет данных", r.Value));
    }

    [Fact]
    public async Task Disk_FallsBackToRoot()
    {
        var fake = new FakeCommandRunner().OnFixture("/bin/df", new[] { "-k", "-P", "/" }, "df/data-volume.txt");
        Assert.NotNull(await new MacInfoService(fake).DiskAsync(CancellationToken.None));
    }

    [MacFact]
    public async Task RealMac_GivesVersionAndDisk()
    {
        var info = await new MacInfoService(new ProcessCommandRunner()).LoadAsync(CancellationToken.None);
        Assert.NotNull(info.OsVersion);
        Assert.NotNull(info.Hardware?.Chip);
        Assert.True(info.Disk!.TotalBytes > 0);
        Assert.NotNull(info.BootTime);
    }
}
