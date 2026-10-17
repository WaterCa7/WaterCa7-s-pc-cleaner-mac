using PcCleaner.Core;
using PcCleaner.Mac.Tests.Support;
using Xunit;

namespace PcCleaner.Mac.Tests.Core;

/// <summary>The same cases for both removers; the Unix one only runs on macOS.</summary>
public abstract class FileRemoverContract
{
    protected abstract IFileRemover Create();

    protected abstract bool CanRun { get; }

    protected void File_InASubfolder_IsDeleted()
    {
        if (!CanRun)
            return;
        using var dir = new TempDir();
        var file = dir.File("a/b/cache.bin", "x");
        Assert.Equal(RemoveOutcome.Deleted, Create().RemoveFile(dir.Path, "a/b/cache.bin"));
        Assert.False(File.Exists(file));
        Assert.True(Directory.Exists(Path.Combine(dir.Path, "a", "b")));
    }

    protected void Missing_IsGone()
    {
        if (!CanRun)
            return;
        using var dir = new TempDir();
        Assert.Equal(RemoveOutcome.Gone, Create().RemoveFile(dir.Path, "nope/x.bin"));
        Assert.Equal(RemoveOutcome.Gone, Create().RemoveFile(dir.Path, "x.bin"));
    }

    protected void Folders_OnlyEmptyOnesGo()
    {
        if (!CanRun)
            return;
        using var dir = new TempDir();
        dir.File("full/x.bin", "x");
        Directory.CreateDirectory(Path.Combine(dir.Path, "empty", "inner"));
        var remover = Create();
        Assert.Equal(RemoveOutcome.Failed, remover.RemoveEmptyDir(dir.Path, "full"));
        Assert.Equal(RemoveOutcome.Failed, remover.RemoveEmptyDir(dir.Path, "empty"));
        Assert.Equal(RemoveOutcome.Deleted, remover.RemoveEmptyDir(dir.Path, "empty/inner"));
        Assert.Equal(RemoveOutcome.Deleted, remover.RemoveEmptyDir(dir.Path, "empty"));
        Assert.True(File.Exists(Path.Combine(dir.Path, "full", "x.bin")));
    }

    protected void BadPaths_AreRefused()
    {
        if (!CanRun)
            return;
        var remover = Create();
        foreach (var bad in new[] { "", "../x", "a/../../x", "/etc/hosts", "a//b", "./x" })
            Assert.Throws<ArgumentException>(() => remover.RemoveFile(TestEnvironment.Root, bad));
    }

    protected void SwappedFolder_IsNotFollowed()
    {
        if (!CanRun)
            return;
        using var root = new TempDir();
        using var outside = new TempDir();
        var precious = outside.File("x.bin", "чужое");
        root.File("a/x.bin", "кэш");
        Directory.Delete(Path.Combine(root.Path, "a"), recursive: true);
        Directory.CreateSymbolicLink(Path.Combine(root.Path, "a"), outside.Path);
        Assert.Equal(RemoveOutcome.Link, Create().RemoveFile(root.Path, "a/x.bin"));
        Assert.True(File.Exists(precious));
    }
}

public class ManagedFileRemoverTests : FileRemoverContract
{
    protected override IFileRemover Create() => new ManagedFileRemover();

    protected override bool CanRun => true;

    [Fact]
    public void FileInASubfolder() => File_InASubfolder_IsDeleted();

    [Fact]
    public void Missing() => Missing_IsGone();

    [Fact]
    public void Folders() => Folders_OnlyEmptyOnesGo();

    [Fact]
    public void BadPaths() => BadPaths_AreRefused();

    [MacFact]
    public void SwappedFolder() => SwappedFolder_IsNotFollowed();
}

public class UnixFileRemoverTests : FileRemoverContract
{
    protected override IFileRemover Create() => new UnixFileRemover();

    protected override bool CanRun => OperatingSystem.IsMacOS();

    [MacFact]
    public void FileInASubfolder() => File_InASubfolder_IsDeleted();

    [MacFact]
    public void Missing() => Missing_IsGone();

    [MacFact]
    public void Folders() => Folders_OnlyEmptyOnesGo();

    [MacFact]
    public void BadPaths() => BadPaths_AreRefused();

    [MacFact]
    public void SwappedFolder() => SwappedFolder_IsNotFollowed();

    [MacFact]
    public void LinkToAFile_RemovesOnlyTheLink()
    {
        using var root = new TempDir();
        using var outside = new TempDir();
        var precious = outside.File("x.bin", "чужое");
        File.CreateSymbolicLink(Path.Combine(root.Path, "link.bin"), precious);
        Assert.Equal(RemoveOutcome.Deleted, Create().RemoveFile(root.Path, "link.bin"));
        Assert.True(File.Exists(precious));
    }

    [MacFact]
    public void ForCurrentOs_IsUnix() => Assert.IsType<UnixFileRemover>(FileRemover.ForCurrentOs());
}
