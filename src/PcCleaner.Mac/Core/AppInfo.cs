namespace PcCleaner.Core;

public static class AppInfo
{
    public const string Name = "WaterCa7s PC Cleaner";

    public const string RepoUrl = "https://github.com/WaterCa7/WaterCa7-s-pc-cleaner-mac";

    /// <summary>CFBundleIdentifier of the .app (tools/mac/Info.plist).</summary>
    public const string BundleId = "io.github.waterca7.pccleaner";

    public static string Version => typeof(AppInfo).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";
}
