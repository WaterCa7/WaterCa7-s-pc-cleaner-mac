namespace PcCleaner.Core.Updates;

/// <summary>
/// Puts a staged update in place of the running app: unpacks it next to it as "….app.new", checks it (codesign, bundle id,
/// version, and — when the running app is signed with a certificate — the same signing requirement), then renames the
/// running app to "….app.old" and the new one into its place. On any failure the running app stays (or is put back).
/// </summary>
public sealed class BundleInstaller
{
    public const string NewSuffix = ".new";
    public const string OldSuffix = ".old";

    private readonly ICommandRunner _runner;

    public BundleInstaller(ICommandRunner runner) => _runner = runner;

    /// <exception cref="UpdateException">The update does not match update.json or does not pass the checks.</exception>
    /// <exception cref="IOException">A folder could not be renamed.</exception>
    public async Task InstallAsync(StagedUpdate staged, string appPath, CancellationToken ct)
    {
        appPath = Path.TrimEndingDirectorySeparator(appPath);
        var newPath = appPath + NewSuffix;
        var oldPath = appPath + OldSuffix;
        var movedAway = false;
        try
        {
            AppZip.DeleteQuietly(newPath);
            AppZip.Extract(staged.ZipPath, staged.Manifest.Download, newPath);
            await VerifyAsync(newPath, staged.Manifest.Version, appPath, ct);
            AppZip.DeleteQuietly(oldPath);
            if (Directory.Exists(oldPath))
                oldPath = $"{appPath}.{Guid.NewGuid():N}{OldSuffix}";
            Directory.Move(appPath, oldPath);
            movedAway = true;
            Directory.Move(newPath, appPath);
        }
        catch
        {
            if (movedAway && !Directory.Exists(appPath))
                Directory.Move(oldPath, appPath);
            AppZip.DeleteQuietly(newPath);
            throw;
        }
    }

    /// <summary>Run by the new version: removes the previous app and any unfinished unpacking next to it.</summary>
    public static void Cleanup(string appPath)
    {
        appPath = Path.TrimEndingDirectorySeparator(appPath);
        var dir = Path.GetDirectoryName(appPath)!;
        var name = Path.GetFileName(appPath);
        AppZip.DeleteQuietly(appPath + NewSuffix);
        try
        {
            foreach (var old in Directory.EnumerateDirectories(dir, name + "*" + OldSuffix))
                AppZip.DeleteQuietly(old);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"Обновление: не удалось убрать прежнюю версию: {ex.Message}");
        }
    }

    /// <summary>The unpacked app must be intact, be this program, be the version from update.json and be signed like the running one.</summary>
    public async Task VerifyAsync(string newApp, Version version, string runningApp, CancellationToken ct)
    {
        var verify = await _runner.RunAsync("/usr/bin/codesign", new[] { "--verify", "--deep", "--strict", newApp }, TimeSpan.FromMinutes(2), ct);
        if (!verify.Ok)
            throw new UpdateException("подпись новой версии не сходится: " + FirstLine(verify));

        Dictionary<string, object?> info;
        try
        {
            info = Plist.ParseDict(await File.ReadAllTextAsync(Path.Combine(newApp, "Contents", "Info.plist"), ct));
        }
        catch (Exception ex) when (ex is IOException or PlistException)
        {
            throw new UpdateException("Info.plist новой версии не читается", ex);
        }
        if (info.GetString("CFBundleIdentifier") != AppInfo.BundleId)
            throw new UpdateException($"в архиве другая программа: {info.GetString("CFBundleIdentifier")}");
        if (info.GetString("CFBundleShortVersionString") != version.ToString(3))
            throw new UpdateException($"в архиве версия {info.GetString("CFBundleShortVersionString")}, а не {version.ToString(3)}");

        if (await RequirementAsync(runningApp, ct) is { } requirement)
        {
            var same = await _runner.RunAsync("/usr/bin/codesign", new[] { "--verify", "-R=" + requirement, newApp }, TimeSpan.FromMinutes(2), ct);
            if (!same.Ok)
                throw new UpdateException("новая версия подписана другим сертификатом");
        }
    }

    /// <summary>
    /// The designated requirement of the running app when it is signed with a certificate ("identifier … and certificate root
    /// = H\"…\""); null for an ad-hoc signature (a hash of this very build, which no other build can match).
    /// </summary>
    public async Task<string?> RequirementAsync(string app, CancellationToken ct)
    {
        var result = await _runner.RunAsync("/usr/bin/codesign", new[] { "-d", "-r-", app }, null, ct);
        var line = (result.StdOut + "\n" + result.StdErr).Split('\n').Select(l => l.Trim())
            .FirstOrDefault(l => l.StartsWith("designated => ", StringComparison.Ordinal));
        if (line is null)
            return null;
        var requirement = line["designated => ".Length..].Trim();
        return requirement.Contains("certificate", StringComparison.Ordinal) && !requirement.StartsWith("cdhash", StringComparison.Ordinal)
            ? requirement
            : null;
    }

    private static string FirstLine(CommandResult result)
        => (result.StdErr.Length > 0 ? result.StdErr : result.StdOut).Trim().Split('\n')[0].Trim();
}
