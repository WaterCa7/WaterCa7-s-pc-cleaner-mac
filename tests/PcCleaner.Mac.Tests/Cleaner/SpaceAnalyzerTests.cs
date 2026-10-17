using PcCleaner.Mac.Tests.Support;
using PcCleaner.Modules.Cleaner;
using Xunit;

namespace PcCleaner.Mac.Tests.Cleaner;

public class SpaceAnalyzerTests
{
    [Fact]
    public async Task TopFolders_BiggestFirst_WithoutTheTrash()
    {
        using var home = new FakeHome();
        home.Old("Movies/film.mov", 5000);
        home.Old("Documents/a.txt", 100);
        home.Old("Documents/deep/b.txt", 200);
        home.Old(".Trash/old.bin", 99999);
        home.Old(".npm/_cacache/x", 1000);
        var folders = await new SpaceAnalyzer(home.Path).TopFoldersAsync(CancellationToken.None);
        Assert.Equal(new[] { ("Movies", 5000L), (".npm", 1000L), ("Documents", 300L) }, folders.Select(f => (f.Name, f.Bytes)));
    }

    [Fact]
    public async Task LargeFiles_ThresholdOrderAndNoLibraryOrHidden()
    {
        using var home = new FakeHome();
        home.Old("Downloads/big.iso", 3000);
        home.Old("Documents/project/video.mp4", 2000);
        home.Old("Documents/small.txt", 10);
        home.Old("Library/Caches/huge.bin", 9000);
        home.Old("Documents/project/.git/objects/pack.pack", 9000);
        home.Old("Desktop/.hidden.bin", 9000);
        var files = await new SpaceAnalyzer(home.Path).LargeFilesAsync(1000, CancellationToken.None);
        Assert.Equal(new[] { "big.iso", "video.mp4" }, files.Select(f => f.Name));
    }

    [Fact]
    public async Task OldDownloads_OnlyUntouchedForTheLimit()
    {
        using var home = new FakeHome();
        var ancient = TimeSpan.FromDays(200);
        home.Add("Downloads/old-installer.dmg", 500, ancient);
        home.Add("Downloads/recent.pdf", 900, TimeSpan.FromDays(3));
        home.Add("Downloads/Old project/a.zip", 300, ancient);
        home.Add("Downloads/Mixed/old.txt", 100, ancient);
        home.Add("Downloads/Mixed/new.txt", 100, TimeSpan.FromDays(1));
        Directory.SetLastWriteTimeUtc(Path.Combine(home.Path, "Downloads", "Old project"), FakeHome.Now - TimeSpan.FromDays(200));
        Directory.SetLastWriteTimeUtc(Path.Combine(home.Path, "Downloads", "Mixed"), FakeHome.Now - TimeSpan.FromDays(200));
        var items = await new SpaceAnalyzer(home.Path).OldDownloadsAsync(90, FakeHome.Now, CancellationToken.None);
        Assert.Equal(new[] { ("old-installer.dmg", false), ("Old project", true) }, items.Select(i => (i.Name, i.IsFolder)));
    }

    [Fact]
    public async Task OldDownloads_NoFolder_IsEmpty()
    {
        using var home = new FakeHome();
        Assert.Empty(await new SpaceAnalyzer(home.Path).OldDownloadsAsync(90, FakeHome.Now, CancellationToken.None));
    }
}
