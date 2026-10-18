using System.Runtime.CompilerServices;
using PcCleaner.Core;

namespace PcCleaner.Mac.Tests.Support;

/// <summary>Runs before any test: program data, logs and the home folder point to a temp folder.</summary>
internal static class TestEnvironment
{
    public static string Root { get; } = Path.Combine(Path.GetTempPath(), "WaterCa7sMacTests-" + Guid.NewGuid().ToString("N")[..8]);

    /// <summary>A fresh folder under the test root (not removed: the whole root is temporary).</summary>
    public static string NewTempDir(string name = "t")
    {
        var dir = Path.Combine(Root, "work", name + "-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        return dir;
    }

    [ModuleInitializer]
    internal static void Init()
    {
        Directory.CreateDirectory(Root);
        AppPaths.Home = Path.Combine(Root, "home");
        Directory.CreateDirectory(AppPaths.Home);
        AppPaths.UseRoot(Root);
        // The updater tests describe an Apple Silicon release; the platform must not follow the machine that runs them.
        PcCleaner.Core.Updates.UpdateManifest.PlatformOverride = "osx-arm64";
    }
}
