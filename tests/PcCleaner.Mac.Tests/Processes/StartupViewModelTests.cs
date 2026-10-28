using PcCleaner.Core;
using PcCleaner.Mac.Tests.Cleaner;
using PcCleaner.Mac.Tests.Startup;
using PcCleaner.Mac.Tests.Support;
using PcCleaner.Modules.Processes;
using PcCleaner.Modules.Startup;
using Xunit;

namespace PcCleaner.Mac.Tests.Processes;

/// <summary>The page model of «Автозагрузка» on fixture data: nothing changes without «Да».</summary>
[Collection("dialogs")]
public class StartupViewModelTests
{
    internal const string LoginItemsText = "Telegram\t/Applications/Telegram.app\tfalse\nМузыка\t/Applications/Музыка.app\ttrue\n";

    internal sealed class Page : IDisposable
    {
        public Page()
        {
            Fake = StartupServiceTests.Runner()
                .On("/usr/bin/osascript", LoginItemsService.Arguments(LoginItemsService.ListScript), new CommandResult(0, LoginItemsText, ""));
            var signatures = new CodeSignatureService(Fake, _ => new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc));
            Func<string, bool> missing = path => path.Contains("/missing/", StringComparison.Ordinal) || path.EndsWith("/gone", StringComparison.Ordinal);
            var startup = new StartupService(Home.Folders, 501, Fake, signatures, Home.Backups, new AdminPrompt(Fake), Trash, missing);
            Vm = new StartupViewModel(startup, new LoginItemsService(Fake, Home.Backups), new MacShell(Fake));
        }

        public StartupHome Home { get; } = new();

        public FakeCommandRunner Fake { get; }

        public CleanerViewModelTests.FakeTrash Trash { get; } = new();

        public StartupViewModel Vm { get; }

        public void Dispose() => Home.Dispose();
    }

    private static async Task<Page> LoadedAsync()
    {
        var page = new Page();
        await page.Vm.OnNavigatedToAsync();
        return page;
    }

    private static void Answer(params bool[] answers)
    {
        var queue = new Queue<bool>(answers);
        Dialogs.ConfirmHandler = _ => Task.FromResult(queue.Count > 0 && queue.Dequeue());
    }

    [Fact]
    public void ThePage_IsStartupOfTheSystemSection()
    {
        using var page = new Page();
        Assert.Equal("Startup", page.Vm.Key);
        Assert.Equal("Автозагрузка", page.Vm.Title);
        Assert.Equal("Sync", page.Vm.Icon);
    }

    [Fact]
    public async Task RefreshCommand_ReadsTheStartupItemsAgain()
    {
        using var page = await LoadedAsync();
        var vm = page.Vm;
        var calls = page.Fake.Calls.Count(c => c == "/bin/launchctl list");
        Assert.True(calls >= 1);
        await vm.RefreshCommand.ExecuteAsync();
        Assert.Equal(calls + 1, page.Fake.Calls.Count(c => c == "/bin/launchctl list"));
        Assert.Equal(4, vm.StartupGroups.Count);
    }

    [Fact]
    public async Task Startup_GroupedBySource()
    {
        using var page = await LoadedAsync();
        Assert.Equal(new[] { (LaunchSource.UserAgent, 6), (LaunchSource.GlobalAgent, 1), (LaunchSource.Daemon, 1), (LaunchSource.AppBundled, 1) },
            page.Vm.StartupGroups.Select(g => (g.Source, g.Rows.Count)));
        var broken = page.Vm.StartupGroups[0].Rows.Single(r => r.Label == "com.example.broken");
        Assert.Equal(("Отключена", "Включить", "Сломана: программа не найдена: /Users/anna/missing/tool"), (broken.StateText, broken.ToggleText, broken.ProblemText));
        var hca = page.Vm.StartupGroups[0].Rows.Single(r => r.Label == "testuser-provisioner");
        Assert.Equal(("Работает (PID 945)", "Отключить", true), (hca.StateText, hca.ToggleText, hca.CanRemove));
        Assert.False(page.Vm.StartupGroups[1].Rows[0].CanRemove);
    }

    [Fact]
    public async Task Startup_ChangesOnlyAfterYes()
    {
        using var page = await LoadedAsync();
        var vm = page.Vm;
        var hca = vm.StartupGroups[0].Rows.Single(r => r.Label == "testuser-provisioner");
        page.Fake
            .On("/bin/launchctl", new[] { "disable", "gui/501/testuser-provisioner" }, new CommandResult(0, "", ""))
            .On("/bin/launchctl", new[] { "bootout", "gui/501/testuser-provisioner" }, new CommandResult(0, "", ""));
        try
        {
            Dialogs.UseSilentMode();
            await vm.ToggleAsync(hca);
            await vm.RemoveAsync(hca);
            Assert.DoesNotContain(page.Fake.Calls, c => c.Contains(" disable ", StringComparison.Ordinal) || c.Contains(" bootout ", StringComparison.Ordinal));
            Assert.Empty(page.Trash.Moved);
            Assert.Empty(page.Home.Backups.List());

            Answer(true);
            await vm.ToggleAsync(hca);
            Assert.Contains("/bin/launchctl disable gui/501/testuser-provisioner", page.Fake.Calls);
            Assert.Equal("«testuser-provisioner» отключена — больше не запустится при входе.", vm.LastResult);
            Assert.Single(page.Home.Backups.List());

            Answer(true);
            await vm.RemoveAsync(vm.StartupGroups[0].Rows.Single(r => r.Label == "com.example.script"));
            Assert.Equal(new[] { page.Home.UserPlist("com.example.script.plist") }, page.Trash.Moved);
        }
        finally
        {
            Dialogs.UseSilentMode();
        }
    }

    [Fact]
    public async Task LoginItems_ReadOnAButton_RemovedAfterYes()
    {
        using var page = await LoadedAsync();
        var vm = page.Vm;
        Assert.DoesNotContain(page.Fake.Calls, c => c.StartsWith("/usr/bin/osascript", StringComparison.Ordinal));
        await vm.LoadLoginItemsCommand.ExecuteAsync();
        Assert.Equal(new[] { "Telegram", "Музыка" }, vm.LoginItems.Select(r => r.Name));
        Assert.Equal("/Applications/Музыка.app · открывается скрытой", vm.LoginItems[1].PathText);

        Dialogs.UseSilentMode();
        await vm.RemoveLoginItemAsync(vm.LoginItems[0]);
        Assert.Equal(2, vm.LoginItems.Count);
        Assert.Empty(page.Home.Backups.List());
    }
}
