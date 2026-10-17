namespace PcCleaner.Core;

/// <summary>A file found by <see cref="FileWalker"/>: <see cref="Relative"/> is inside the walked root ("a/b/file").</summary>
public sealed record FileEntry(string FullPath, string Relative, long Length, DateTime LastWriteUtc);

/// <summary>
/// Walks a folder without ever following or reporting symbolic links. Folders inside that cannot be read are skipped;
/// a root that cannot be read throws <see cref="UnauthorizedAccessException"/> (macOS privacy protection → the caller
/// asks for Full Disk Access).
/// </summary>
public static class FileWalker
{
    private static readonly EnumerationOptions OneLevel = new()
    {
        RecurseSubdirectories = false,
        IgnoreInaccessible = false,
        AttributesToSkip = 0,
        ReturnSpecialDirectories = false,
    };

    public static IEnumerable<FileEntry> Files(string root, CancellationToken ct, Func<string, bool>? skipDir = null)
    {
        var pending = new Stack<string>();
        pending.Push("");
        while (pending.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var relative = pending.Pop();
            foreach (var entry in Entries(root, relative))
            {
                if ((entry.Attributes & FileAttributes.ReparsePoint) != 0)
                    continue;
                var childRelative = relative.Length == 0 ? entry.Name : relative + "/" + entry.Name;
                if (entry is DirectoryInfo)
                {
                    if (skipDir?.Invoke(childRelative) != true)
                        pending.Push(childRelative);
                }
                else if (entry is FileInfo file)
                {
                    yield return new FileEntry(file.FullName, childRelative, file.Length, file.LastWriteTimeUtc);
                }
            }
        }
    }

    /// <summary>Every folder inside <paramref name="root"/> (relative paths), the deepest first.</summary>
    public static IReadOnlyList<string> Directories(string root, CancellationToken ct)
    {
        var result = new List<string>();
        var pending = new Stack<string>();
        pending.Push("");
        while (pending.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var relative = pending.Pop();
            foreach (var entry in Entries(root, relative))
            {
                if (entry is not DirectoryInfo || (entry.Attributes & FileAttributes.ReparsePoint) != 0)
                    continue;
                var childRelative = relative.Length == 0 ? entry.Name : relative + "/" + entry.Name;
                result.Add(childRelative);
                pending.Push(childRelative);
            }
        }
        return result.OrderByDescending(d => d.Count(c => c == '/')).ToList();
    }

    public static (long Bytes, int Files) Measure(string root, CancellationToken ct)
    {
        long bytes = 0;
        var files = 0;
        foreach (var file in Files(root, ct))
        {
            bytes += file.Length;
            files++;
        }
        return (bytes, files);
    }

    private static IReadOnlyList<FileSystemInfo> Entries(string root, string relative)
    {
        var dir = relative.Length == 0 ? root : Path.Combine(root, relative);
        try
        {
            return new DirectoryInfo(dir).EnumerateFileSystemInfos("*", OneLevel).ToList();
        }
        catch (Exception ex) when (relative.Length > 0 && ex is UnauthorizedAccessException or IOException)
        {
            // A folder inside that cannot be read (or vanished): skip it.
            return Array.Empty<FileSystemInfo>();
        }
        catch (DirectoryNotFoundException)
        {
            return Array.Empty<FileSystemInfo>();
        }
    }
}
