namespace PcCleaner.Core;

/// <summary>Shared services of the running app (tests build their own instances instead).</summary>
public static class AppServices
{
    public static ICommandRunner Runner { get; set; } = new ProcessCommandRunner();

    public static AppSettings Settings { get; set; } = new();

    /// <summary>Loads settings.json; called once at startup.</summary>
    public static void Init() => Settings = AppSettings.Load();
}
