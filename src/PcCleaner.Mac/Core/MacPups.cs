namespace PcCleaner.Core;

/// <summary>
/// Potentially unwanted programs for the Mac: scareware "optimizers", search hijackers, adware installers. These are not
/// viruses; the app only suggests the user review them. Matching ignores case, spaces and punctuation, so
/// "Search Marquis", "SearchMarquis" and "com.searchmarquis.agent" all match. A name too common to match on its own (VSearch)
/// is matched by its bundle id only.
/// </summary>
public static class MacPups
{
    private sealed record Pup(string Pattern, string Reason);

    private static readonly Pup[] Items =
    {
        new("MacKeeper", "MacKeeper — навязчивый «оптимизатор» с запугиванием и платными исправлениями"),
        new("com.zeobit", "MacKeeper (Zeobit) — навязчивый «оптимизатор» с запугиванием"),
        new("Advanced Mac Cleaner", "Advanced Mac Cleaner — «оптимизатор», который пугает выдуманными проблемами"),
        new("Mac Auto Fixer", "Mac Auto Fixer — «оптимизатор» с запугиванием, часто ставится «в нагрузку»"),
        new("Smart Mac Booster", "Smart Mac Booster — навязчивый «ускоритель»"),
        new("Mac Cleanup Pro", "Mac Cleanup Pro — навязчивый платный очиститель"),
        new("MacBooster", "MacBooster — навязчивый «ускоритель», ставит дополнительные программы"),
        new("Mac Tonic", "Mac Tonic — «оптимизатор» с запугиванием"),
        new("Mac Speedup Pro", "Mac Speedup Pro — «оптимизатор» с запугиванием"),
        new("Genieo", "Genieo — рекламная программа, меняет поиск в браузере"),
        new("Search Baron", "Search Baron — угонщик поиска в браузере"),
        new("Search Marquis", "Search Marquis — угонщик поиска в браузере"),
        new("Bundlore", "Bundlore — установщик рекламных программ"),
        new("InstallCore", "InstallCore — установщик рекламных программ"),
        new("Pirrit", "Pirrit — рекламная программа"),
        new("MyShopcoupon", "MyShopcoupon — рекламная программа"),
        new("WeKnow", "WeKnow — угонщик поиска в браузере"),
        new("com.vsearch", "VSearch — рекламная программа, меняет поиск в браузере"),
        new("MacReviver", "MacReviver — «оптимизатор» с запугиванием и платными исправлениями"),
        new("Mac Mechanic", "Mac Mechanic — «оптимизатор» с запугиванием"),
    };

    private static readonly (string Key, string Reason)[] Normalized = Items.Select(p => (Normalize(p.Pattern), p.Reason)).ToArray();

    /// <summary>The reason when any of <paramref name="names"/> (app name, bundle id, path, label) is a known PUP.</summary>
    public static string? Match(params string?[] names)
    {
        foreach (var name in names)
        {
            if (string.IsNullOrWhiteSpace(name))
                continue;
            var key = Normalize(name);
            foreach (var (pattern, reason) in Normalized)
            {
                if (key.Contains(pattern, StringComparison.Ordinal))
                    return reason;
            }
        }
        return null;
    }

    private static string Normalize(string text) => new(text.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
}
