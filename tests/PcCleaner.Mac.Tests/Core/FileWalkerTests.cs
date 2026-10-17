using PcCleaner.Core;
using PcCleaner.Mac.Tests.Support;
using Xunit;

namespace PcCleaner.Mac.Tests.Core;

public class FileWalkerTests
{
    [Fact]
    public void Files_AreFoundWithSizesAndRelativePaths()
    {
        using var dir = new TempDir();
        dir.File("a.txt", "12345");
        dir.File("sub/deeper/b.bin", "1234567890");
        var files = FileWalker.Files(dir.Path, CancellationToken.None).OrderBy(f => f.Relative).ToList();
        Assert.Equal(new[] { "a.txt", "sub/deeper/b.bin" }, files.Select(f => f.Relative));
        Assert.Equal(new[] { 5L, 10L }, files.Select(f => f.Length));
        Assert.All(files, f => Assert.True(File.Exists(f.FullPath)));
    }

    [Fact]
    public void SkipDir_IsHonoured()
    {
        using var dir = new TempDir();
        dir.File("keep/a.txt", "1");
        dir.File("skip/b.txt", "1");
        var files = FileWalker.Files(dir.Path, CancellationToken.None, rel => rel == "skip").Select(f => f.Relative).ToList();
        Assert.Equal(new[] { "keep/a.txt" }, files);
    }

    [Fact]
    public void MissingRoot_IsEmpty() => Assert.Empty(FileWalker.Files(Path.Combine(TestEnvironment.Root, "нет"), CancellationToken.None));

    [Fact]
    public void Directories_DeepestFirst()
    {
        using var dir = new TempDir();
        Directory.CreateDirectory(Path.Combine(dir.Path, "a", "b", "c"));
        Directory.CreateDirectory(Path.Combine(dir.Path, "d"));
        var dirs = FileWalker.Directories(dir.Path, CancellationToken.None);
        Assert.Equal("a/b/c", dirs[0]);
        Assert.Equal(4, dirs.Count);
        Assert.True(dirs.ToList().IndexOf("a/b") < dirs.ToList().IndexOf("a"));
    }

    [Fact]
    public void Measure_SumsEverything()
    {
        using var dir = new TempDir();
        dir.File("a.txt", "123");
        dir.File("b/c.txt", "4567");
        Assert.Equal((7L, 2), FileWalker.Measure(dir.Path, CancellationToken.None));
    }

    [Fact]
    public void Cancel_Stops()
    {
        using var dir = new TempDir();
        dir.File("a.txt", "1");
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.Throws<OperationCanceledException>(() => FileWalker.Files(dir.Path, cts.Token).ToList());
    }

    [MacFact]
    public void Links_AreNeitherFollowedNorReported()
    {
        using var root = new TempDir();
        using var outside = new TempDir();
        outside.File("big.bin", new string('x', 1000));
        root.File("own.txt", "1");
        Directory.CreateSymbolicLink(Path.Combine(root.Path, "dirlink"), outside.Path);
        File.CreateSymbolicLink(Path.Combine(root.Path, "filelink"), Path.Combine(outside.Path, "big.bin"));
        Assert.Equal(new[] { "own.txt" }, FileWalker.Files(root.Path, CancellationToken.None).Select(f => f.Relative));
        Assert.Empty(FileWalker.Directories(root.Path, CancellationToken.None));
    }
}
