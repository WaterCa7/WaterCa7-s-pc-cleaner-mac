using PcCleaner.Core;

namespace PcCleaner.Modules.Cleaner;

/// <summary>A folder at the top of the home folder; <see cref="Denied"/> = macOS did not let the program read it.</summary>
public sealed record FolderSize(string Name, string Path, long Bytes, bool Denied);

/// <summary>A file or folder of the user that may be moved to the Trash after a confirmation.</summary>
public sealed record UserItem(string Name, string Path, long Bytes, DateTime LastWriteUtc, bool IsFolder);

/// <summary>
/// Where the space went: the biggest folders of the home folder, large files, old downloads. Only reads; walking the
/// Desktop, Documents and Downloads may make macOS ask the user for access, so it runs only when the user asks.
/// </summary>
public sealed class SpaceAnalyzer
{
    public const int MaxItems = 200;

    private readonly string _home;

    public SpaceAnalyzer(string home) => _home = home;

    public Task<IReadOnlyList<FolderSize>> TopFoldersAsync(CancellationToken ct) => Task.Run<IReadOnlyList<FolderSize>>(() =>
    {
        var result = new List<FolderSize>();
        foreach (var dir in TopFolders())
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                result.Add(new FolderSize(Path.GetFileName(dir), dir, FileWalker.Measure(dir, ct).Bytes, false));
            }
            catch (UnauthorizedAccessException)
            {
                result.Add(new FolderSize(Path.GetFileName(dir), dir, 0, true));
            }
        }
        return result.OrderByDescending(f => f.Bytes).ThenBy(f => f.Name, StringComparer.Ordinal).ToList();
    }, ct);

    /// <summary>Files of at least <paramref name="minBytes"/> outside ~/Library and hidden folders, the biggest first.</summary>
    public Task<IReadOnlyList<UserItem>> LargeFilesAsync(long minBytes, CancellationToken ct) => Task.Run<IReadOnlyList<UserItem>>(() =>
        FileWalker.Files(_home, ct, rel => rel == "Library" || System.IO.Path.GetFileName(rel).StartsWith('.'))
            .Where(f => f.Length >= minBytes && !System.IO.Path.GetFileName(f.Relative).StartsWith('.'))
            .OrderByDescending(f => f.Length)
            .Take(MaxItems)
            .Select(f => new UserItem(System.IO.Path.GetFileName(f.FullPath), f.FullPath, f.Length, f.LastWriteUtc, false))
            .ToList(), ct);

    /// <summary>Files and folders directly in ~/Downloads not changed for <paramref name="olderThanDays"/> days, the biggest first.</summary>
    public Task<IReadOnlyList<UserItem>> OldDownloadsAsync(int olderThanDays, DateTime nowUtc, CancellationToken ct) => Task.Run<IReadOnlyList<UserItem>>(() =>
    {
        var downloads = System.IO.Path.Combine(_home, "Downloads");
        if (!Directory.Exists(downloads))
            return Array.Empty<UserItem>();
        var limit = nowUtc - TimeSpan.FromDays(olderThanDays);
        var items = new List<UserItem>();
        foreach (var entry in new DirectoryInfo(downloads).EnumerateFileSystemInfos())
        {
            ct.ThrowIfCancellationRequested();
            if ((entry.Attributes & FileAttributes.ReparsePoint) != 0 || entry.Name.StartsWith('.') || entry.LastWriteTimeUtc > limit)
                continue;
            if (entry is FileInfo file)
            {
                items.Add(new UserItem(file.Name, file.FullName, file.Length, file.LastWriteTimeUtc, false));
            }
            else
            {
                var files = FileWalker.Files(entry.FullName, ct).ToList();
                if (files.Any(f => f.LastWriteUtc > limit))
                    continue;
                items.Add(new UserItem(entry.Name, entry.FullName, files.Sum(f => f.Length), entry.LastWriteTimeUtc, true));
            }
        }
        return items.OrderByDescending(i => i.Bytes).Take(MaxItems).ToList();
    }, ct);

    private IEnumerable<string> TopFolders()
        => new DirectoryInfo(_home).EnumerateDirectories()
            .Where(d => (d.Attributes & FileAttributes.ReparsePoint) == 0 && d.Name != ".Trash")
            .Select(d => d.FullName)
            .ToList();
}
