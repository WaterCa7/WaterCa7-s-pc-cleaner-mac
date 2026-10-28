using PcCleaner.Core;
using PcCleaner.Mac.Tests.Support;
using PcCleaner.Modules.Processes;
using Xunit;

namespace PcCleaner.Mac.Tests.Processes;

/// <summary>The page model of «Процессы» on fixture data: nothing is stopped without «Да».</summary>
[Collection("dialogs")]
public class ProcessesViewModelTests
{
    internal sealed class Page
    {
        public Page()
        {
            Fake = new FakeCommandRunner()
                .OnFixture("/bin/ps", new[] { "-axww", "-o", "pid=,ppid=,uid=,pcpu=,rss=,etime=,comm=" }, "ps/mixed-comm.txt")
                .OnFixture("/bin/ps", new[] { "-axww", "-o", "pid=,args=" }, "ps/mixed-args.txt")
                .On("/usr/bin/codesign", new[] { "-dv", "--verbose=2", "/Applications/Google Chrome.app" },
                    new CommandResult(0, "", Fixture.Text("codesign/developer-id-dotnet.txt")));
            var signatures = new CodeSignatureService(Fake, _ => new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc));
            Func<string, bool> missing = path => path.Contains("/missing/", StringComparison.Ordinal) || path.EndsWith("/gone", StringComparison.Ordinal);
            var processes = new ProcessesService(Fake, signatures, 999, 501, missing, TimeSpan.Zero, _ => null);
            Vm = new ProcessesViewModel(processes, new MacShell(Fake));
        }

        public FakeCommandRunner Fake { get; }

        public ProcessesViewModel Vm { get; }
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
    public void ThePage_IsProcessesOfTheSystemSection()
    {
        var page = new Page();
        Assert.Equal("Processes", page.Vm.Key);
        Assert.Equal("Процессы", page.Vm.Title);
        Assert.Equal("Что запущено на Mac сейчас", page.Vm.Subtitle);
    }

    [Fact]
    public async Task Processes_HideSystem_Search_Sort()
    {
        var page = await LoadedAsync();
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
        var page = await LoadedAsync();
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
        var page = await LoadedAsync();
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
    public void WebLinks_SendOnlyTheNameOrTheHash()
    {
        Assert.Equal("https://www.google.com/search?q=Google%20Chrome%20macOS%20%D1%87%D1%82%D0%BE%20%D1%8D%D1%82%D0%BE", WebLinks.Search("Google Chrome macOS что это"));
        Assert.Equal("https://www.virustotal.com/gui/file/ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad",
            WebLinks.VirusTotal("BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD"));
    }
}
