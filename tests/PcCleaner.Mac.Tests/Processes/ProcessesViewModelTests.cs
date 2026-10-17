using PcCleaner.Core;
using PcCleaner.Mac.Tests.Cleaner;
using PcCleaner.Mac.Tests.Startup;
using PcCleaner.Mac.Tests.Support;
using PcCleaner.Modules.Processes;
using PcCleaner.Modules.Startup;
using Xunit;

namespace PcCleaner.Mac.Tests.Processes;

/// <summary>The page model on fixture data: nothing changes without «Да».</summary>
[Collection("dialogs")]
public class ProcessesViewModelTests
{
    internal const string LoginItemsText = "Telegram\t/Applications/Telegram.app\tfalse\nМузыка\t/Applications/Музыка.app\ttrue\n";

    internal sealed class Page : IDisposable
    {
        public Page()
        {
            Fake = StartupServiceTests.Runner()
                .OnFixture("/bin/ps", new[] { "-axww", "-o", "pid=,ppid=,uid=,pcpu=,rss=,etime=,comm=" }, "ps/mixed-comm.txt")
                .OnFixture("/bin/ps", new[] { "-axww", "-o", "pid=,args=" }, "ps/mixed-args.txt")
                .On("/usr/bin/osascript", LoginItemsService.Arguments(LoginItemsService.ListScript), new CommandResult(0, LoginItemsText, ""))
                .On("/usr/bin/codesign", new[] { "-dv", "--verbose=2", "/Applications/Google Chrome.app" },
                    new CommandResult(0, "", Fixture.Text("codesign/developer-id-dotnet.txt")));
            var signatures = new CodeSignatureService(Fake, _ => new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc));
            Func<string, bool> missing = path => path.Contains("/missing/", StringComparison.Ordinal) || path.EndsWith("/gone", StringComparison.Ordinal);
            var processes = new ProcessesService(Fake, signatures, 999, 501, missing, TimeSpan.Zero);
            var startup = new StartupService(Home.Folders, 501, Fake, signatures, Home.Backups, new AdminPrompt(Fake), Trash, missing);
            Vm = new ProcessesViewModel(processes, startup, new LoginItemsService(Fake, Home.Backups), new MacShell(Fake));
        }

        public StartupHome Home { get; } = new();

        public FakeCommandRunner Fake { get; }

        public CleanerViewModelTests.FakeTrash Trash { get; } = new();

        public ProcessesViewModel Vm { get; }

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
    public async Task Processes_HideSystem_Search_Sort()
    {
        using var page = await LoadedAsync();
        var vm = page.Vm;
        Assert.DoesNotContain(vm.Processes, r => r.Entry.Info.Pid is 1 or 160);
        Assert.Contains(vm.Processes, r => r.Name == "MacKeeper" && r.HasStrongHint);
        Assert.Equal("Запущено 13 процессов, показано 11; с важными подсказками: 4.", vm.ProcessSummary);

        vm.HideSystem = false;
        Assert.Equal(13, vm.Processes.Count);
        vm.SearchText = "chrome";
        Assert.Equal(2, vm.Processes.Count);
        vm.SearchText = "";
        vm.Sort = ProcessesViewModel.Sorts[1];
        Assert.Equal(402, vm.Processes[0].Entry.Info.Pid);
        vm.Sort = ProcessesViewModel.Sorts[2];
        Assert.Equal("agent", vm.Processes[0].Name);
    }

    [Fact]
    public async Task Selection_DrivesTheActions()
    {
        using var page = await LoadedAsync();
        var vm = page.Vm;
        Assert.True(vm.NoSelection);
        Assert.False(vm.TerminateCommand.CanExecute(null));
        vm.HideSystem = false;
        vm.Selected = vm.Processes.Single(r => r.Entry.Info.Pid == 550);
        Assert.False(vm.TerminateCommand.CanExecute(null));
        Assert.Equal("Завершить нельзя: системный процесс (root)", vm.Selected.ProtectedText);
        vm.Selected = vm.Processes.Single(r => r.Entry.Info.Pid == 401);
        Assert.True(vm.TerminateCommand.CanExecute(null));
        Assert.Equal("Подпись: Developer ID: Microsoft Corporation (UBF8T346G9)", vm.Selected.SignatureText);
    }

    [Fact]
    public async Task Terminate_OnlyAfterYes_AndForceOnlyAfterASecondYes()
    {
        using var page = await LoadedAsync();
        var vm = page.Vm;
        var row = vm.Processes.Single(r => r.Entry.Info.Pid == 401);
        page.Fake
            .On("/bin/ps", new[] { "-p", "401", "-o", "uid=,comm=" }, new CommandResult(0, "  501 /Applications/Google Chrome.app/Contents/MacOS/Google Chrome\n", ""))
            .On("/bin/kill", new[] { "-TERM", "401" }, new CommandResult(0, "", ""))
            .On("/bin/kill", new[] { "-KILL", "401" }, new CommandResult(0, "", ""))
            .On("/bin/kill", new[] { "-0", "401" }, new CommandResult(0, "", ""));
        try
        {
            Answer(false);
            await vm.TerminateAsync(row);
            Assert.DoesNotContain(page.Fake.Calls, c => c.StartsWith("/bin/kill", StringComparison.Ordinal));

            Answer(true, false);
            await vm.TerminateAsync(row);
            Assert.Contains("/bin/kill -TERM 401", page.Fake.Calls);
            Assert.DoesNotContain("/bin/kill -KILL 401", page.Fake.Calls);
            Assert.Contains("принудительно", vm.LastResult, StringComparison.Ordinal);

            Answer(true, true);
            await vm.TerminateAsync(row);
            Assert.Contains("/bin/kill -KILL 401", page.Fake.Calls);
        }
        finally
        {
            Dialogs.UseSilentMode();
        }
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
        Assert.True(vm.LoginItemsLoaded);

        Dialogs.UseSilentMode();
        await vm.RemoveLoginItemAsync(vm.LoginItems[0]);
        Assert.Equal(2, vm.LoginItems.Count);
        Assert.Empty(page.Home.Backups.List());
    }

    [Fact]
    public void WebLinks_SendOnlyTheNameOrTheHash()
    {
        Assert.Equal("https://www.google.com/search?q=Google%20Chrome%20macOS%20%D1%87%D1%82%D0%BE%20%D1%8D%D1%82%D0%BE", WebLinks.Search("Google Chrome macOS что это"));
        Assert.Equal("https://www.virustotal.com/gui/file/ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad",
            WebLinks.VirusTotal("BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD"));
    }
}
