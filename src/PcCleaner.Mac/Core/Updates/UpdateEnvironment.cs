using System.ComponentModel;
using System.Diagnostics;
using System.Net.Http;

namespace PcCleaner.Core.Updates;

/// <summary>Everything the updater depends on outside itself; tests replace every part.</summary>
public sealed class UpdateEnvironment
{
    public const string ExecutableName = "WaterCa7sPcCleaner";

    /// <summary>The running executable (…/WaterCa7s PC Cleaner.app/Contents/MacOS/WaterCa7sPcCleaner).</summary>
    public required string ExecutablePath { get; init; }

    public required string UpdatesDir { get; init; }

    public required Version CurrentVersion { get; init; }

    public required ICommandRunner Runner { get; init; }

    public string PublicKeyPem { get; init; } = UpdateKeys.PublicKeyPem;

    public string ManifestUrl { get; init; } = AppInfo.RepoUrl + "/releases/latest/download/update.json";

    public HttpMessageHandler? HttpHandler { get; init; }

    /// <summary>Opens the app as a new instance with the arguments; false when it could not be started.</summary>
    public Func<string, IReadOnlyList<string>, bool> Relaunch { get; init; } = Open;

    /// <summary>Whether the user may write into a folder (the app's own folder must be, to replace the app).</summary>
    public Func<string, bool> CanWrite { get; init; } = CanWriteTo;

    public TimeSpan CheckTimeout { get; init; } = TimeSpan.FromSeconds(15);

    /// <summary>Saves settings.json (tests keep the shared test settings file untouched).</summary>
    public Action<AppSettings> SaveSettings { get; init; } = settings => settings.Save();

    /// <summary>The .app the running executable belongs to, or null when it is not "….app/Contents/MacOS/WaterCa7sPcCleaner".</summary>
    public string? AppBundlePath
    {
        get
        {
            var macOs = Path.GetDirectoryName(ExecutablePath);
            var contents = macOs is null ? null : Path.GetDirectoryName(macOs);
            var bundle = contents is null ? null : Path.GetDirectoryName(contents);
            return Path.GetFileName(ExecutablePath) == ExecutableName && Path.GetFileName(macOs) == "MacOS" &&
                   Path.GetFileName(contents) == "Contents" && bundle is not null && bundle.EndsWith(".app", StringComparison.Ordinal)
                ? bundle
                : null;
        }
    }

    /// <summary>
    /// Only the packaged app updates itself. A development build (run by `dotnet` or from the build folder) never does — an
    /// update would replace it with a release build.
    /// </summary>
    public bool IsInstalledBuild
    {
        get
        {
            if (AppBundlePath is not { } bundle)
                return false;
            try
            {
                var info = Plist.ParseDict(File.ReadAllText(Path.Combine(bundle, "Contents", "Info.plist")));
                return info.GetString("CFBundleIdentifier") == AppInfo.BundleId;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlistException)
            {
                return false;
            }
        }
    }

    /// <summary>Why the app cannot replace itself where it is (the user must move it or download by hand), or null.</summary>
    public string? WhyNotReplaceable
    {
        get
        {
            if (AppBundlePath is not { } bundle)
                return "это сборка для разработки";
            if (bundle.Contains("/AppTranslocation/", StringComparison.Ordinal))
                return "macOS запустила программу из временной копии — перетащите её в папку «Программы» и откройте оттуда";
            if (bundle.StartsWith("/Volumes/", StringComparison.Ordinal))
                return "программа запущена с образа диска или флешки — перетащите её в папку «Программы»";
            var parent = Path.GetDirectoryName(bundle)!;
            return CanWrite(parent) ? null : $"нет прав на запись в папку {parent}";
        }
    }

    public static UpdateEnvironment ForCurrentProcess(ICommandRunner runner) => new()
    {
        ExecutablePath = Environment.ProcessPath ?? "",
        UpdatesDir = AppPaths.UpdatesDir,
        CurrentVersion = typeof(AppInfo).Assembly.GetName().Version ?? new Version(1, 0, 0),
        Runner = runner,
    };

    private static bool Open(string app, IReadOnlyList<string> args)
    {
        var psi = new ProcessStartInfo("/usr/bin/open") { UseShellExecute = false };
        psi.ArgumentList.Add("-n");
        psi.ArgumentList.Add(app);
        if (args.Count > 0)
        {
            psi.ArgumentList.Add("--args");
            foreach (var arg in args)
                psi.ArgumentList.Add(arg);
        }
        try
        {
            using var process = Process.Start(psi);
            process?.WaitForExit(10_000);
            return process is { HasExited: true, ExitCode: 0 };
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            Log.Warn($"Обновление: не удалось запустить {app}: {ex.Message}");
            return false;
        }
    }

    private static bool CanWriteTo(string folder)
    {
        var probe = Path.Combine(folder, $".waterca7s-write-test-{Guid.NewGuid():N}");
        try
        {
            File.WriteAllBytes(probe, Array.Empty<byte>());
            File.Delete(probe);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
