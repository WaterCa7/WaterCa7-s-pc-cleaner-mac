using PcCleaner.Core;
using PcCleaner.Mac.Tests.Support;
using Xunit;

namespace PcCleaner.Mac.Tests.Shell;

public class FullDiskAccessTests
{
    [Fact]
    public void Readable_IsGranted()
    {
        using var home = new TempDir();
        home.File("Library/Application Support/com.apple.TCC/TCC.db", "x");
        Assert.Equal(FdaState.Granted, FullDiskAccess.Check(home.Path));
    }

    [Fact]
    public void Denied_IsDenied()
        => Assert.Equal(FdaState.Denied, FullDiskAccess.Check("/Users/x", _ => throw new UnauthorizedAccessException()));

    [Fact]
    public void NotPermitted_IsDenied()
        => Assert.Equal(FdaState.Denied, FullDiskAccess.Check("/Users/x", _ => throw new IOException("Operation not permitted")));

    [Fact]
    public void Missing_IsUnknown()
    {
        using var home = new TempDir();
        Assert.Equal(FdaState.Unknown, FullDiskAccess.Check(home.Path));
    }

    [MacFact]
    public void RealMac_GivesAnAnswer()
        => Assert.NotEqual(FdaState.Unknown, FullDiskAccess.Check(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)));
}
