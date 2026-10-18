using PcCleaner.Core;
using PcCleaner.Core.Updates;
using PcCleaner.Mac.Tests.Support;
using Xunit;

namespace PcCleaner.Mac.Tests.Updates;

public class AppZipTests
{
    [Fact]
    public void Extract_TheBundle()
    {
        using var dir = new TempDir();
        var bytes = MacUpdateKit.BundleZip("1.1.0");
        var zip = dir.File("update.zip", "");
        File.WriteAllBytes(zip, bytes);
        var target = Path.Combine(dir.Path, "WaterCa7s PC Cleaner.app.new");
        AppZip.Extract(zip, MacUpdateKit.FileFor(bytes, "1.1.0"), target);
        Assert.Contains("<string>1.1.0</string>", File.ReadAllText(Path.Combine(target, "Contents", "Info.plist")), StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(target, "Contents", "Resources", "AppIcon.icns")));
        if (!OperatingSystem.IsWindows())
            Assert.True(File.GetUnixFileMode(Path.Combine(target, "Contents", "MacOS", "WaterCa7sPcCleaner")).HasFlag(UnixFileMode.UserExecute));
    }

    [Fact]
    public void Extract_ChecksSizeAndHash_OnTheSameBytes()
    {
        using var dir = new TempDir();
        var bytes = MacUpdateKit.BundleZip("1.1.0");
        var zip = dir.File("update.zip", "");
        File.WriteAllBytes(zip, bytes);
        var target = Path.Combine(dir.Path, "new.app");
        var wrongHash = MacUpdateKit.FileFor(bytes, "1.1.0") with { Sha256 = new string('0', 64) };
        Assert.Contains("контрольная сумма", Assert.Throws<UpdateException>(() => AppZip.Extract(zip, wrongHash, target)).Message, StringComparison.Ordinal);
        var wrongSize = MacUpdateKit.FileFor(bytes, "1.1.0") with { Size = bytes.Length + 1 };
        Assert.Throws<UpdateException>(() => AppZip.Extract(zip, wrongSize, target));
        Assert.False(Directory.Exists(target));
    }

    [Theory]
    [InlineData("WaterCa7s PC Cleaner.app/../../evil.txt")]
    [InlineData("WaterCa7s PC Cleaner.app/Contents/../../evil.txt")]
    [InlineData("/etc/evil.txt")]
    [InlineData("Other.app/Contents/evil.txt")]
    [InlineData("evil.txt")]
    public void Extract_RefusesPathsOutsideTheApp(string name)
    {
        using var dir = new TempDir();
        var bytes = MacUpdateKit.BundleZip("1.1.0", zip => MacUpdateKit.File(zip, name, "evil", 0b110_100_100));
        var target = Path.Combine(dir.Path, "inner", "new.app");
        Assert.Throws<UpdateException>(() => AppZip.ExtractBytes(bytes, target));
        Assert.False(Directory.Exists(target));
        Assert.False(File.Exists(Path.Combine(dir.Path, "evil.txt")));
        Assert.False(File.Exists(Path.Combine(dir.Path, "inner", "evil.txt")));
    }

    [Fact]
    public void Extract_RefusesLinks()
    {
        using var dir = new TempDir();
        var bytes = MacUpdateKit.BundleZip("1.1.0", zip =>
            MacUpdateKit.File(zip, "WaterCa7s PC Cleaner.app/Contents/Resources/link", "/etc/passwd", 0b111_111_111, MacUpdateKit.Symlink));
        Assert.Contains("ссылка", Assert.Throws<UpdateException>(() => AppZip.ExtractBytes(bytes, Path.Combine(dir.Path, "new.app"))).Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Extract_SkipsMacOsMetadata_AndNeedsInfoPlist()
    {
        using var dir = new TempDir();
        var bytes = MacUpdateKit.BundleZip("1.1.0", zip =>
        {
            MacUpdateKit.File(zip, "__MACOSX/WaterCa7s PC Cleaner.app/._Info.plist", "meta", 0b110_100_100);
            MacUpdateKit.File(zip, "WaterCa7s PC Cleaner.app/Contents/._Info.plist", "meta", 0b110_100_100);
        });
        var target = Path.Combine(dir.Path, "new.app");
        AppZip.ExtractBytes(bytes, target);
        Assert.False(File.Exists(Path.Combine(target, "Contents", "._Info.plist")));
        Assert.False(Directory.Exists(Path.Combine(dir.Path, "__MACOSX")));

        using var empty = new MemoryStream();
        using (var zip = new System.IO.Compression.ZipArchive(empty, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true))
            MacUpdateKit.File(zip, "WaterCa7s PC Cleaner.app/readme.txt", "x", 0b110_100_100);
        Assert.Throws<UpdateException>(() => AppZip.ExtractBytes(empty.ToArray(), Path.Combine(dir.Path, "empty.app")));
        Assert.False(Directory.Exists(Path.Combine(dir.Path, "empty.app")));
    }

    [Fact]
    public void Extract_GarbageAndExistingTarget()
    {
        using var dir = new TempDir();
        Assert.Contains("повреждён", Assert.Throws<UpdateException>(() => AppZip.ExtractBytes(new byte[] { 1, 2, 3, 4 }, Path.Combine(dir.Path, "g.app"))).Message,
            StringComparison.Ordinal);
        Directory.CreateDirectory(Path.Combine(dir.Path, "exists.app"));
        Assert.Throws<UpdateException>(() => AppZip.ExtractBytes(MacUpdateKit.BundleZip("1.1.0"), Path.Combine(dir.Path, "exists.app")));
    }
}
