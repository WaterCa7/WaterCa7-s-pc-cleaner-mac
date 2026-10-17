using PcCleaner.Core;
using PcCleaner.Mac.Tests.Support;
using PcCleaner.Modules.Settings;
using PcCleaner.Modules.Startup;
using Xunit;

namespace PcCleaner.Mac.Tests.Shell;

[Collection("dialogs")]
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
    public async Task Backups_AreListed_AndRestoredOnlyAfterYes()
    {
        using var dir = new TempDir();
        var store = new BackupStore(Path.Combine(dir.Path, "Backups"));
        store.SaveLoginItem("Telegram", "/Applications/Telegram.app", hidden: false);
        var fake = new FakeCommandRunner()
            .On("/usr/bin/osascript", LoginItemsService.Arguments(LoginItemsService.AddScript, "/Applications/Telegram.app", "false"), new CommandResult(0, "", ""));
        var restorer = new BackupRestorer(store, dir.Path, 501, fake, new AdminPrompt(fake), new LoginItemsService(fake, store));
        var vm = new SettingsViewModel(new MacShell(fake), () => FdaState.Granted, restorer);
        Assert.True(vm.NoBackups);

        await vm.OnNavigatedToAsync();
        var item = Assert.Single(vm.Backups);
        Assert.Equal("Объект входа «Telegram»", item.Title);
        Assert.EndsWith("— убран из объектов входа", item.WhatText, StringComparison.Ordinal);
        Assert.True(vm.HasBackups);

        try
        {
            Dialogs.UseSilentMode();
            await vm.RestoreAsync(item);
            Assert.Empty(fake.Calls);

            var asked = new List<string>();
            Dialogs.ConfirmHandler = r =>
            {
                asked.Add(r.Message);
                return Task.FromResult(true);
            };
            await vm.RestoreAsync(item);
            Assert.Contains("Программа снова будет открываться при входе.", Assert.Single(asked), StringComparison.Ordinal);
            Assert.Single(fake.Calls, c => c.StartsWith("/usr/bin/osascript", StringComparison.Ordinal));
        }
        finally
        {
            Dialogs.UseSilentMode();
        }
    }

    [Fact]
    public void Version_NamesTheApp() => Assert.StartsWith("WaterCa7s PC Cleaner 1.", new SettingsViewModel(new MacShell(new FakeCommandRunner()), () => FdaState.Unknown).VersionText);
}
