using PcCleaner.Core;
using PcCleaner.Mac.Tests.Support;
using PcCleaner.Modules.Programs;
using Xunit;

namespace PcCleaner.Mac.Tests.Programs;

public class LeftoverFinderTests
{
    private const string Team = "ABCDE12345";

    internal static InstalledApp App(string path, string name, string? id, string? bundleName = null, string? team = Team)
        => new(path, name, bundleName ?? name, id, "1.0", null, AppLocation.Applications, false, AppArchitecture.AppleSilicon, null, null, null, null)
        {
            Signature = team is null ? null : new SignatureInfo(SignatureKind.DeveloperId, $"Developer ID Application: Vendor ({team})", team),
        };

    /// <summary>A home folder where com.vendor.writer left everything it could, next to a sibling app's data.</summary>
    internal static string FillHome(TempDir dir)
    {
        var home = Path.Combine(dir.Path, "home");
        string L(string relative, string content = "x") => dir.File(Path.Combine("home", "Library", relative), content);
        L("Application Support/com.vendor.writer/db.sqlite", new string('x', 5000));
        L("Caches/com.vendor.writer/Cache.db");
        L("Preferences/com.vendor.writer.plist");
        L("Preferences/ByHost/com.vendor.writer.0000-1111.plist");
        L("Saved Application State/com.vendor.writer.savedState/windows.plist");
        L("HTTPStorages/com.vendor.writer/httpstorages.sqlite");
        L("WebKit/com.vendor.writer/WebsiteData/x");
        L("Cookies/com.vendor.writer.binarycookies");
        L("LaunchAgents/com.vendor.writer.updater.plist");
        L("LaunchAgents/com.vendor.writer.pro.agent.plist");
        L("Containers/com.vendor.writer/Data/x");
        L("Group Containers/ABCDE12345.com.vendor.shared/x");
        L("Application Support/Writer/old.db");
        L("Logs/Writer/writer.log");
        L("Application Support/Notes/notes.db");
        L("Caches/com.vendor.reader/x");
        L("Preferences/com.vendor.writerx.plist");
        return home;
    }

    [Fact]
    public void ById_AndByName_WithFullDiskAccess()
    {
        using var dir = new TempDir();
        var home = FillHome(dir);
        var writer = App("/Applications/Writer.app", "Writer", "com.vendor.writer");
        var all = new[] { writer, App("/Applications/Reader.app", "Reader", "com.vendor.reader"), App("/Applications/Writer Pro.app", "Writer Pro", "com.vendor.writer.pro") };
        var scan = LeftoverFinder.Find(writer, home, all, fullDiskAccess: true);
        var relative = scan.Items.ToDictionary(i => Path.GetRelativePath(Path.Combine(home, "Library"), i.Path).Replace('\\', '/'));

        Assert.Equal(new[]
            {
                "Application Support/com.vendor.writer", "Caches/com.vendor.writer", "Preferences/com.vendor.writer.plist",
                "Preferences/ByHost/com.vendor.writer.0000-1111.plist", "Saved Application State/com.vendor.writer.savedState",
                "HTTPStorages/com.vendor.writer", "WebKit/com.vendor.writer", "Cookies/com.vendor.writer.binarycookies",
                "LaunchAgents/com.vendor.writer.updater.plist", "Containers/com.vendor.writer", "Application Support/Writer", "Logs/Writer",
                "Group Containers/ABCDE12345.com.vendor.shared",
            },
            relative.Keys);
        Assert.All(relative.Where(p => !p.Key.StartsWith("Group Containers", StringComparison.Ordinal)), p => Assert.True(p.Value.Checked, p.Key));
        Assert.False(relative["Group Containers/ABCDE12345.com.vendor.shared"].Checked);
        Assert.True(relative["Application Support/com.vendor.writer"].Bytes >= 5000);
        Assert.False(scan.NeedsFullDiskAccess);
        Assert.True(scan.CheckedBytes >= 5000);
    }

    [Fact]
    public void WithoutFullDiskAccess_ContainersAreNotLookedAt()
    {
        using var dir = new TempDir();
        var home = FillHome(dir);
        var writer = App("/Applications/Writer.app", "Writer", "com.vendor.writer");
        var scan = LeftoverFinder.Find(writer, home, new[] { writer }, fullDiskAccess: false);
        Assert.True(scan.NeedsFullDiskAccess);
        Assert.DoesNotContain(scan.Items, i => i.Path.Contains("Containers", StringComparison.Ordinal));
    }

    [Fact]
    public void NameSharedByAnotherApp_IsNotTicked()
    {
        using var dir = new TempDir();
        var home = FillHome(dir);
        var writer = App("/Applications/Writer.app", "Writer", "com.vendor.writer");
        var other = App("/Applications/Old/Writer.app", "Writer", "com.other.writer");
        var byName = LeftoverFinder.Find(writer, home, new[] { writer, other }, fullDiskAccess: false).Items
            .Where(i => i.Note is not null).ToList();
        Assert.Equal(2, byName.Count);
        Assert.All(byName, i => Assert.False(i.Checked));
    }

    [Fact]
    public void ProtectedApps_AndOddIds_FindNothing()
    {
        using var dir = new TempDir();
        var home = FillHome(dir);
        var apple = App("/System/Applications/Notes.app", "Notes", "com.apple.Notes") with { Location = AppLocation.Apple };
        Assert.Empty(LeftoverFinder.Find(apple, home, new[] { apple }, true).Items);
        var odd = App("/Applications/X.app", "..", "../../Documents", team: null);
        Assert.Empty(LeftoverFinder.Find(odd, home, new[] { odd }, true).Items);
    }

    [MacFact]
    public void Links_AreNotTaken()
    {
        using var dir = new TempDir();
        var home = FillHome(dir);
        var outside = dir.File("outside/secret.txt", "secret");
        Directory.CreateSymbolicLink(Path.Combine(home, "Library", "Caches", "com.vendor.linked"), Path.GetDirectoryName(outside)!);
        File.CreateSymbolicLink(Path.Combine(home, "Library", "Preferences", "com.vendor.linked.plist"), outside);
        var linked = App("/Applications/Linked.app", "Linked", "com.vendor.linked");
        Assert.Empty(LeftoverFinder.Find(linked, home, new[] { linked }, true).Items);
    }
}
