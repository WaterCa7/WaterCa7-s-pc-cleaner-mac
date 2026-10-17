using System.Text.Json;
using System.Text.Json.Serialization;

namespace PcCleaner.Core;

/// <summary>
/// User settings persisted to settings.json. Modules may keep their own small state in <see cref="ModuleData"/>
/// (e.g. the last scan of the dashboard).
/// </summary>
public sealed class AppSettings
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    private static readonly object SaveLock = new();

    private long _snapshotVersion;
    private long _writtenVersion;

    /// <summary>
    /// Free-form per-module storage: ModuleData["Dashboard"]["LastScan"] = "...".
    /// Use <see cref="GetModuleValue"/>/<see cref="SetModuleValue"/>; code that changes it directly must hold
    /// <c>lock (settings)</c> while doing so (and call <see cref="Save"/> after releasing it).
    /// </summary>
    public Dictionary<string, Dictionary<string, string>> ModuleData { get; set; } = new();

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(AppPaths.SettingsFile))
            {
                var json = File.ReadAllText(AppPaths.SettingsFile);
                return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            Log.Warn($"Не удалось прочитать настройки, используются значения по умолчанию: {ErrorText.For(ex)}");
        }
        return new AppSettings();
    }

    /// <summary>
    /// Saves settings.json atomically (tmp + move). Thread-safe: the JSON snapshot is taken under <c>lock (this)</c> and a
    /// snapshot older than the one already written is skipped, so a slow save never overwrites a newer one.
    /// </summary>
    public void Save()
    {
        try
        {
            var (json, version) = Snapshot();
            lock (SaveLock)
            {
                if (version <= _writtenVersion)
                    return;
                AppPaths.EnsureDir(AppPaths.DataDir);
                // A unique temp name: another AppSettings instance (tests, screenshot mode) may be saving at the same time.
                var tmp = $"{AppPaths.SettingsFile}.{Guid.NewGuid():N}.tmp";
                File.WriteAllText(tmp, json);
                File.Move(tmp, AppPaths.SettingsFile, overwrite: true);
                _writtenVersion = version;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error("Не удалось сохранить настройки", ex);
        }
    }

    public string? GetModuleValue(string module, string key)
    {
        lock (this)
            return ModuleData.TryGetValue(module, out var d) && d.TryGetValue(key, out var v) ? v : null;
    }

    public void SetModuleValue(string module, string key, string? value)
    {
        lock (this)
        {
            if (!ModuleData.TryGetValue(module, out var d))
                ModuleData[module] = d = new Dictionary<string, string>();
            if (value is null)
                d.Remove(key);
            else
                d[key] = value;
        }
        Save();
    }

    private (string Json, long Version) Snapshot()
    {
        lock (this)
            return (JsonSerializer.Serialize(this, JsonOptions), ++_snapshotVersion);
    }
}
