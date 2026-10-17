using PcCleaner.Core;
using PcCleaner.Mac.Tests.Support;
using PcCleaner.Modules.Settings;
using Xunit;

namespace PcCleaner.Mac.Tests.Shell;

public class SettingsViewModelTests
{
    [Fact]
    public async Task Fda_IsCheckedOnNavigation()
    {
        var state = FdaState.Denied;
        var vm = new SettingsViewModel(new MacShell(new FakeCommandRunner()), () => state);
        await vm.OnNavigatedToAsync();
        Assert.False(vm.IsFdaGranted);
        Assert.StartsWith("Полного доступа к диску нет", vm.FdaText);
        state = FdaState.Granted;
        vm.RefreshFdaCommand.Execute(null);
        Assert.True(vm.IsFdaGranted);
        Assert.StartsWith("Полный доступ к диску разрешён", vm.FdaText);
    }

    [Fact]
    public void OpenFdaSettings_OpensThePane()
    {
        var fake = new FakeCommandRunner();
        new SettingsViewModel(new MacShell(fake), () => FdaState.Unknown).OpenFdaSettingsCommand.Execute(null);
        Assert.Contains($"/usr/bin/open {SystemSettingsLinks.FullDiskAccess}", fake.Calls);
    }

    [Fact]
    public void Version_NamesTheApp() => Assert.StartsWith("WaterCa7s PC Cleaner 1.", new SettingsViewModel(new MacShell(new FakeCommandRunner()), () => FdaState.Unknown).VersionText);
}
