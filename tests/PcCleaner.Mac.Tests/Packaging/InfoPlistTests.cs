using PcCleaner.Core;
using Xunit;

namespace PcCleaner.Mac.Tests.Packaging;

public class InfoPlistTests
{
    [Fact]
    public void Template_HasTheRequiredKeys()
    {
        var d = Plist.ParseDict(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Assets", "Info.plist")));
        Assert.Equal(AppInfo.BundleId, d.GetString("CFBundleIdentifier"));
        Assert.Equal("WaterCa7sPcCleaner", d.GetString("CFBundleExecutable"));
        Assert.Equal("@VERSION@", d.GetString("CFBundleShortVersionString"));
        Assert.Equal("@VERSION@", d.GetString("CFBundleVersion"));
        Assert.Equal("14.0", d.GetString("LSMinimumSystemVersion"));
        Assert.Equal("AppIcon", d.GetString("CFBundleIconFile"));
        Assert.Equal("ru", d.GetString("CFBundleDevelopmentRegion"));
        Assert.Equal("APPL", d.GetString("CFBundlePackageType"));
        Assert.Contains("Finder", d.GetString("NSAppleEventsUsageDescription"));
    }
}
