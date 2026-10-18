namespace PcCleaner.Core;

/// <summary>Panes of System Settings opened with `open` (the legacy ids also open on macOS 12 Monterey).</summary>
public static class SystemSettingsLinks
{
    /// <summary>Конфиденциальность и безопасность → Полный доступ к диску.</summary>
    public const string FullDiskAccess = "x-apple.systempreferences:com.apple.preference.security?Privacy_AllFiles";

    /// <summary>Объекты входа of this Mac's macOS.</summary>
    public static string LoginItems => LoginItemsFor(OperatingSystem.IsMacOSVersionAtLeast(13) ? 13 : 12);

    /// <summary>Конфиденциальность и безопасность → Автоматизация (permission to control System Events).</summary>
    public const string Automation = "x-apple.systempreferences:com.apple.preference.security?Privacy_Automation";

    /// <summary>
    /// macOS 13+: Основные → Объекты входа и расширения (also «Разрешить в фоне»); macOS 12 has no such pane — the login
    /// items are a tab of Пользователи и группы.
    /// </summary>
    public static string LoginItemsFor(int macOsMajor) => macOsMajor >= 13
        ? "x-apple.systempreferences:com.apple.LoginItems-Settings.extension"
        : "x-apple.systempreferences:com.apple.preferences.users";
}
