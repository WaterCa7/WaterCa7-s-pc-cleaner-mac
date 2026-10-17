using PcCleaner.Core;
using PcCleaner.Modules.Processes;

namespace PcCleaner.Modules.Startup;

public enum LaunchSource
{
    /// <summary>~/Library/LaunchAgents: the user's own; the only kind that may be removed.</summary>
    UserAgent,

    /// <summary>/Library/LaunchAgents: started for every user, installed by programs.</summary>
    GlobalAgent,

    /// <summary>/Library/LaunchDaemons: system services (root); changing them needs the administrator password.</summary>
    Daemon,

    /// <summary>Registered by an app from inside its bundle (SMAppService): known only to launchd.</summary>
    AppBundled,

    /// <summary>/System/Library: part of macOS, read-only (hidden unless asked for).</summary>
    Apple,
}

/// <summary>What launchd says about a service right now.</summary>
public sealed record LaunchState(bool Loaded, bool Running, int? Pid, int? LastExit)
{
    public static readonly LaunchState NotLoaded = new(false, false, null, null);
}

/// <summary>A startup item: a launchd plist (or a service an app registered) with what the app found out about it.</summary>
/// <param name="Label">The launchd label; for a plist without one, its file name (see <see cref="HasLabel"/>).</param>
/// <param name="Program">What launchd starts (Program or the first of ProgramArguments).</param>
/// <param name="Target">The file the checks look at: the program, or the script an interpreter runs.</param>
/// <param name="State">Null when unknown (Apple's daemons are not asked one by one).</param>
/// <param name="Problem">Why the item is broken (no program file, unreadable plist, no label), or null.</param>
public sealed record LaunchEntry(
    LaunchSource Source,
    string? PlistPath,
    string Label,
    bool HasLabel,
    string? Program,
    IReadOnlyList<string> Arguments,
    string? Target,
    bool RunAtLoad,
    bool KeepAlive,
    LaunchState? State,
    bool Disabled,
    string? Problem,
    SignatureInfo? Signature,
    IReadOnlyList<Hint> Hints)
{
    public bool IsBroken => Problem is not null;

    public bool HasStrongHint => Hints.Any(h => h.Strong);

    public bool ReadOnly => Source == LaunchSource.Apple;

    public bool NeedsAdmin => Source == LaunchSource.Daemon;

    public bool CanRemove => Source == LaunchSource.UserAgent && PlistPath is not null;

    public bool IsRunning => State?.Running == true;

    public string CommandLine => Arguments.Count > 1 ? string.Join(' ', Arguments.Select(Quote)) : Program ?? Arguments.FirstOrDefault() ?? "";

    public string SourceText => Source switch
    {
        LaunchSource.UserAgent => "агент пользователя",
        LaunchSource.GlobalAgent => "агент для всех пользователей",
        LaunchSource.Daemon => "системная служба",
        LaunchSource.AppBundled => "встроена в программу",
        _ => "macOS",
    };

    private static string Quote(string arg) => arg.Contains(' ', StringComparison.Ordinal) ? $"\"{arg}\"" : arg;
}

/// <summary>An app opened at login (System Settings → Объекты входа → «Открывать при входе»).</summary>
public sealed record LoginItem(string Name, string Path, bool Hidden);
