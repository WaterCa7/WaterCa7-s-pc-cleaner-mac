using PcCleaner.Core;
using PcCleaner.Mac.Tests.Cleaner;
using PcCleaner.Mac.Tests.Startup;
using PcCleaner.Mac.Tests.Support;
using PcCleaner.Modules.Processes;
using PcCleaner.Modules.Startup;
using Xunit;

namespace PcCleaner.Mac.Tests.Processes;

public class ProcessesContributorTests
{
    private const string Home = "/Users/anna";
    private static readonly Hint Strong = new("запущена из временной папки", true);
    private static readonly Hint Weak = new("без подписи разработчика", false);

    private static ProcessEntry Process(int pid, string command, params Hint[] hints)
        => new(new ProcessInfo(pid, 1, 501, 0, 0, TimeSpan.Zero, command, null), false, null, null, hints);

    private static LaunchEntry Entry(string label, LaunchSource source = LaunchSource.UserAgent, string? target = null, SignatureInfo? signature = null,
        string? problem = null, bool disabled = false, bool hasLabel = true, Hint[]? hints = null)
        => new(source, $"/Users/anna/Library/LaunchAgents/{label}.plist", label, hasLabel, target, target is null ? Array.Empty<string>() : new[] { target },
            target, true, false, LaunchState.NotLoaded, disabled, problem, signature, hints ?? Array.Empty<Hint>());

    private static readonly List<string> Disabled = new();

    private static IReadOnlyList<HealthIssue> Evaluate(IReadOnlyList<ProcessEntry> processes, params LaunchEntry[] startup)
        => ProcessesRules.Evaluate(processes, startup, Home, (entry, _) =>
        {
            lock (Disabled)
                Disabled.Add(entry.Label);
            return Task.FromResult($"«{entry.Label}» отключена.");
        });

    [Fact]
    public void Processes_OnlyStrongSigns_OneFindingPerProgram()
    {
        var issues = Evaluate(new[]
        {
            Process(10, "/private/tmp/x/agent", Strong),
            Process(11, "/private/tmp/x/agent", Strong),
            Process(12, "/opt/homebrew/bin/redis-server", Weak),
            Process(13, "/Applications/Telegram.app/Contents/MacOS/Telegram"),
        });
        var issue = Assert.Single(issues);
        Assert.Equal((IssueSeverity.High, "Подозрительный процесс «agent»", (object?)null), (issue.Severity, issue.Title, issue.FixAsync));
        Assert.StartsWith("Запущена из временной папки. Путь: /private/tmp/x/agent.", issue.Details, StringComparison.Ordinal);
        Assert.Equal("Processes", issue.TargetPageKey);
    }

    [Fact]
    public async Task StrongStartupEntry_IsImportant_AndTheFixDisablesIt()
    {
        var issue = Assert.Single(Evaluate(Array.Empty<ProcessEntry>(), Entry("com.example.strong", target: "/Users/Shared/a", hints: new[] { Strong })));
        Assert.Equal((IssueSeverity.High, false), (issue.Severity, issue.AutoFixSafe));
        Assert.Equal("«com.example.strong» отключена.", await issue.FixAsync!(CancellationToken.None));
        Assert.Contains("com.example.strong", Disabled);
    }

    [Fact]
    public void BrokenEntries_AreMedium_AndSafeToFix()
    {
        var issues = Evaluate(Array.Empty<ProcessEntry>(),
            Entry("com.example.gone", problem: "программа не найдена: /Users/anna/gone"),
            Entry("com.example.global", LaunchSource.GlobalAgent, problem: "программа не найдена: /Library/x"),
            Entry("nolabel", problem: "в plist нет метки (Label)", hasLabel: false),
            Entry("com.example.daemon", LaunchSource.Daemon, problem: "программа не найдена: /Library/y"),
            Entry("com.example.off", problem: "программа не найдена: /z", disabled: true));
        Assert.Equal(new[] { "Сломанная запись автозагрузки «com.example.gone»", "Сломанная запись автозагрузки «com.example.global»",
            "Сломанная запись автозагрузки «nolabel»" }, issues.Select(i => i.Title));
        Assert.All(issues, i => Assert.Equal(IssueSeverity.Medium, i.Severity));
        Assert.Equal(new[] { true, true, false }, issues.Select(i => i.AutoFixSafe));
        Assert.Null(issues[2].FixAsync);
    }

    [Fact]
    public void UnsignedProgramInTheHomeFolder_IsMedium()
    {
        var issues = Evaluate(Array.Empty<ProcessEntry>(),
            Entry("com.example.mine", target: "/Users/anna/bin/sync", signature: new SignatureInfo(SignatureKind.AdHoc), hints: new[] { Weak }),
            Entry("homebrew.mxcl.redis", target: "/opt/homebrew/opt/redis/bin/redis-server", signature: new SignatureInfo(SignatureKind.Unsigned), hints: new[] { Weak }),
            Entry("com.example.signed", target: "/Users/anna/Applications/Sync.app/Contents/MacOS/Sync", signature: new SignatureInfo(SignatureKind.DeveloperId)));
        var issue = Assert.Single(issues);
        Assert.Equal((IssueSeverity.Medium, "Запись автозагрузки без подписи разработчика: «com.example.mine»"), (issue.Severity, issue.Title));
    }

    [Fact]
    public void ManyThirdPartyEntries_IsATip()
    {
        var many = Enumerable.Range(1, 16).Select(i => Entry($"com.example.{i}")).ToList();
        many.Add(Entry("com.apple.x", LaunchSource.Apple));
        var tip = Assert.Single(Evaluate(Array.Empty<ProcessEntry>(), many.ToArray()));
        Assert.Equal((IssueSeverity.Info, "В автозагрузке 16 сторонних записей"), (tip.Severity, tip.Title));
        Assert.Empty(Evaluate(Array.Empty<ProcessEntry>(), many.Skip(1).ToArray()));
    }

    [Fact]
    public async Task Contributor_UsesTheRealServices()
    {
        using var home = new StartupHome();
        var fake = new FakeCommandRunner()
            .OnFixture("/bin/ps", new[] { "-axww", "-o", "pid=,ppid=,uid=,pcpu=,rss=,etime=,comm=" }, "ps/mixed-comm.txt")
            .OnFixture("/bin/ps", new[] { "-axww", "-o", "pid=,args=" }, "ps/mixed-args.txt")
            .OnFixture("/bin/launchctl", new[] { "list" }, "launchd/list.txt");
        var signatures = new CodeSignatureService(fake, _ => new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc));
        var missing = (string path) => path.Contains("/missing/", StringComparison.Ordinal) || path.EndsWith("/gone", StringComparison.Ordinal);
        var contributor = new ProcessesContributor(
            new ProcessesService(fake, signatures, 999, 501, missing, executablePath: _ => null),
            new StartupService(home.Folders, 501, fake, signatures, home.Backups, new AdminPrompt(fake), new CleanerViewModelTests.FakeTrash(), missing),
            Home);
        var issues = await contributor.AnalyzeAsync(CancellationToken.None);
        Assert.Contains(issues, i => i.Title == "Подозрительный процесс «MacKeeper»");
        Assert.Contains(issues, i => i.Title == "Сломанная запись автозагрузки «com.example.broken»" && i.AutoFixSafe);
        Assert.Contains(issues, i => i.Title == "Подозрительная запись автозагрузки «com.example.script»");
        Assert.All(issues, i => Assert.Equal(ProcessesRules.ModuleName, i.Module));
        Assert.DoesNotContain(fake.Calls, c => c.Contains(" disable ", StringComparison.Ordinal) || c.StartsWith("/bin/kill", StringComparison.Ordinal));
        Assert.Empty(home.Backups.List());
    }
}
