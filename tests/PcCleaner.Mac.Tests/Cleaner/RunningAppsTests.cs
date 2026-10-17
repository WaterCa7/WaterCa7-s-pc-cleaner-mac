using PcCleaner.Core;
using PcCleaner.Mac.Tests.Support;
using PcCleaner.Modules.Cleaner;
using Xunit;

namespace PcCleaner.Mac.Tests.Cleaner;

public class RunningAppsTests
{
    private const string Ps = """
        /sbin/launchd
        /System/Library/CoreServices/Finder.app/Contents/MacOS/Finder
        /Applications/Google Chrome.app/Contents/MacOS/Google Chrome
        /Applications/Google Chrome.app/Contents/Frameworks/Google Chrome Framework.framework/Versions/130.0/Helpers/Google Chrome Helper (Renderer).app/Contents/MacOS/Google Chrome Helper (Renderer)
        /Applications/Safari.app/Contents/MacOS/Safari
        /usr/libexec/trustd
        /Users/testuser/Applications/Мой редактор.app/Contents/MacOS/editor
        (zombie)
        """;

    [Fact]
    public void Bundles_AreTheOutermostApps()
        => Assert.Equal(new[]
            {
                "/System/Library/CoreServices/Finder.app",
                "/Applications/Google Chrome.app",
                "/Applications/Safari.app",
                "/Users/testuser/Applications/Мой редактор.app",
            },
            RunningAppsService.BundlesFromPs(Ps));

    [Fact]
    public void Bundles_EmptyOutput() => Assert.Empty(RunningAppsService.BundlesFromPs(""));

    [Fact]
    public async Task Load_ReadsIdsAndNames()
    {
        using var apps = new TempDir();
        var chrome = Bundle(apps, "Google Chrome", "com.google.Chrome", "Google Chrome", "Google Chrome");
        var editor = Bundle(apps, "Мой редактор", "ru.example.editor", "Editor", "editor");
        var fake = new FakeCommandRunner().On("/bin/ps", new[] { "-xww", "-o", "comm=" },
            new CommandResult(0, $"{chrome}/Contents/MacOS/Google Chrome\n{editor}/Contents/MacOS/editor\n", ""));
        var set = await new RunningAppsService(fake).LoadAsync(CancellationToken.None);
        Assert.True(set.Matches("com.google.chrome"));
        Assert.True(set.Matches("Google Chrome"));
        Assert.True(set.Matches("ru.example.editor"));
        Assert.True(set.Matches("Мой редактор"));
        Assert.True(set.Matches("editor"));
        Assert.False(set.Matches("com.spotify.client"));
        Assert.True(set.IsRunning("Safari", "Google Chrome"));
    }

    [Fact]
    public async Task Load_PsFails_NothingRuns()
    {
        var set = await new RunningAppsService(new FakeCommandRunner()).LoadAsync(CancellationToken.None);
        Assert.Empty(set.Apps);
        Assert.Contains(Log.Snapshot(), e => e.Message.StartsWith("Не удалось узнать, какие программы запущены"));
    }

    [MacFact]
    public async Task RealMac_FinderRuns()
    {
        var set = await new RunningAppsService(new ProcessCommandRunner()).LoadAsync(CancellationToken.None);
        Assert.True(set.IsRunning("com.apple.finder", "Finder"));
    }

    private static string Bundle(TempDir dir, string folder, string id, string name, string executable)
    {
        var bundle = Path.Combine(dir.Path, folder + ".app");
        dir.File($"{folder}.app/Contents/Info.plist", $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <plist version="1.0"><dict>
              <key>CFBundleIdentifier</key><string>{id}</string>
              <key>CFBundleName</key><string>{name}</string>
              <key>CFBundleExecutable</key><string>{executable}</string>
            </dict></plist>
            """);
        return bundle;
    }
}
