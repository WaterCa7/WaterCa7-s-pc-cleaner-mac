using System.IO.Compression;
using System.Security.Cryptography;

namespace PcCleaner.Core.Updates;

/// <summary>
/// Unpacks a downloaded update. The zip lives in a user-writable folder, so it is read into memory once: the bytes whose
/// size and SHA-256 are checked are the very bytes that are unpacked — nothing can swap the file in between. Only entries
/// inside "WaterCa7s PC Cleaner.app/" are accepted: no "..", no absolute paths, no links; file permissions (the executable
/// bit) come from the archive; macOS's own "__MACOSX/" and "._*" entries are skipped.
/// </summary>
public static class AppZip
{
    public const string BundleName = "WaterCa7s PC Cleaner.app";

    /// <summary>The most a bundle may unpack to (a zip bomb stops here).</summary>
    public const long MaxUnpackedBytes = 2L << 30;

    private const int TypeMask = 0xF000;
    private const int SymlinkType = 0xA000;

    /// <summary>Checks the zip against the (signed) manifest and unpacks it into <paramref name="targetBundle"/>, which must not exist.</summary>
    public static void Extract(string zipPath, UpdateFile expected, string targetBundle)
    {
        byte[] bytes;
        using (var stream = new FileStream(zipPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            if (stream.Length != expected.Size)
                throw new UpdateException($"размер архива {stream.Length} байт, а в update.json — {expected.Size}");
            bytes = new byte[expected.Size];
            stream.ReadExactly(bytes);
        }
        if (Convert.ToHexStringLower(SHA256.HashData(bytes)) != expected.Sha256)
            throw new UpdateException("контрольная сумма архива не совпадает с update.json");
        ExtractBytes(bytes, targetBundle);
    }

    public static void ExtractBytes(byte[] bytes, string targetBundle)
    {
        if (Directory.Exists(targetBundle) || File.Exists(targetBundle))
            throw new UpdateException($"папка для распаковки уже существует: {targetBundle}");
        var root = Path.GetFullPath(targetBundle);
        var prefix = BundleName + "/";
        long unpacked = 0;
        try
        {
            using var zip = new ZipArchive(new MemoryStream(bytes, writable: false), ZipArchiveMode.Read);
            Directory.CreateDirectory(root);
            foreach (var entry in zip.Entries)
            {
                var name = entry.FullName;
                if (name.StartsWith("__MACOSX/", StringComparison.Ordinal) || Path.GetFileName(name.TrimEnd('/')).StartsWith("._", StringComparison.Ordinal))
                    continue;
                if (!name.StartsWith(prefix, StringComparison.Ordinal) && name != BundleName)
                    throw new UpdateException($"в архиве есть файл вне программы: {name}");
                var relative = name.Length > prefix.Length ? name[prefix.Length..] : "";
                if (relative.Length == 0)
                    continue;
                if (relative.StartsWith('/') || relative.Contains('\\') || relative.Split('/').Any(s => s is ".." or "."))
                    throw new UpdateException($"в архиве недопустимый путь: {name}");
                var mode = (entry.ExternalAttributes >> 16) & 0xFFFF;
                if ((mode & TypeMask) == SymlinkType)
                    throw new UpdateException($"в архиве символическая ссылка: {name}");

                var target = Path.GetFullPath(Path.Combine(root, relative));
                if (!target.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                    throw new UpdateException($"в архиве недопустимый путь: {name}");
                if (name.EndsWith('/'))
                {
                    Directory.CreateDirectory(target);
                    continue;
                }
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                using (var input = entry.Open())
                using (var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    var buffer = new byte[81920];
                    int read;
                    while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        unpacked += read;
                        if (unpacked > MaxUnpackedBytes)
                            throw new UpdateException("архив распаковывается в слишком большой объём");
                        output.Write(buffer, 0, read);
                    }
                }
                if (!OperatingSystem.IsWindows() && (mode & 0x1FF) != 0)
                    File.SetUnixFileMode(target, (UnixFileMode)(mode & 0x1FF));
            }
        }
        catch (InvalidDataException ex)
        {
            DeleteQuietly(root);
            throw new UpdateException($"архив повреждён: {ex.Message}", ex);
        }
        catch
        {
            DeleteQuietly(root);
            throw;
        }
        if (!File.Exists(Path.Combine(root, "Contents", "Info.plist")))
        {
            DeleteQuietly(root);
            throw new UpdateException("в архиве нет программы (Contents/Info.plist)");
        }
    }

    internal static void DeleteQuietly(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"Обновление: не удалось удалить {path}: {ex.Message}");
        }
    }
}
