using PcCleaner.Core;
using PcCleaner.Mac.Tests.Support;
using PcCleaner.Modules.Programs;
using Xunit;

namespace PcCleaner.Mac.Tests.Programs;

/// <summary>Fake app bundles in a temporary folder: Info.plist, a main executable with a real Mach-O header.</summary>
internal sealed class FakeApps : IDisposable
{
    private readonly TempDir _dir = new();

    public FakeApps()
    {
        Folders = new AppFolders(Dir("Applications"), Dir("home/Applications"), new[] { Dir("System/Applications") });
        Home = Dir("home");
    }

    public AppFolders Folders { get; }

    public string Home { get; }

    public static string Plist(string id, string name, string executable, string version = "1.0")
        => $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <plist version="1.0"><dict>
            <key>CFBundleIdentifier</key><string>{id}</string>
            <key>CFBundleName</key><string>{name}</string>
            <key>CFBundleExecutable</key><string>{executable}</string>
            <key>CFBundleShortVersionString</key><string>{version}</string>
            </dict></plist>
            """;

    /// <summary>Creates root/relative (e.g. "Vendor/Tool.app") with the plist and the executable it names.</summary>
    public string App(string root, string relative, string? plist, byte[]? executable = null, bool appStore = false)
    {
        var bundle = Path.Combine(root, relative);
        Directory.CreateDirectory(Path.Combine(bundle, "Contents", "MacOS"));
        if (plist is not null)
        {
            File.WriteAllText(Path.Combine(bundle, "Contents", "Info.plist"), plist);
            var exe = PcCleaner.Core.Plist.ParseDict(plist).GetString("CFBundleExecutable");
            if (exe is not null)
                File.WriteAllBytes(Path.Combine(bundle, "Contents", "MacOS", exe), executable ?? MachOTests.Thin(MachOTests.Arm64));
        }
        if (appStore)
            Directory.CreateDirectory(Path.Combine(bundle, "Contents", "_MASReceipt"));
        return bundle;
    }

    public void Dispose() => _dir.Dispose();

    private string Dir(string relative) => Directory.CreateDirectory(Path.Combine(_dir.Path, relative)).FullName;
}

public class AppCatalogTests
{
    private static AppCatalog Catalog(FakeApps apps, FakeCommandRunner? fake = null)
    {
        fake ??= new FakeCommandRunner();
        return new AppCatalog(apps.Folders, fake, new CodeSignatureService(fake));
    }

    [Fact]
    public async Task List_ReadsBundles_InVendorFolders_AndNotApple()
    {
        using var apps = new FakeApps();
        var root = apps.Folders.Applications;
        apps.App(root, "Google Chrome.app", Fixture.Text("apps/chrome.plist"), MachOTests.Fat(false, MachOTests.X86_64, MachOTests.Arm64));
        apps.App(root, "Firefox.app", Fixture.Text("apps/firefox.plist"), MachOTests.Thin(MachOTests.X86_64));
        apps.App(root, "PowerShell.app", Fixture.Text("apps/powershell.plist"), "#!/bin/sh\n"u8.ToArray());
        apps.App(root, "Python 3.14/Python Launcher.app", Fixture.Text("apps/python-launcher.plist"));
        apps.App(root, "Store.app", FakeApps.Plist("com.example.store", "Store", "Store"), appStore: true);
        apps.App(root, "MacKeeper.app", FakeApps.Plist("com.mackeeper.MacKeeper", "MacKeeper", "MacKeeper"));
        apps.App(root, "Broken.app", null);
        apps.App(apps.Folders.UserApplications, "Mine.app", FakeApps.Plist("com.example.mine", "Mine", "Mine"));
        apps.App(apps.Folders.Apple[0], "Calculator.app", FakeApps.Plist("com.apple.calculator", "Calculator", "Calculator"));

        var list = await Catalog(apps).ListAsync(includeApple: false, CancellationToken.None);
        Assert.Equal(new[] { "Broken", "Firefox", "Google Chrome", "MacKeeper", "PowerShell", "Python Launcher", "Store", "Mine" }, list.Apps.Select(a => a.Name));
        var byName = list.Apps.ToDictionary(a => a.Name);

        var chrome = byName["Google Chrome"];
        Assert.Equal(("com.google.Chrome", "Chrome", "155.0.8059.40", AppArchitecture.Universal, AppLocation.Applications),
            (chrome.BundleId, chrome.BundleName, chrome.Version, chrome.Architecture, chrome.Location));
        Assert.Equal(AppArchitecture.IntelOnly, byName["Firefox"].Architecture);
        Assert.Equal(AppArchitecture.Unknown, byName["PowerShell"].Architecture);
        Assert.Equal(("3.14.8", AppArchitecture.AppleSilicon), (byName["Python Launcher"].Version, byName["Python Launcher"].Architecture));
        Assert.True(byName["Store"].IsAppStore);
        Assert.Equal("App Store", byName["Store"].SourceText);
        Assert.StartsWith("MacKeeper", byName["MacKeeper"].PupReason, StringComparison.Ordinal);
        Assert.Equal(("Info.plist не читается", (string?)null), (byName["Broken"].Problem, byName["Broken"].BundleId));
        Assert.Equal(AppLocation.UserApplications, byName["Mine"].Location);
        Assert.All(list.Apps, a => Assert.False(a.IsProtected));

        var withApple = await Catalog(apps).ListAsync(includeApple: true, CancellationToken.None);
        var calculator = withApple.Apps.Single(a => a.Name == "Calculator");
        Assert.True(calculator.IsApple && calculator.IsProtected && calculator.PupReason is null);
    }

    [Fact]
    public async Task LastUsed_OneCallForAllApps()
    {
        using var apps = new FakeApps();
        var a = apps.App(apps.Folders.Applications, "A.app", FakeApps.Plist("com.example.a", "A", "A"));
        var b = apps.App(apps.Folders.Applications, "B.app", FakeApps.Plist("com.example.b", "B", "B"));
        var fake = new FakeCommandRunner().On("/usr/bin/mdls", new[] { "-name", "kMDItemLastUsedDate", "-raw", a, b },
            new CommandResult(0, Fixture.Text("apps/mdls-xcode.txt") + "\0" + Fixture.Text("apps/mdls-never.txt") + "\0", ""));
        var list = await Catalog(apps, fake).ListAsync(includeApple: false, CancellationToken.None);
        Assert.Empty(list.Problems);
        Assert.Equal(new DateTime(2026, 10, 13, 5, 41, 25, DateTimeKind.Utc), list.Apps[0].LastUsedUtc);
        Assert.Null(list.Apps[1].LastUsedUtc);
    }

    [Fact]
    public async Task LastUsed_Unknown_IsAProblemNotAFailure()
    {
        using var apps = new FakeApps();
        apps.App(apps.Folders.Applications, "A.app", FakeApps.Plist("com.example.a", "A", "A"));
        var list = await Catalog(apps).ListAsync(includeApple: false, CancellationToken.None);
        Assert.Single(list.Apps);
        Assert.Single(list.Problems);
    }

    [Fact]
    public void Uninstaller_InsideOrNextToTheApp()
    {
        using var apps = new FakeApps();
        var root = apps.Folders.Applications;
        var tool = apps.App(root, "Vendor/Tool.app", FakeApps.Plist("com.vendor.tool", "Tool", "Tool"));
        apps.App(root, "Vendor/Uninstall Vendor Tools.app", FakeApps.Plist("com.vendor.uninstall", "Uninstall", "Uninstall"));
        Assert.EndsWith("Uninstall Vendor Tools.app", AppCatalog.FindUninstaller(tool, root));

        var app = apps.App(root, "App.app", FakeApps.Plist("com.example.app", "App", "App"));
        apps.App(root, "Uninstall Other.app", null);
        Assert.Null(AppCatalog.FindUninstaller(app, root));
        apps.App(root, "App Uninstaller.app", null);
        Assert.EndsWith("App Uninstaller.app", AppCatalog.FindUninstaller(app, root));

        var inner = apps.App(root, "Inner.app", FakeApps.Plist("com.example.inner", "Inner", "Inner"));
        Directory.CreateDirectory(Path.Combine(inner, "Contents", "Resources", "Inner Uninstaller.app"));
        Assert.EndsWith(Path.Combine("Resources", "Inner Uninstaller.app"), AppCatalog.FindUninstaller(inner, root));
    }

    [Fact]
    public async Task Measure_CountsTheBundle()
    {
        using var apps = new FakeApps();
        var app = apps.App(apps.Folders.Applications, "A.app", FakeApps.Plist("com.example.a", "A", "A"));
        File.WriteAllBytes(Path.Combine(app, "Contents", "Resources.bin"), new byte[10_000]);
        Assert.True(await AppCatalog.MeasureAsync(app, CancellationToken.None) >= 10_000);
    }

    [Theory]
    [InlineData("(null)", 1)]
    [InlineData("2026-10-13 05:41:25 +0000", 1)]
    [InlineData("2026-10-13 05:41:25 +0000\0(null)\0", 2)]
    [InlineData("2026-10-13 05:41:25 +0000\0(null)", 2)]
    public void Mdls_Values(string raw, int count) => Assert.Equal(count, AppParsers.LastUsed(raw, count)!.Count);

    [Fact]
    public void Mdls_WrongCountOrDates()
    {
        Assert.Null(AppParsers.LastUsed("(null)\0(null)\0(null)", 2));
        Assert.Equal(new DateTime(2026, 10, 13, 2, 41, 25, DateTimeKind.Utc), AppParsers.ParseDate("2026-10-13 05:41:25 +0300"));
        Assert.Null(AppParsers.ParseDate("(null)"));
        Assert.Null(AppParsers.ParseDate("13.10.2026 05:41"));
    }

    [MacFact]
    public async Task RealMac_ListsTheApps_WithDates()
    {
        var runner = new ProcessCommandRunner();
        var list = await new AppCatalog(AppFolders.For(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)), runner, new CodeSignatureService(runner))
            .ListAsync(includeApple: true, CancellationToken.None);
        Assert.Empty(list.Problems);
        Assert.True(list.Apps.Count > 20);
        var calculator = list.Apps.Single(a => a.BundleId == "com.apple.calculator");
        Assert.Equal(AppLocation.Apple, calculator.Location);
        Assert.NotEqual(AppArchitecture.Unknown, calculator.Architecture);
        Assert.DoesNotContain(list.Apps, a => a.Name == "Safari" && a.Location != AppLocation.Apple);
    }
}
