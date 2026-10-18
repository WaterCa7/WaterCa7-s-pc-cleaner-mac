using System.IO.Compression;
using PcCleaner.Core;
using PcCleaner.Core.Updates;
using PcCleaner.Mac.Tests.Support;

namespace PcCleaner.Mac.Tests.Updates;

/// <summary>Zips of an app bundle (built in memory, with Unix modes) and an installed fake app in a temporary folder.</summary>
internal static class MacUpdateKit
{
    public const int Executable = 0b111_101_101; // 0755
    public const int RegularFile = 0x8000;
    public const int Symlink = 0xA000;

    public static string InfoPlist(string version, string id = AppInfo.BundleId)
        => $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <plist version="1.0"><dict>
            <key>CFBundleIdentifier</key><string>{id}</string>
            <key>CFBundleShortVersionString</key><string>{version}</string>
            <key>CFBundleExecutable</key><string>WaterCa7sPcCleaner</string>
            </dict></plist>
            """;

    /// <summary>A zip like make-app.sh makes: the .app folder with Info.plist, the executable and an icon.</summary>
    public static byte[] BundleZip(string version, Action<ZipArchive>? extra = null, string root = PcCleaner.Core.Updates.AppZip.BundleName, string id = AppInfo.BundleId)
    {
        using var memory = new MemoryStream();
        using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
        {
            Dir(zip, root + "/");
            Dir(zip, root + "/Contents/");
            File(zip, root + "/Contents/Info.plist", InfoPlist(version, id), 0b110_100_100);
            Dir(zip, root + "/Contents/MacOS/");
            // Bytes that do not compress: the manifest wants a zip of at least 1 MB.
            var executable = zip.CreateEntry(root + "/Contents/MacOS/WaterCa7sPcCleaner");
            executable.ExternalAttributes = (RegularFile | Executable) << 16;
            var noise = new byte[(1 << 20) + 100];
            new Random(42).NextBytes(noise);
            using (var stream = executable.Open())
                stream.Write(noise);
            File(zip, root + "/Contents/Resources/AppIcon.icns", "icon", 0b110_100_100);
            extra?.Invoke(zip);
        }
        return memory.ToArray();
    }

    public static void Dir(ZipArchive zip, string name)
    {
        var entry = zip.CreateEntry(name);
        entry.ExternalAttributes = (0x4000 | 0b111_101_101) << 16;
    }

    public static void File(ZipArchive zip, string name, string content, int mode, int type = RegularFile)
    {
        var entry = zip.CreateEntry(name);
        entry.ExternalAttributes = (type | mode) << 16;
        using var writer = new StreamWriter(entry.Open());
        writer.Write(content);
    }

    public static UpdateFile FileFor(byte[] zip, string version)
        => new("WaterCa7sPcCleaner-osx-arm64.zip", UpdateTestKit.Url(version, "WaterCa7sPcCleaner-osx-arm64.zip"), zip.Length, UpdateTestKit.Sha256(zip));

    /// <summary>An installed app in a temporary "Applications" folder: …/WaterCa7s PC Cleaner.app/Contents/MacOS/WaterCa7sPcCleaner.</summary>
    public static string InstalledApp(TempDir dir, string version = "1.0.0")
    {
        var app = Path.Combine(dir.Path, "Applications", AppZip.BundleName);
        Directory.CreateDirectory(Path.Combine(app, "Contents", "MacOS"));
        System.IO.File.WriteAllText(Path.Combine(app, "Contents", "Info.plist"), InfoPlist(version));
        System.IO.File.WriteAllText(Path.Combine(app, "Contents", "MacOS", "WaterCa7sPcCleaner"), "old " + version);
        return app;
    }
}
