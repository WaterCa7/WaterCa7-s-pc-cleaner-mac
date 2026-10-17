using PcCleaner.Core;
using PcCleaner.Mac.Tests.Cleaner;
using PcCleaner.Mac.Tests.Support;
using PcCleaner.Modules.Processes;
using PcCleaner.Modules.Programs;
using Xunit;

namespace PcCleaner.Mac.Tests.Programs;

/// <summary>The page model on fake apps: nothing changes without «Да».</summary>
[Collection("dialogs")]
public class ProgramsViewModelTests
{
    internal static readonly DateTime Now = new(2026, 10, 18, 12, 0, 0, DateTimeKind.Utc);
    private static readonly string[] CommArgs = { "-axww", "-o", "pid=,ppid=,uid=,pcpu=,rss=,etime=,comm=" };
    private static readonly string[] ArgsArgs = { "-axww", "-o", "pid=,args=" };

    internal sealed class Page : IDisposable
    {
        public Page()
        {
            var root = Apps.Folders.Applications;
            Writer = Apps.App(root, "Writer.app", FakeApps.Plist("com.vendor.writer", "Writer", "Writer", "3.2"));
            File.WriteAllBytes(Path.Combine(Writer, "Contents", "Resources.bin"), new byte[300_000]);
            Apps.App(root, "Reader.app", FakeApps.Plist("com.vendor.reader", "Reader", "Reader"));
            Apps.App(root, "MacKeeper.app", FakeApps.Plist("com.mackeeper.MacKeeper", "MacKeeper", "MacKeeper"));
            Apps.App(root, "Old Game.app", FakeApps.Plist("com.example.oldgame", "Old Game", "Old Game"), MachOTests.Thin(MachOTests.X86_64));
            Apps.App(root, "Store.app", FakeApps.Plist("com.example.store", "Store", "Store"), appStore: true);
            Tool = Apps.App(root, "Vendor/Tool.app", FakeApps.Plist("com.vendor.tool", "Tool", "Tool"));
            Uninstaller = Apps.App(root, "Vendor/Uninstall Tool.app", null);
            Apps.App(Apps.Folders.Apple[0], "Chess.app", FakeApps.Plist("com.apple.Chess", "Chess", "Chess"));

            var library = Path.Combine(Apps.Home, "Library");
            void L(string relative) { Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(library, relative))!); File.WriteAllText(Path.Combine(library, relative), "x"); }
            L("Caches/com.vendor.writer/Cache.db");
            L("Preferences/com.vendor.writer.plist");
            L("Application Support/Writer/old.db");

            Fake = new FakeCommandRunner()
                .On("/bin/ps", CommArgs, new CommandResult(0, "", ""))
                .On("/bin/ps", ArgsArgs, new CommandResult(0, "", ""));
            var signatures = new CodeSignatureService(Fake);
            var processes = new ProcessesService(Fake, signatures, 999, 501, _ => false, TimeSpan.Zero, _ => null);
            var programs = new ProgramsService(new AppCatalog(Apps.Folders, Fake, signatures), Trash, processes, Apps.Home, () => FdaState.Denied);
            Vm = new ProgramsViewModel(programs, new MacShell(Fake), Apps.Home, () => Now);
        }

        public FakeApps Apps { get; } = new();

        public string Writer { get; }

        public string Tool { get; }

        public string Uninstaller { get; }

        public FakeCommandRunner Fake { get; }

        public CleanerViewModelTests.FakeTrash Trash { get; } = new();

        public ProgramsViewModel Vm { get; }

        public async Task LoadAsync()
        {
            await Vm.OnNavigatedToAsync();
            await Vm.Measuring;
        }

        public async Task<AppRow> SelectAsync(string name)
        {
            Vm.Selected = Vm.Apps.Single(r => r.Name == name);
            await Vm.LeftoverSearch;
            return Vm.Selected;
        }

        public void Dispose() => Apps.Dispose();
    }

    private static void Answer(params bool[] answers)
    {
        var queue = new Queue<bool>(answers);
        Dialogs.ConfirmHandler = _ => Task.FromResult(queue.Count > 0 && queue.Dequeue());
    }

    [Fact]
    public async Task List_Filters_Search_Sort()
    {
        using var page = new Page();
        await page.LoadAsync();
        var vm = page.Vm;
        Assert.Equal(new[] { "MacKeeper", "Old Game", "Reader", "Store", "Tool", "Uninstall Tool", "Writer" }, vm.Apps.Select(r => r.Name));
        Assert.All(vm.Apps, r => Assert.NotNull(r.Size));
        Assert.StartsWith("Установлено 7 программ, вместе ", vm.Summary, StringComparison.Ordinal);
        Assert.EndsWith(", показано 7; нежелательных: 1.", vm.Summary, StringComparison.Ordinal);

        vm.Filter = ProgramsViewModel.Filters.Single(f => f.Value == AppFilter.Unwanted);
        Assert.Equal(new[] { "MacKeeper" }, vm.Apps.Select(r => r.Name));
        vm.Filter = ProgramsViewModel.Filters.Single(f => f.Value == AppFilter.IntelOnly);
        Assert.Equal(new[] { "Old Game" }, vm.Apps.Select(r => r.Name));
        vm.Filter = ProgramsViewModel.Filters.Single(f => f.Value == AppFilter.AppStore);
        Assert.Equal(new[] { "Store" }, vm.Apps.Select(r => r.Name));
        vm.Filter = ProgramsViewModel.Filters[0];
        vm.SearchText = "writ";
        Assert.Equal(new[] { "Writer" }, vm.Apps.Select(r => r.Name));
        vm.SearchText = "";
        vm.Sort = ProgramsViewModel.Sorts.Single(s => s.Value == "size");
        Assert.Equal("Writer", vm.Apps[0].Name);
    }

    [Fact]
    public async Task Selection_FindsLeftovers()
    {
        using var page = new Page();
        await page.LoadAsync();
        var writer = await page.SelectAsync("Writer");
        Assert.Equal(new[] { "~/Library/Application Support/Writer", "~/Library/Caches/com.vendor.writer", "~/Library/Preferences/com.vendor.writer.plist" },
            page.Vm.Leftovers.Select(l => l.PathText).Order(StringComparer.Ordinal));
        Assert.All(page.Vm.Leftovers, l => Assert.True(l.IsChecked));
        Assert.True(page.Vm.LeftoversNeedFda);
        Assert.StartsWith("Остатки в ~/Library — отмеченные уйдут в Корзину", page.Vm.LeftoversText, StringComparison.Ordinal);
        Assert.Equal("Версия 3.2 · com.vendor.writer", writer.DetailsText);
        Assert.True(page.Vm.UninstallCommand.CanExecute(null));
    }

    [Fact]
    public async Task Uninstall_OnlyAfterYes_AndOnlyTickedLeftovers()
    {
        using var page = new Page();
        await page.LoadAsync();
        var writer = await page.SelectAsync("Writer");
        try
        {
            Dialogs.UseSilentMode();
            await page.Vm.UninstallAsync(writer);
            Assert.Empty(page.Trash.Moved);

            page.Vm.Leftovers.Single(l => l.PathText.EndsWith("Application Support/Writer", StringComparison.Ordinal)).IsChecked = false;
            Answer(true);
            await page.Vm.UninstallAsync(writer);
            Assert.Equal(page.Writer, page.Trash.Moved[0]);
            Assert.Equal(3, page.Trash.Moved.Count);
            Assert.DoesNotContain(page.Trash.Moved, p => p.Contains("Application Support", StringComparison.Ordinal));
            Assert.StartsWith("«Writer» перемещена в Корзину вместе с 2 остатками", page.Vm.LastResult, StringComparison.Ordinal);
        }
        finally
        {
            Dialogs.UseSilentMode();
        }
    }

    [Fact]
    public async Task RunningApp_MustBeClosedFirst()
    {
        using var page = new Page();
        await page.LoadAsync();
        var writer = await page.SelectAsync("Writer");
        page.Fake.On("/bin/ps", CommArgs, new CommandResult(0, $"  700     1   501   1.0  1024 05:00 {page.Writer}/Contents/MacOS/Writer\n", ""));
        try
        {
            Answer(true, false);
            await page.Vm.UninstallAsync(writer);
            Assert.Empty(page.Trash.Moved);
            Answer(true, true, false);
            await page.Vm.UninstallAsync(writer);
            Assert.Empty(page.Trash.Moved);
            Assert.DoesNotContain(page.Fake.Calls, c => c.StartsWith("/bin/kill", StringComparison.Ordinal));
        }
        finally
        {
            Dialogs.UseSilentMode();
        }
    }

    [Fact]
    public async Task OwnUninstaller_IsOfferedFirst()
    {
        using var page = new Page();
        await page.LoadAsync();
        var tool = await page.SelectAsync("Tool");
        Assert.True(tool.HasUninstaller);
        try
        {
            Answer(true, true);
            await page.Vm.UninstallAsync(tool);
            Assert.Empty(page.Trash.Moved);
            Assert.Contains($"/usr/bin/open {page.Uninstaller}", page.Fake.Calls);
            Assert.StartsWith("Открыт деинсталлятор «Uninstall Tool»", page.Vm.LastResult, StringComparison.Ordinal);

            Answer(true, false);
            await page.Vm.UninstallAsync(tool);
            Assert.Equal(new[] { page.Tool }, page.Trash.Moved);
        }
        finally
        {
            Dialogs.UseSilentMode();
        }
    }

    [Fact]
    public async Task AppleApps_OnRequest_ReadOnly()
    {
        using var page = new Page();
        await page.LoadAsync();
        Assert.DoesNotContain(page.Vm.Apps, r => r.Name == "Chess");
        page.Vm.ShowApple = true;
        await page.Vm.Reloading;
        var chess = await page.SelectAsync("Chess");
        Assert.False(page.Vm.UninstallCommand.CanExecute(null));
        Assert.Equal("Встроена в macOS — удалить нельзя.", chess.ProtectedText);
        Assert.Empty(page.Vm.Leftovers);
        Answer(true, true, true);
        await page.Vm.UninstallAsync(chess);
        Dialogs.UseSilentMode();
        Assert.Empty(page.Trash.Moved);
    }
}
