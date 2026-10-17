namespace PcCleaner.Core;

public enum FdaState
{
    Granted,
    Denied,
    Unknown,
}

/// <summary>
/// «Полный доступ к диску» (Full Disk Access). Without it macOS hides Safari data, the Trash and other apps' containers.
/// The check opens the user's TCC database for reading and closes it at once — nothing is read.
/// </summary>
public static class FullDiskAccess
{
    public static string ProbePath(string home) => Path.Combine(home, "Library", "Application Support", "com.apple.TCC", "TCC.db");

    public static FdaState Check(string home, Func<string, Stream>? open = null)
    {
        open ??= path => new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        try
        {
            using var stream = open(ProbePath(home));
            return FdaState.Granted;
        }
        catch (UnauthorizedAccessException)
        {
            return FdaState.Denied;
        }
        catch (IOException ex) when (ex.Message.Contains("Operation not permitted", StringComparison.OrdinalIgnoreCase))
        {
            return FdaState.Denied;
        }
        catch (IOException)
        {
            // FileNotFound / DirectoryNotFound and the like: not a Mac, or the file moved.
            return FdaState.Unknown;
        }
    }
}
