using System.Text.RegularExpressions;
using PcCleaner.Core;

namespace PcCleaner.Modules.Programs;

/// <summary>Something an app left in ~/Library. <see cref="Checked"/>: removed with the app unless the user unticks it.</summary>
public sealed record Leftover(string Path, string What, bool Checked, long Bytes, string? Note = null);

/// <param name="NeedsFullDiskAccess">Containers were not looked at: without Full Disk Access macOS would ask for the other app's data.</param>
public sealed record LeftoverScan(IReadOnlyList<Leftover> Items, bool NeedsFullDiskAccess)
{
    public long CheckedBytes => Items.Where(i => i.Checked).Sum(i => i.Bytes);
}

/// <summary>
/// What an app left in ~/Library, found only by its exact bundle id and exact name — never by similarity. Folders named
/// after the app are ticked only when no other app has that name; a developer's shared group containers are never ticked.
/// Links are not followed and nothing outside ~/Library is returned.
/// </summary>
public static class LeftoverFinder
{
    private static readonly Regex SafeId = new(@"^[A-Za-z0-9][A-Za-z0-9._-]*$", RegexOptions.CultureInvariant);

    // Folders shared by a vendor's apps: never ticked by name.
    private static readonly HashSet<string> VendorFolders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Apple", "Google", "Microsoft", "Adobe", "JetBrains", "Mozilla", "Oracle", "Steam", "Zoom", "Mail", "Logs", "Caches",
    };

    public static Task<LeftoverScan> FindAsync(InstalledApp app, string home, IReadOnlyList<InstalledApp> allApps, bool fullDiskAccess,
        CancellationToken ct)
        => Task.Run(() => Find(app, home, allApps, fullDiskAccess, ct), ct);

    public static LeftoverScan Find(InstalledApp app, string home, IReadOnlyList<InstalledApp> allApps, bool fullDiskAccess,
        CancellationToken ct = default)
    {
        if (app.IsProtected)
            return new LeftoverScan(Array.Empty<Leftover>(), false);
        var library = System.IO.Path.Combine(home, "Library");
        var found = new List<Leftover>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        void Add(string path, string what, bool isChecked, string? note = null)
        {
            ct.ThrowIfCancellationRequested();
            if (!seen.Add(path) || !Inside(library, path) || IsLink(path))
                return;
            long bytes;
            if (Directory.Exists(path))
                bytes = FileWalker.Measure(path, ct).Bytes;
            else if (File.Exists(path))
                bytes = SafeLength(path);
            else
                return;
            found.Add(new Leftover(path, what, isChecked, bytes, note));
        }

        string In(params string[] parts) => System.IO.Path.Combine(new[] { library }.Concat(parts).ToArray());

        var id = app.BundleId is { } bundleId && SafeId.IsMatch(bundleId) ? bundleId : null;
        if (id is not null)
        {
            Add(In("Application Support", id), "Данные программы", true);
            Add(In("Caches", id), "Кэш", true);
            Add(In("Preferences", id + ".plist"), "Настройки", true);
            foreach (var file in Files(In("Preferences", "ByHost"), id + ".*.plist"))
                Add(file, "Настройки для этого Mac", true);
            Add(In("Saved Application State", id + ".savedState"), "Сохранённые окна", true);
            Add(In("HTTPStorages", id), "Данные сети", true);
            Add(In("HTTPStorages", id + ".binarycookies"), "Cookies программы", true);
            Add(In("WebKit", id), "Данные веб-страниц", true);
            Add(In("Cookies", id + ".binarycookies"), "Cookies программы", true);
            Add(In("Application Scripts", id), "Скрипты программы", true);
            var longerIds = allApps.Select(a => a.BundleId).OfType<string>()
                .Where(other => other.Length > id.Length && other.StartsWith(id + ".", StringComparison.OrdinalIgnoreCase)).ToList();
            foreach (var file in Files(In("LaunchAgents"), id + "*.plist"))
            {
                var name = System.IO.Path.GetFileNameWithoutExtension(file);
                var ours = name.Equals(id, StringComparison.OrdinalIgnoreCase) || name.StartsWith(id + ".", StringComparison.OrdinalIgnoreCase);
                if (ours && !longerIds.Any(other => name.StartsWith(other, StringComparison.OrdinalIgnoreCase)))
                    Add(file, "Агент автозагрузки", true);
            }
            if (fullDiskAccess)
                Add(In("Containers", id), "Контейнер программы", true);
        }

        foreach (var name in new[] { app.Name, app.BundleName }.OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (name.Length < 3 || name.Contains('/') || name.StartsWith('.') || name.StartsWith("com.apple.", StringComparison.OrdinalIgnoreCase))
                continue;
            var shared = VendorFolders.Contains(name) || allApps.Any(other => !ReferenceEquals(other, app) && other.Path != app.Path &&
                (string.Equals(other.Name, name, StringComparison.OrdinalIgnoreCase) || string.Equals(other.BundleName, name, StringComparison.OrdinalIgnoreCase)));
            var note = shared ? "папку с таким именем может использовать другая программа — проверьте сами" : "найдено по имени программы";
            Add(In("Application Support", name), "Данные программы", !shared, note);
            Add(In("Logs", name), "Журналы", !shared, note);
        }

        if (fullDiskAccess && app.Signature?.TeamId is { } team && SafeId.IsMatch(team))
        {
            foreach (var dir in Directories(In("Group Containers"), team + ".*"))
                Add(dir, "Общие данные программ разработчика", false, "их часто делят несколько программ одного разработчика");
        }

        return new LeftoverScan(found, !fullDiskAccess && id is not null);
    }

    private static bool Inside(string library, string path)
    {
        var full = System.IO.Path.GetFullPath(path);
        var root = System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(library)) + System.IO.Path.DirectorySeparatorChar;
        // Never ~/Library itself or one of its own folders (Caches, Logs…): only things inside them.
        var relative = full.StartsWith(root, StringComparison.Ordinal) ? full[root.Length..] : null;
        return relative is not null && relative.Split(System.IO.Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries).Length >= 2;
    }

    private static IEnumerable<string> Files(string folder, string pattern)
    {
        try
        {
            return Directory.Exists(folder) ? Directory.EnumerateFiles(folder, pattern).Order(StringComparer.Ordinal).ToList() : Array.Empty<string>();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Array.Empty<string>();
        }
    }

    private static IEnumerable<string> Directories(string folder, string pattern)
    {
        try
        {
            return Directory.Exists(folder) ? Directory.EnumerateDirectories(folder, pattern).Order(StringComparer.Ordinal).ToList() : Array.Empty<string>();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Array.Empty<string>();
        }
    }

    private static bool IsLink(string path)
    {
        try
        {
            // The entry itself, not what it points to (a link to a folder elsewhere is never taken).
            return new FileInfo(path).LinkTarget is not null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return true;
        }
    }

    private static long SafeLength(string path)
    {
        try
        {
            return new FileInfo(path).Length;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }
}
