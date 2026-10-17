using PcCleaner.Core;
using PcCleaner.Mac.Tests.Support;
using Xunit;

namespace PcCleaner.Mac.Tests.Core;

public class BackupStoreTests
{
    [Fact]
    public void Plist_IsCopiedAndListed()
    {
        using var dir = new TempDir();
        var plist = dir.File("LaunchAgents/com.example.helper.plist", Fixture.Text("plist/launch-agent.plist"));
        var store = new BackupStore(Path.Combine(dir.Path, "Backups"));
        var record = store.SavePlist(plist, "com.example.helper", new Dictionary<string, string> { ["label"] = "com.example.helper" });
        Assert.Equal(File.ReadAllBytes(plist), File.ReadAllBytes(record.StoredFile!));
        var listed = Assert.Single(new BackupStore(store.Dir).List());
        Assert.Equal((BackupKind.LaunchdPlist, plist, "com.example.helper"), (listed.Kind, listed.OriginalPath, listed.Details["label"]));
    }

    [Fact]
    public void LoginItems_AndRemoval()
    {
        using var dir = new TempDir();
        var store = new BackupStore(dir.Path);
        var a = store.SaveLoginItem("Telegram", "/Applications/Telegram.app", hidden: false);
        store.SaveLoginItem("Музыка", "/Applications/Музыка.app", hidden: true);
        Assert.Equal(2, store.List().Count);
        Assert.Equal("true", store.List().Single(r => r.Title.Contains("Музыка")).Details["hidden"]);
        store.Remove(a.Id);
        Assert.Single(store.List());
    }

    [Fact]
    public void DamagedIndex_IsAnEmptyList()
    {
        using var dir = new TempDir();
        dir.File("index.json", "{ не json");
        Assert.Empty(new BackupStore(dir.Path).List());
    }

    [Fact]
    public void MissingOriginal_Throws_SoTheChangeIsNotMade()
    {
        using var dir = new TempDir();
        Assert.ThrowsAny<IOException>(() => new BackupStore(dir.Path).SavePlist(Path.Combine(dir.Path, "нет.plist"), "x"));
        Assert.Empty(new BackupStore(dir.Path).List());
    }
}
