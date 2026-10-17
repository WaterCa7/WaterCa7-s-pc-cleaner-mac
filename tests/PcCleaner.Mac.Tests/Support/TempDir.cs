namespace PcCleaner.Mac.Tests.Support;

/// <summary>A fresh folder under the test root, removed on Dispose.</summary>
public sealed class TempDir : IDisposable
{
    public TempDir() => Directory.CreateDirectory(Path);

    public string Path { get; } = System.IO.Path.Combine(TestEnvironment.Root, "t-" + Guid.NewGuid().ToString("N")[..8]);

    /// <summary>Writes <paramref name="content"/> to <paramref name="relative"/> (folders are created) and returns the full path.</summary>
    public string File(string relative, string content)
    {
        var full = System.IO.Path.Combine(Path, relative);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
        System.IO.File.WriteAllText(full, content);
        return full;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A leftover temp folder is harmless.
        }
    }
}
