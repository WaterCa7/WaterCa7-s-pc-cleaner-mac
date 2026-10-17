using PcCleaner.Core;
using PcCleaner.Mac.Tests.Support;
using PcCleaner.Modules.Programs;
using Xunit;

namespace PcCleaner.Mac.Tests.Programs;

public class ProgramsContributorTests
{
    private static readonly DateTime Now = new(2026, 10, 18, 12, 0, 0, DateTimeKind.Utc);
    private const long Mb = 1L << 20;

    private static InstalledApp App(string name, DateTime? lastUsed = null, AppArchitecture arch = AppArchitecture.AppleSilicon, string? pup = null,
        AppLocation location = AppLocation.Applications)
        => LeftoverFinderTests.App($"/Applications/{name}.app", name, "com.example." + name.ToLowerInvariant()) with
        {
            LastUsedUtc = lastUsed, Architecture = arch, PupReason = pup, Location = location,
        };

    [Fact]
    public void Pups_AreImportant_OnePerApp()
    {
        var issues = ProgramsRules.Evaluate(new[] { App("MacKeeper", pup: "MacKeeper — навязчивый «оптимизатор»"), App("Telegram") },
            new Dictionary<string, long>(), Now);
        var issue = Assert.Single(issues);
        Assert.Equal((IssueSeverity.High, "Потенциально нежелательная программа «MacKeeper»", "Programs"), (issue.Severity, issue.Title, issue.TargetPageKey));
        Assert.Null(issue.FixAsync);
    }

    [Fact]
    public void LongUnusedAndBig_IsOneTip()
    {
        var old = Now.AddDays(-200);
        var apps = new[]
        {
            App("Big", old), App("Bigger", old), App("Small", old), App("Recent", Now.AddDays(-10)), App("Never"),
            App("Chess", old, location: AppLocation.Apple),
        };
        var sizes = new Dictionary<string, long>
        {
            [apps[0].Path] = 600 * Mb, [apps[1].Path] = 2048 * Mb, [apps[2].Path] = 100 * Mb, [apps[3].Path] = 900 * Mb, [apps[5].Path] = 900 * Mb,
        };
        Assert.Equal(new[] { true, true, true, false, false, false }, apps.Select(a => ProgramsRules.IsLongUnused(a, Now)));
        var tip = Assert.Single(ProgramsRules.Evaluate(apps, sizes, Now));
        Assert.Equal((IssueSeverity.Info, "Давно не запускались: 2 программы (2,59 ГБ)"), (tip.Severity, tip.Title));
        Assert.StartsWith("Не открывались больше полугода и занимают много места: Bigger — 2 ГБ, Big — 600 МБ.", tip.Details, StringComparison.Ordinal);
        Assert.Equal(2648 * Mb, tip.ReclaimableBytes);
    }

    [Fact]
    public void IntelOnly_IsOneTip()
    {
        var tip = Assert.Single(ProgramsRules.Evaluate(
            new[] { App("Old Game", arch: AppArchitecture.IntelOnly), App("Scanner", arch: AppArchitecture.IntelOnly), App("Universal", arch: AppArchitecture.Universal) },
            new Dictionary<string, long>(), Now));
        Assert.Equal("Только для Intel: 2 программы", tip.Title);
        Assert.StartsWith("Old Game, Scanner — работают через Rosetta", tip.Details, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Contributor_MeasuresOnlyLongUnusedApps()
    {
        using var apps = new FakeApps();
        var root = apps.Folders.Applications;
        var old = apps.App(root, "Old.app", FakeApps.Plist("com.example.old", "Old", "Old"), MachOTests.Thin(MachOTests.X86_64));
        var recent = apps.App(root, "Recent.app", FakeApps.Plist("com.example.recent", "Recent", "Recent"));
        var fake = new FakeCommandRunner().On("/usr/bin/mdls", new[] { "-name", "kMDItemLastUsedDate", "-raw", old, recent },
            new CommandResult(0, "2025-01-01 10:00:00 +0000\0" + "2026-10-17 10:00:00 +0000", ""));
        var signatures = new CodeSignatureService(fake);
        var programs = new ProgramsService(new AppCatalog(apps.Folders, fake, signatures), new NoTrash(),
            new PcCleaner.Modules.Processes.ProcessesService(fake, signatures, 999, 501, executablePath: _ => null), apps.Home, () => FdaState.Denied);
        var issues = await new ProgramsContributor(programs, () => Now).AnalyzeAsync(CancellationToken.None);
        // Old is small (a few bytes), so only the Intel tip remains.
        Assert.Equal(new[] { "Только для Intel: 1 программа" }, issues.Select(i => i.Title));
        Assert.All(issues, i => Assert.Equal(ProgramsRules.ModuleName, i.Module));
    }

    private sealed class NoTrash : ITrash
    {
        public Task<TrashOutcome> MoveAsync(string path, CancellationToken ct) => throw new InvalidOperationException("the check must not change anything");
    }
}
