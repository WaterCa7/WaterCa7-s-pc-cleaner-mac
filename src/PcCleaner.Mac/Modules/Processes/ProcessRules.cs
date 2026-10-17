using PcCleaner.Core;

namespace PcCleaner.Modules.Processes;

/// <summary>A reason to look at a program twice. <see cref="Strong"/> hints reach the dashboard; the rest only inform.</summary>
public sealed record Hint(string Text, bool Strong);

/// <summary>Pure rules about processes and the programs they run: what is system, what may not be stopped, what looks odd.</summary>
public static class ProcessRules
{
    private static readonly string[] SystemPrefixes =
    {
        "/System/", "/usr/", "/bin/", "/sbin/", "/Library/Apple/", "/private/var/db/", "/Library/Developer/CommandLineTools/",
    };

    private static readonly HashSet<string> VitalNames = new(StringComparer.Ordinal) { "kernel_task", "launchd", "WindowServer", "loginwindow" };

    /// <summary>Hidden folders in the home folder where developer tools install themselves (not a sign of anything).</summary>
    private static readonly HashSet<string> ToolFolders = new(StringComparer.Ordinal)
    {
        ".dotnet", ".nuget", ".cargo", ".rustup", ".nvm", ".npm", ".pnpm", ".bun", ".deno", ".volta", ".yarn", ".rbenv", ".pyenv",
        ".gem", ".asdf", ".sdkman", ".gradle", ".m2", ".local", ".conda", ".pixi", ".vscode", ".vscode-insiders", ".cursor",
        ".windsurf", ".docker", ".orbstack", ".colima", ".lima", ".rd", ".ghcup", ".opam", ".juliaup", ".claude", ".codex",
    };

    /// <summary>Parts of macOS itself (/usr/local, where Homebrew lived on Intel, is not).</summary>
    public static bool IsSystemPath(string path)
        => SystemPrefixes.Any(p => path.StartsWith(p, StringComparison.Ordinal)) && !path.StartsWith("/usr/local/", StringComparison.Ordinal);

    /// <summary>Why the process may not be stopped from the app, or null when it may.</summary>
    public static string? WhyProtected(ProcessInfo process, int ownPid, int ownUid)
    {
        if (process.Pid <= 1 || VitalNames.Contains(process.Name))
            return "без этого процесса macOS не работает";
        if (process.Pid == ownPid)
            return "это сама программа";
        if (process.Uid != ownUid)
            return process.Uid == 0 ? "системный процесс (root)" : "процесс другого пользователя";
        return null;
    }

    /// <summary>
    /// Hints about where a program lives and who signed it. Temp folders, /Users/Shared and hidden folders are strong only
    /// for programs that are not from a developer Apple knows.
    /// </summary>
    /// <param name="names">Other names to check against known unwanted programs: app name, bundle id, launchd label.</param>
    public static IReadOnlyList<Hint> Hints(string path, SignatureInfo? signature, params string?[] names)
    {
        var hints = new List<Hint>();
        if (MacPups.Match(names.Append(path).ToArray()) is { } pup)
            hints.Add(new Hint(pup, true));
        if (!path.StartsWith('/') || IsSystemPath(path))
            return hints;

        var known = signature?.FromKnownDeveloper == true;
        if (signature?.Kind == SignatureKind.Invalid)
            hints.Add(new Hint("подпись не совпадает с файлом — программу изменили после подписи", true));
        if (path.Contains("/AppTranslocation/", StringComparison.Ordinal))
            hints.Add(new Hint("открыта прямо из «Загрузок» — macOS запускает её из временной копии; лучше перенести в «Программы»", false));
        else if (IsTemp(path))
            hints.Add(new Hint("запущена из временной папки", !known));
        if (path.StartsWith("/Users/Shared/", StringComparison.Ordinal))
            hints.Add(new Hint("запущена из общей папки /Users/Shared", !known));
        if (HiddenFolder(path) is { } hidden)
            hints.Add(hidden == ".Trash" ? new Hint("лежит в Корзине", false) : new Hint($"лежит в скрытой папке «{hidden}»", !known));
        if (signature?.Kind is SignatureKind.Unsigned or SignatureKind.AdHoc && !path.StartsWith("/Applications/", StringComparison.Ordinal))
            hints.Add(new Hint("без подписи разработчика", false));
        return hints;
    }

    /// <summary>A program impersonating Apple: an Apple-looking name outside /System, not signed by Apple.</summary>
    public static bool ImpersonatesApple(string label, string? programPath, SignatureInfo? signature)
        => label.StartsWith("com.apple.", StringComparison.OrdinalIgnoreCase)
           && programPath is not null && !IsSystemPath(programPath)
           && signature is { Kind: not (SignatureKind.Apple or SignatureKind.Unknown) };

    private static bool IsTemp(string path)
        => path.StartsWith("/tmp/", StringComparison.Ordinal) || path.StartsWith("/private/tmp/", StringComparison.Ordinal) ||
           path.StartsWith("/private/var/folders/", StringComparison.Ordinal) || path.StartsWith("/var/folders/", StringComparison.Ordinal) ||
           path.StartsWith("/private/var/tmp/", StringComparison.Ordinal);

    /// <summary>The first hidden folder on the way to the file, except the developer tools' own folders in the home folder.</summary>
    private static string? HiddenFolder(string path)
    {
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < segments.Length - 1; i++)
        {
            var segment = segments[i];
            if (segment.Length < 2 || segment[0] != '.')
                continue;
            return i == 2 && segments[0] == "Users" && ToolFolders.Contains(segment) ? null : segment;
        }
        return null;
    }
}
