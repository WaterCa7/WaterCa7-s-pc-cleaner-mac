namespace PcCleaner.Core;

/// <summary>Panes of System Settings opened with `open` (macOS 13+ addresses).</summary>
public static class SystemSettingsLinks
{
    /// <summary>Конфиденциальность и безопасность → Полный доступ к диску.</summary>
    public const string FullDiskAccess = "x-apple.systempreferences:com.apple.preference.security?Privacy_AllFiles";

    /// <summary>Основные → Объекты входа и расширения (also «Разрешить в фоне»).</summary>
    public const string LoginItems = "x-apple.systempreferences:com.apple.LoginItems-Settings.extension";

    /// <summary>Конфиденциальность и безопасность → Автоматизация (permission to control System Events).</summary>
    public const string Automation = "x-apple.systempreferences:com.apple.preference.security?Privacy_Automation";
}
