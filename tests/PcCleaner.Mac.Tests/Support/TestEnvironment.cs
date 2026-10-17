using System.Runtime.CompilerServices;
using PcCleaner.Core;

namespace PcCleaner.Mac.Tests.Support;

/// <summary>Runs before any test: program data, logs and the home folder point to a temp folder.</summary>
internal static class TestEnvironment
{
    public static string Root { get; } = Path.Combine(Path.GetTempPath(), "WaterCa7sMacTests-" + Guid.NewGuid().ToString("N")[..8]);

    [ModuleInitializer]
    internal static void Init()
    {
        Directory.CreateDirectory(Root);
        AppPaths.Home = Path.Combine(Root, "home");
        Directory.CreateDirectory(AppPaths.Home);
        AppPaths.UseRoot(Root);
    }
}
