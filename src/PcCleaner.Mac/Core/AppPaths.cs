namespace PcCleaner.Core;

/// <summary>
/// Folders of the program: data in ~/Library/Application Support/WaterCa7sPcCleaner, logs in ~/Library/Logs/WaterCa7sPcCleaner.
/// Tests point <see cref="Home"/> at a temp folder; tests and screenshot mode keep data and logs in a temp folder too.
/// </summary>
public static class AppPaths
{
    /// <summary>The user's home folder — the root of every per-user check (a temp folder in tests).</summary>
    public static string Home { get; set; } = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    public static string DataDir { get; set; } = Path.Combine(Home, "Library", "Application Support", "WaterCa7sPcCleaner");

    public static string LogsDir { get; set; } = Path.Combine(Home, "Library", "Logs", "WaterCa7sPcCleaner");

    /// <summary>Copies of plists and previous settings, made before any change.</summary>
    public static string BackupsDir => Path.Combine(DataDir, "Backups");

    /// <summary>Downloaded updates waiting to be installed.</summary>
    public static string UpdatesDir => Path.Combine(DataDir, "Updates");

    public static string SettingsFile => Path.Combine(DataDir, "settings.json");

    public static string EnsureDir(string dir)
    {
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>Keeps data and logs under <paramref name="root"/> (tests, screenshot mode); the home folder stays as is.</summary>
    public static void UseRoot(string root)
    {
        DataDir = Path.Combine(root, "Data");
        LogsDir = Path.Combine(root, "Logs");
    }
}
