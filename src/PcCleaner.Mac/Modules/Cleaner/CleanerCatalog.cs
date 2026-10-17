using PcCleaner.Core;

namespace PcCleaner.Modules.Cleaner;

public enum CleanAction
{
    /// <summary>Regenerable files: deleted for good after the confirmation.</summary>
    Delete,

    /// <summary>The Trash: emptied by Finder after its own confirmation.</summary>
    EmptyTrash,
}

public sealed record CleanerCategory(string Id, string Title, string Description, CleanAction Action);

public static class CleanerCatalog
{
    public static readonly CleanerCategory AppCaches = new("caches", "Кэши программ",
        "Временные файлы программ в ~/Library/Caches — программы создадут их заново. Кэши запущенных программ и системные кэши macOS не трогаются.",
        CleanAction.Delete);

    public static readonly CleanerCategory Logs = new("logs", "Журналы и отчёты о сбоях",
        "Журналы программ и отчёты о сбоях старше 7 дней.",
        CleanAction.Delete);

    public static readonly CleanerCategory Browsers = new("browsers", "Кэши браузеров",
        "Только кэш страниц и картинок Safari, Chrome, Edge, Brave, Opera, Яндекс Браузера и Firefox. Пароли, история, cookies и закладки не трогаются.",
        CleanAction.Delete);

    public static readonly CleanerCategory Developer = new("developer", "Кэши разработки",
        "Xcode DerivedData, кэши симуляторов, загрузки Homebrew, кэши npm, pip, Yarn и CocoaPods. Архивы Xcode и файлы поддержки устройств только показываются.",
        CleanAction.Delete);

    public static readonly CleanerCategory Trash = new("trash", "Корзина",
        "Файлы, которые вы уже удалили. Очистка Корзины безвозвратна.",
        CleanAction.EmptyTrash);

    public static readonly IReadOnlyList<CleanerCategory> All = new[] { AppCaches, Logs, Browsers, Developer, Trash };

    /// <summary>What «Исправить найденное» on the dashboard cleans: nothing the user might still want.</summary>
    public static readonly IReadOnlyList<CleanerCategory> Regenerable = new[] { AppCaches, Logs, Browsers };
}

public enum ScanState
{
    Ready,
    Empty,
    NeedsFullDiskAccess,
    Error,
}

/// <summary>
/// One folder of a category and the files in it that may go. <see cref="Root"/> is never deleted itself; neither are the
/// folders directly inside it (only emptied deeper ones). A skipped target still shows its size.
/// </summary>
public sealed record CleanTarget(string Title, string Root, IReadOnlyList<FileEntry> Files, string? SkipReason = null, bool ShowOnly = false)
{
    public long Bytes => Files.Sum(f => f.Length);

    public bool Cleanable => SkipReason is null && !ShowOnly && Files.Count > 0;
}

/// <summary>What a category holds right now (a read-only check).</summary>
public sealed record CategoryScan(CleanerCategory Category, IReadOnlyList<CleanTarget> Targets, ScanState State, string? Note = null)
{
    public long Bytes => Targets.Where(t => t.Cleanable).Sum(t => t.Bytes);

    public int FileCount => Targets.Where(t => t.Cleanable).Sum(t => t.Files.Count);

    public long ShowOnlyBytes => Targets.Where(t => t.ShowOnly).Sum(t => t.Bytes);

    public long SkippedBytes => Targets.Where(t => t.SkipReason is not null).Sum(t => t.Bytes);

    /// <summary>"Google Chrome: браузер открыт — закройте его" for skipped targets that hold something.</summary>
    public IReadOnlyList<string> SkipNotes
        => Targets.Where(t => t.SkipReason is not null && t.Files.Count > 0)
            .Select(t => $"{t.Title}: {t.SkipReason}")
            .Distinct()
            .ToList();
}
