using System.Runtime.InteropServices;

namespace PcCleaner.Core;

public enum RemoveOutcome
{
    Deleted,

    /// <summary>Already gone.</summary>
    Gone,

    /// <summary>No permission (or macOS privacy protection).</summary>
    Denied,

    /// <summary>In use.</summary>
    Busy,

    /// <summary>A folder on the way is a symbolic link (it may have been swapped after the scan): nothing was deleted.</summary>
    Link,

    /// <summary>Anything else, e.g. a folder that is not empty.</summary>
    Failed,
}

/// <summary>
/// Permanent deletion of regenerable files (caches, logs) inside a root folder. The root itself is never deleted, and no
/// symbolic link on the way is ever followed, so a folder swapped for a link after the scan cannot redirect the deletion.
/// </summary>
public interface IFileRemover
{
    /// <param name="root">The folder the deletion is confined to.</param>
    /// <param name="relative">Path inside <paramref name="root"/> ("a/b/file"), never empty, absolute or with "..".</param>
    RemoveOutcome RemoveFile(string root, string relative);

    /// <summary>Removes an empty folder inside <paramref name="root"/>; a folder that is not empty stays.</summary>
    RemoveOutcome RemoveEmptyDir(string root, string relative);
}

public static class FileRemover
{
    public static IFileRemover ForCurrentOs() => OperatingSystem.IsMacOS() ? new UnixFileRemover() : new ManagedFileRemover();

    /// <summary>The path segments of <paramref name="relative"/>; throws for empty, absolute or ".." paths.</summary>
    internal static string[] Segments(string relative)
    {
        if (string.IsNullOrEmpty(relative) || Path.IsPathRooted(relative) || relative.StartsWith('/') || relative.StartsWith('\\'))
            throw new ArgumentException($"Нужен путь внутри папки, а не «{relative}»", nameof(relative));
        var parts = relative.Split('/', '\\');
        if (parts.Any(p => p.Length == 0 || p == "." || p == ".."))
            throw new ArgumentException($"Недопустимый путь «{relative}»", nameof(relative));
        return parts;
    }
}

/// <summary>
/// macOS: the root and every folder on the way are opened with O_NOFOLLOW (openat from the previous one), then
/// unlinkat removes the last entry relative to the folder that was actually opened.
/// </summary>
public sealed class UnixFileRemover : IFileRemover
{
    private const int ORdOnly = 0x0;
    private const int ONoFollow = 0x100;
    private const int ODirectory = 0x100000;
    private const int OCloExec = 0x1000000;
    private const int AtRemoveDir = 0x80;

    private const int EPERM = 1;
    private const int ENOENT = 2;
    private const int EACCES = 13;
    private const int EBUSY = 16;
    private const int ENOTDIR = 20;
    private const int ELOOP = 62;

    public RemoveOutcome RemoveFile(string root, string relative) => Remove(root, relative, 0);

    public RemoveOutcome RemoveEmptyDir(string root, string relative) => Remove(root, relative, AtRemoveDir);

    private static RemoveOutcome Remove(string root, string relative, int flags)
    {
        var parts = FileRemover.Segments(relative);
        var fd = open(root, ORdOnly | ODirectory | ONoFollow | OCloExec);
        if (fd < 0)
            return Outcome(Marshal.GetLastPInvokeError());
        try
        {
            for (var i = 0; i < parts.Length - 1; i++)
            {
                var next = openat(fd, parts[i], ORdOnly | ODirectory | ONoFollow | OCloExec);
                if (next < 0)
                    return Outcome(Marshal.GetLastPInvokeError());
                close(fd);
                fd = next;
            }
            return unlinkat(fd, parts[^1], flags) == 0 ? RemoveOutcome.Deleted : Outcome(Marshal.GetLastPInvokeError());
        }
        finally
        {
            close(fd);
        }
    }

    private static RemoveOutcome Outcome(int errno) => errno switch
    {
        ENOENT => RemoveOutcome.Gone,
        EACCES or EPERM => RemoveOutcome.Denied,
        EBUSY => RemoveOutcome.Busy,
        ELOOP or ENOTDIR => RemoveOutcome.Link,
        _ => RemoveOutcome.Failed,
    };

    // open/openat are variadic in C; only the fixed arguments are passed (no O_CREAT, so no mode).
    [DllImport("libc", SetLastError = true)]
    private static extern int open([MarshalAs(UnmanagedType.LPUTF8Str)] string path, int flags);

    [DllImport("libc", SetLastError = true)]
    private static extern int openat(int fd, [MarshalAs(UnmanagedType.LPUTF8Str)] string path, int flags);

    [DllImport("libc", SetLastError = true)]
    private static extern int unlinkat(int fd, [MarshalAs(UnmanagedType.LPUTF8Str)] string path, int flags);

    [DllImport("libc", SetLastError = true)]
    private static extern int close(int fd);
}

/// <summary>
/// Other systems (development and tests on Windows): every folder on the way is checked for being a link before the
/// deletion. Not race-free like <see cref="UnixFileRemover"/>, so the Mac app never uses it.
/// </summary>
public sealed class ManagedFileRemover : IFileRemover
{
    public RemoveOutcome RemoveFile(string root, string relative)
        => Remove(root, relative, path =>
        {
            if (Directory.Exists(path))
                return RemoveOutcome.Failed;
            File.Delete(path);
            return RemoveOutcome.Deleted;
        });

    public RemoveOutcome RemoveEmptyDir(string root, string relative)
        => Remove(root, relative, path =>
        {
            if (Directory.EnumerateFileSystemEntries(path).Any())
                return RemoveOutcome.Failed;
            Directory.Delete(path);
            return RemoveOutcome.Deleted;
        });

    private static RemoveOutcome Remove(string root, string relative, Func<string, RemoveOutcome> delete)
    {
        var parts = FileRemover.Segments(relative);
        try
        {
            var current = root;
            if (new DirectoryInfo(current).LinkTarget is not null)
                return RemoveOutcome.Link;
            for (var i = 0; i < parts.Length - 1; i++)
            {
                current = Path.Combine(current, parts[i]);
                var dir = new DirectoryInfo(current);
                if (dir.LinkTarget is not null)
                    return RemoveOutcome.Link;
                if (!dir.Exists)
                    return RemoveOutcome.Gone;
            }
            var target = Path.Combine(current, parts[^1]);
            if (!File.Exists(target) && !Directory.Exists(target))
                return RemoveOutcome.Gone;
            return delete(target);
        }
        catch (UnauthorizedAccessException)
        {
            return RemoveOutcome.Denied;
        }
        catch (DirectoryNotFoundException)
        {
            return RemoveOutcome.Gone;
        }
        catch (IOException)
        {
            return RemoveOutcome.Busy;
        }
    }
}
