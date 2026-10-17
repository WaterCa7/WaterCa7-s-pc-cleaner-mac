using System.Text.Json;

namespace PcCleaner.Core;

public enum BackupKind
{
    /// <summary>A launchd plist (LaunchAgent/LaunchDaemon) copied before it was disabled or removed.</summary>
    LaunchdPlist,

    /// <summary>A login item (name, path, hidden) recorded before it was removed.</summary>
    LoginItem,
}

/// <summary>One backup: when, what, where it came from; <see cref="StoredFile"/> is the copy (for plists).</summary>
public sealed record BackupRecord(
    string Id,
    DateTime Time,
    BackupKind Kind,
    string Title,
    string? OriginalPath,
    string? StoredFile,
    Dictionary<string, string> Details);

/// <summary>
/// Copies made before every change to startup items, with an index (index.json) in the Backups folder. A copy that
/// cannot be written throws: the caller must then not make the change.
/// </summary>
public sealed class BackupStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly object _lock = new();

    public BackupStore(string dir) => Dir = dir;

    public string Dir { get; }

    private string IndexFile => Path.Combine(Dir, "index.json");

    /// <summary>Copies <paramref name="plistPath"/> into its own folder and records it.</summary>
    public BackupRecord SavePlist(string plistPath, string title, IReadOnlyDictionary<string, string>? details = null)
    {
        var id = NewId();
        var folder = AppPaths.EnsureDir(Path.Combine(Dir, id));
        var stored = Path.Combine(folder, Path.GetFileName(plistPath));
        File.Copy(plistPath, stored);
        if (new FileInfo(stored).Length != new FileInfo(plistPath).Length)
            throw new IOException("копия plist не совпала с оригиналом");
        var record = new BackupRecord(id, DateTime.Now, BackupKind.LaunchdPlist, title, plistPath, stored,
            details?.ToDictionary(p => p.Key, p => p.Value) ?? new Dictionary<string, string>());
        Add(record);
        Log.Info($"Резервная копия: {plistPath} → {stored}");
        return record;
    }

    public BackupRecord SaveLoginItem(string name, string path, bool hidden)
    {
        var record = new BackupRecord(NewId(), DateTime.Now, BackupKind.LoginItem, $"Объект входа «{name}»", path, null,
            new Dictionary<string, string> { ["name"] = name, ["path"] = path, ["hidden"] = hidden ? "true" : "false" });
        Add(record);
        Log.Info($"Резервная копия объекта входа: {name} ({path})");
        return record;
    }

    /// <summary>Every backup, the newest first; a damaged index gives an empty list.</summary>
    public IReadOnlyList<BackupRecord> List()
    {
        lock (_lock)
            return Read().OrderByDescending(r => r.Time).ToList();
    }

    public void Remove(string id)
    {
        lock (_lock)
        {
            var records = Read();
            if (records.RemoveAll(r => r.Id == id) > 0)
                Write(records);
        }
    }

    private void Add(BackupRecord record)
    {
        lock (_lock)
        {
            var records = Read();
            records.Add(record);
            Write(records);
        }
    }

    private List<BackupRecord> Read()
    {
        try
        {
            return File.Exists(IndexFile)
                ? JsonSerializer.Deserialize<List<BackupRecord>>(File.ReadAllText(IndexFile), JsonOptions) ?? new List<BackupRecord>()
                : new List<BackupRecord>();
        }
        catch (JsonException ex)
        {
            Log.Warn($"Список резервных копий повреждён: {ex.Message}");
            return new List<BackupRecord>();
        }
    }

    private void Write(List<BackupRecord> records)
    {
        AppPaths.EnsureDir(Dir);
        var tmp = $"{IndexFile}.{Guid.NewGuid():N}.tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(records, JsonOptions));
        File.Move(tmp, IndexFile, overwrite: true);
    }

    private static string NewId() => $"{DateTime.Now:yyyy-MM-dd_HH-mm-ss}_{Guid.NewGuid().ToString("N")[..6]}";
}
