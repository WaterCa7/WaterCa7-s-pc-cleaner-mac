using PcCleaner.Core;
using PcCleaner.Modules.Processes;
using Xunit;

namespace PcCleaner.Mac.Tests.Processes;

public class ProcessRulesTests
{
    private static readonly SignatureInfo DeveloperId = new(SignatureKind.DeveloperId, "Developer ID Application: Example (ABCDE12345)", "ABCDE12345");
    private static readonly SignatureInfo AdHoc = new(SignatureKind.AdHoc);
    private static readonly SignatureInfo Unsigned = new(SignatureKind.Unsigned);

    [Theory]
    [InlineData("/usr/libexec/logd", true)]
    [InlineData("/System/Library/CoreServices/Finder.app/Contents/MacOS/Finder", true)]
    [InlineData("/Library/Apple/System/Library/CoreServices/XProtect.app/Contents/MacOS/XProtect", true)]
    [InlineData("/usr/local/bin/helper", false)]
    [InlineData("/opt/homebrew/bin/redis-server", false)]
    [InlineData("/Applications/Safari.app/Contents/MacOS/Safari", false)]
    [InlineData("/Library/PrivilegedHelperTools/com.example.helper", false)]
    public void SystemPaths(string path, bool system) => Assert.Equal(system, ProcessRules.IsSystemPath(path));

    [Fact]
    public void Protected_Processes()
    {
        static ProcessInfo P(int pid, int uid, string command) => new(pid, 1, uid, 0, 0, TimeSpan.Zero, command, null);
        Assert.NotNull(ProcessRules.WhyProtected(P(1, 0, "/sbin/launchd"), 999, 501));
        Assert.NotNull(ProcessRules.WhyProtected(P(160, 501, "/System/Library/PrivateFrameworks/SkyLight.framework/Resources/WindowServer"), 999, 501));
        Assert.NotNull(ProcessRules.WhyProtected(P(170, 501, "/System/Library/CoreServices/loginwindow.app/Contents/MacOS/loginwindow"), 999, 501));
        Assert.Equal("это сама программа", ProcessRules.WhyProtected(P(999, 501, "/Applications/WaterCa7s PC Cleaner.app/Contents/MacOS/WaterCa7sPcCleaner"), 999, 501));
        Assert.Equal("системный процесс (root)", ProcessRules.WhyProtected(P(300, 0, "/Library/PrivilegedHelperTools/x"), 999, 501));
        Assert.Equal("процесс другого пользователя", ProcessRules.WhyProtected(P(301, 502, "/Applications/Telegram.app/Contents/MacOS/Telegram"), 999, 501));
        Assert.Null(ProcessRules.WhyProtected(P(302, 501, "/Applications/Telegram.app/Contents/MacOS/Telegram"), 999, 501));
    }

    [Fact]
    public void TempFolder_IsStrong_UnlessAKnownDeveloperSignedIt()
    {
        Assert.Contains(ProcessRules.Hints("/private/tmp/updater", Unsigned), h => h is { Strong: true, Text: "запущена из временной папки" });
        Assert.Contains(ProcessRules.Hints("/private/var/folders/zz/abc/T/x/agent", null), h => h.Strong);
        Assert.Contains(ProcessRules.Hints("/private/tmp/updater", DeveloperId), h => h is { Strong: false, Text: "запущена из временной папки" });
    }

    [Fact]
    public void AppOpenedFromDownloads_IsOnlyAdvice()
    {
        var hints = ProcessRules.Hints("/private/var/folders/zz/abc/T/AppTranslocation/1234/d/Some App.app/Contents/MacOS/Some App", AdHoc);
        Assert.All(hints, h => Assert.False(h.Strong));
        Assert.Contains(hints, h => h.Text.Contains("«Загрузок»", StringComparison.Ordinal));
        Assert.DoesNotContain(hints, h => h.Text.Contains("временной папки", StringComparison.Ordinal));
    }

    [Fact]
    public void SharedAndHiddenFolders()
    {
        Assert.Contains(ProcessRules.Hints("/Users/Shared/Agent/agent", Unsigned), h => h is { Strong: true, Text: "запущена из общей папки /Users/Shared" });
        Assert.Contains(ProcessRules.Hints("/Users/anna/Library/.hidden/agent", Unsigned), h => h is { Strong: true, Text: "лежит в скрытой папке «.hidden»" });
        Assert.Contains(ProcessRules.Hints("/Users/anna/.config/sync/agent", DeveloperId), h => h is { Strong: false, Text: "лежит в скрытой папке «.config»" });
        Assert.Contains(ProcessRules.Hints("/Users/anna/.Trash/Old.app/Contents/MacOS/Old", DeveloperId), h => h is { Strong: false, Text: "лежит в Корзине" });
        // Developer tools in their own folders: only the missing signature is mentioned, and it is not strong.
        var tool = Assert.Single(ProcessRules.Hints("/Users/anna/.cargo/bin/rust-analyzer", AdHoc));
        Assert.Equal(new Hint("без подписи разработчика", false), tool);
        Assert.Empty(ProcessRules.Hints("/Users/anna/.dotnet/sdk/10.0.402/Roslyn/bincore/VBCSCompiler", DeveloperId));
    }

    [Fact]
    public void Signatures()
    {
        Assert.Contains(ProcessRules.Hints("/Applications/Broken.app/Contents/MacOS/Broken", new SignatureInfo(SignatureKind.Invalid)), h => h.Strong);
        Assert.Empty(ProcessRules.Hints("/Applications/Tool.app/Contents/MacOS/Tool", Unsigned));
        Assert.Equal(new[] { new Hint("без подписи разработчика", false) }, ProcessRules.Hints("/opt/homebrew/Cellar/redis/7.2/bin/redis-server", AdHoc));
        Assert.Empty(ProcessRules.Hints("/usr/libexec/odd", Unsigned));
        Assert.Contains(ProcessRules.Hints("/Users/anna/Library/Application Support/Sync/agent", DeveloperId), h => h is { Strong: false } && h.Text.Contains("Application Support", StringComparison.Ordinal));
        Assert.Empty(ProcessRules.Hints("/Users/anna/Library/Application Support/Sync/Sync Helper.app/Contents/MacOS/Sync Helper", DeveloperId));
        Assert.Empty(ProcessRules.Hints("dotnet", null));
    }

    [Fact]
    public void KnownUnwantedPrograms_AreStrong_ByPathOrName()
    {
        var byPath = Assert.Single(ProcessRules.Hints("/Applications/MacKeeper.app/Contents/MacOS/MacKeeper", DeveloperId));
        Assert.True(byPath.Strong);
        Assert.StartsWith("MacKeeper", byPath.Text, StringComparison.Ordinal);
        Assert.Contains(ProcessRules.Hints("/Library/Application Support/x/helper", DeveloperId, "com.searchmarquis.agent"), h => h.Strong);
    }

    [Fact]
    public void ImpersonatingApple()
    {
        Assert.True(ProcessRules.ImpersonatesApple("com.apple.updater", "/Users/anna/Library/Application Support/u/updater", AdHoc));
        Assert.False(ProcessRules.ImpersonatesApple("com.apple.updater", "/Users/anna/Library/u/updater", new SignatureInfo(SignatureKind.Apple)));
        Assert.False(ProcessRules.ImpersonatesApple("com.apple.Finder", "/System/Library/CoreServices/Finder.app/Contents/MacOS/Finder", AdHoc));
        Assert.False(ProcessRules.ImpersonatesApple("com.apple.updater", "/Users/anna/u/updater", SignatureInfo.Unknown));
        Assert.False(ProcessRules.ImpersonatesApple("com.example.updater", "/Users/anna/u/updater", AdHoc));
    }
}
