using System.Globalization;
using System.Text.RegularExpressions;
using PcCleaner.Core;
using PcCleaner.Modules.Processes;

namespace PcCleaner.Modules.Startup;

/// <summary>Where launchd plists live (a test points these at a temporary folder).</summary>
public sealed record StartupFolders(string UserAgents, string GlobalAgents, string Daemons, string AppleAgents, string AppleDaemons)
{
    public static StartupFolders For(string home) => new(
        Path.Combine(home, "Library", "LaunchAgents"), "/Library/LaunchAgents", "/Library/LaunchDaemons",
        "/System/Library/LaunchAgents", "/System/Library/LaunchDaemons");
}

/// <param name="Problems">What could not be checked (the list is still shown).</param>
public sealed record StartupList(IReadOnlyList<LaunchEntry> Items, IReadOnlyList<string> Problems);

public sealed record StartupResult(bool Ok, string Message);

/// <summary>
/// Startup items of launchd: reading the plists and launchd's own state, and reversible changes. Disabling is
/// `launchctl disable` (+ `bootout`), enabling is `enable` (+ `bootstrap`); system daemons only through the macOS password
/// window. Every change first copies the plist into the backups; no copy — no change.
/// </summary>
public sealed class StartupService
{
    private const string Launchctl = "/bin/launchctl";

    // launchctl: "Could not find service" / "No such process" — the service is not loaded, which is fine before a change.
    private const int NotFound = 113;
    private const int NoSuchProcess = 3;

    private static readonly HashSet<string> InterpreterNames = new(StringComparer.Ordinal)
    {
        "sh", "bash", "zsh", "dash", "ksh", "csh", "tcsh", "fish", "env", "python", "python3", "perl", "ruby", "node", "osascript", "open",
    };

    // A path in a shell command: "/x/y", not the "//host" of a URL or the end of "a/b".
    private static readonly Regex AbsolutePath = new(@"(?<![\w.:/])/[^\s'"";|&<>()]+", RegexOptions.CultureInvariant);

    private readonly StartupFolders _folders;
    private readonly int _uid;
    private readonly ICommandRunner _runner;
    private readonly CodeSignatureService _signatures;
    private readonly BackupStore _backups;
    private readonly AdminPrompt _admin;
    private readonly ITrash _trash;
    private readonly Func<string, bool> _missing;

    public StartupService(StartupFolders folders, int uid, ICommandRunner runner, CodeSignatureService signatures, BackupStore backups,
        AdminPrompt admin, ITrash trash, Func<string, bool>? missing = null)
    {
        _folders = folders;
        _uid = uid;
        _runner = runner;
        _signatures = signatures;
        _backups = backups;
        _admin = admin;
        _trash = trash;
        _missing = missing ?? ProcessesService.FileMissing;
    }

    /// <summary>The user's login domain, "gui/501".</summary>
    public string GuiDomain => "gui/" + _uid.ToString(CultureInfo.InvariantCulture);

    /// <summary>The service as launchctl names it: "gui/501/label" or "system/label".</summary>
    public string ServiceTarget(LaunchEntry entry) => $"{Domain(entry)}/{entry.Label}";

    public async Task<StartupList> ListAsync(bool includeApple, CancellationToken ct)
    {
        var listTask = _runner.RunAsync(Launchctl, new[] { "list" }, null, ct);
        var guiDisabledTask = _runner.RunAsync(Launchctl, new[] { "print-disabled", GuiDomain }, null, ct);
        var systemDisabledTask = _runner.RunAsync(Launchctl, new[] { "print-disabled", "system" }, null, ct);

        var folders = new List<(LaunchSource Source, string Folder)>
        {
            (LaunchSource.UserAgent, _folders.UserAgents), (LaunchSource.GlobalAgent, _folders.GlobalAgents), (LaunchSource.Daemon, _folders.Daemons),
        };
        if (includeApple)
        {
            folders.Add((LaunchSource.Apple, _folders.AppleAgents));
            folders.Add((LaunchSource.Apple, _folders.AppleDaemons));
        }
        var drafts = await ReadFoldersAsync(folders, ct);

        var problems = new List<string>();
        var list = await listTask;
        if (!list.Ok)
            problems.Add("launchctl list не ответил — неизвестно, какие агенты запущены");
        var loaded = list.Ok ? LaunchdParsers.ParseList(list.StdOut) : new Dictionary<string, LaunchdListItem>();
        var guiDisabled = Disabled(await guiDisabledTask);
        var systemDisabled = Disabled(await systemDisabledTask);

        // Services apps registered from inside their bundles appear only in launchd's list.
        var known = drafts.Where(d => d.HasLabel).Select(d => d.Label).ToHashSet(StringComparer.Ordinal);
        var registered = loaded.Keys.Where(label => !known.Contains(label) && IsThirdPartyLabel(label)).ToList();
        drafts.AddRange((await ParallelAsync(registered, label => RegisteredAsync(label, includeApple, ct))).OfType<Draft>());

        // A system daemon's state: launchctl print works without the password.
        var daemonStates = (await ParallelAsync(drafts.Where(d => d.Source == LaunchSource.Daemon && d.HasLabel).Select(d => d.Label).Distinct().ToList(),
                async label => (label, state: await DaemonStateAsync(label, ct))))
            .ToDictionary(p => p.label, p => p.state, StringComparer.Ordinal);

        var signatures = await SignaturesAsync(drafts, ct);
        var items = drafts.Select(d => Finish(d, loaded, guiDisabled, systemDisabled, daemonStates, signatures))
            .OrderBy(e => e.Source).ThenBy(e => e.Label, StringComparer.OrdinalIgnoreCase).ToList();
        return new StartupList(items, problems);
    }

    public async Task<StartupResult> DisableAsync(LaunchEntry entry, CancellationToken ct)
    {
        if (Refuse(entry) is { } refusal)
            return refusal;
        if (Backup(entry, "отключение") is { } failed)
            return failed;

        var target = ServiceTarget(entry);
        var loaded = entry.State?.Loaded == true;
        if (entry.NeedsAdmin)
        {
            var commands = new List<AdminCommand> { new(Launchctl, "disable", target) };
            if (loaded)
                commands.Add(new AdminCommand(Launchctl, "bootout", target));
            return AdminOutcome(await _admin.RunAsync($"Отключить «{entry.Label}» в автозагрузке", commands, ct), entry, "отключить",
                $"«{entry.Label}» отключена — больше не запустится вместе с Mac.");
        }

        var disable = await RunAsync(ct, "disable", target);
        if (!disable.Ok)
            return new StartupResult(false, $"Не удалось отключить «{entry.Label}»{Reason(disable)}.");
        Log.Action($"Автозагрузка: отключено {target}");
        if (loaded && await RunAsync(ct, "bootout", target) is { Ok: false, ExitCode: not (NotFound or NoSuchProcess) })
            return new StartupResult(true, $"«{entry.Label}» отключена; уже запущенная копия остановится после выхода из системы.");
        return new StartupResult(true, $"«{entry.Label}» отключена — больше не запустится при входе.");
    }

    public async Task<StartupResult> EnableAsync(LaunchEntry entry, CancellationToken ct)
    {
        if (Refuse(entry) is { } refusal)
            return refusal;
        if (Backup(entry, "включение") is { } failed)
            return failed;

        var target = ServiceTarget(entry);
        // Load it now only when launchd knows it is not loaded; an app's own service is loaded by the app.
        var load = entry.State?.Loaded == false && entry.PlistPath is not null && entry.Source != LaunchSource.AppBundled;
        if (entry.NeedsAdmin)
        {
            var commands = new List<AdminCommand> { new(Launchctl, "enable", target) };
            if (load)
                commands.Add(new AdminCommand(Launchctl, "bootstrap", "system", entry.PlistPath!));
            return AdminOutcome(await _admin.RunAsync($"Включить «{entry.Label}» в автозагрузке", commands, ct), entry, "включить",
                $"«{entry.Label}» включена.");
        }

        var enable = await RunAsync(ct, "enable", target);
        if (!enable.Ok)
            return new StartupResult(false, $"Не удалось включить «{entry.Label}»{Reason(enable)}.");
        Log.Action($"Автозагрузка: включено {target}");
        if (load && !(await RunAsync(ct, "bootstrap", GuiDomain, entry.PlistPath!)).Ok)
            return new StartupResult(true, $"«{entry.Label}» включена и запустится при следующем входе.");
        return new StartupResult(true, $"«{entry.Label}» включена.");
    }

    /// <summary>Only the user's own agents: copy, unload, plist to the Trash.</summary>
    public async Task<StartupResult> RemoveAsync(LaunchEntry entry, CancellationToken ct)
    {
        if (!entry.CanRemove)
            return new StartupResult(false, "Удалять можно только свои записи из ~/Library/LaunchAgents; эту можно отключить.");
        if (Backup(entry, "удаление") is { } failed)
            return failed;

        var stopped = true;
        if (entry.HasLabel && entry.State?.Loaded == true)
            stopped = await RunAsync(ct, "bootout", ServiceTarget(entry)) is { Ok: true } or { ExitCode: NotFound or NoSuchProcess };

        var trashed = await _trash.MoveAsync(entry.PlistPath!, ct);
        if (!trashed.Ok)
            return new StartupResult(false, $"Не удалось переместить «{Path.GetFileName(entry.PlistPath)}» в Корзину: {trashed.Error}");
        Log.Action($"Автозагрузка: {entry.PlistPath} перемещён в Корзину");
        return new StartupResult(true, stopped
            ? $"«{entry.Label}» удалена (файл в Корзине, копия — в «Настройках»)."
            : $"«{entry.Label}» удалена (файл в Корзине); уже запущенная копия остановится после выхода из системы.");
    }

    private StartupResult? Refuse(LaunchEntry entry)
    {
        if (entry.ReadOnly)
            return new StartupResult(false, "Записи macOS здесь только для просмотра.");
        if (!entry.HasLabel)
            return new StartupResult(false, "У записи нет метки — launchd её не загружает; её можно только удалить.");
        return null;
    }

    private StartupResult? Backup(LaunchEntry entry, string action)
    {
        if (entry.PlistPath is null)
            return null;
        try
        {
            _backups.SavePlist(entry.PlistPath, entry.Label, new Dictionary<string, string>
            {
                ["label"] = entry.Label,
                ["source"] = entry.Source.ToString(),
                ["domain"] = Domain(entry),
                ["action"] = action,
            });
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new StartupResult(false, $"Не удалось сохранить резервную копию — ничего не изменено: {ErrorText.For(ex)}");
        }
    }

    private static StartupResult AdminOutcome(AdminResult result, LaunchEntry entry, string verb, string success)
    {
        if (result.Ok)
            return new StartupResult(true, success);
        return result.Cancelled
            ? new StartupResult(false, "Отменено — ничего не изменено.")
            : new StartupResult(false, $"Не удалось {verb} «{entry.Label}»: {result.Error}");
    }

    private string Domain(LaunchEntry entry) => Domain(entry.Source, entry.PlistPath);

    private string Domain(LaunchSource source, string? plistPath)
        => source == LaunchSource.Daemon || (source == LaunchSource.Apple && plistPath?.Contains("/LaunchDaemons/", StringComparison.Ordinal) == true)
            ? "system"
            : GuiDomain;

    private Task<CommandResult> RunAsync(CancellationToken ct, params string[] args) => _runner.RunAsync(Launchctl, args, null, ct);

    private static IReadOnlyDictionary<string, bool> Disabled(CommandResult result)
        => result.Ok ? LaunchdParsers.ParsePrintDisabled(result.StdOut) : new Dictionary<string, bool>();

    private static bool IsThirdPartyLabel(string label)
        => !label.StartsWith("com.apple.", StringComparison.Ordinal) && !label.StartsWith("application.", StringComparison.Ordinal)
           && !label.StartsWith("0x", StringComparison.Ordinal);

    // ---- reading ------------------------------------------------------------------------------------------------

    /// <summary>An entry before launchd's state and the signature are known.</summary>
    private sealed record Draft(
        LaunchSource Source, string? PlistPath, string Label, bool HasLabel, string? Program, IReadOnlyList<string> Arguments,
        bool RunAtLoad, bool KeepAlive, bool DisabledInPlist, string? Problem, LaunchState? State = null);

    private async Task<List<Draft>> ReadFoldersAsync(IReadOnlyList<(LaunchSource Source, string Folder)> folders, CancellationToken ct)
    {
        var files = new List<(LaunchSource Source, string Path)>();
        foreach (var (source, folder) in folders)
        {
            try
            {
                if (Directory.Exists(folder))
                    files.AddRange(Directory.EnumerateFiles(folder, "*.plist").Order(StringComparer.Ordinal).Select(f => (source, f)));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Log.Warn($"Не удалось прочитать папку {folder}: {ErrorText.For(ex)}");
            }
        }
        return (await ParallelAsync(files, f => ReadPlistAsync(f.Source, f.Path, ct), width: 8)).ToList();
    }

    private async Task<Draft> ReadPlistAsync(LaunchSource source, string path, CancellationToken ct)
    {
        var fileLabel = Path.GetFileNameWithoutExtension(path);
        var read = await PlistFile.ReadAsync(path, _runner, ct);
        if (read.Value is not { } plist)
            return new Draft(source, path, fileLabel, false, null, Array.Empty<string>(), false, false, false, $"plist не читается: {read.Problem}");

        var label = plist.GetString("Label");
        var arguments = plist.GetStrings("ProgramArguments");
        var program = plist.GetString("Program") ?? arguments.FirstOrDefault();
        var keepAlive = plist.GetBool("KeepAlive") ?? plist.GetDict("KeepAlive") is { Count: > 0 };
        var problem = string.IsNullOrWhiteSpace(label) ? "в plist нет метки (Label)"
            : program is null ? "в plist не указана программа для запуска"
            : null;
        return new Draft(source, path, string.IsNullOrWhiteSpace(label) ? fileLabel : label, !string.IsNullOrWhiteSpace(label), program, arguments,
            plist.GetBool("RunAtLoad") == true, keepAlive, plist.GetBool("Disabled") == true, problem);
    }

    private async Task<Draft?> RegisteredAsync(string label, bool includeApple, CancellationToken ct)
    {
        var result = await RunAsync(ct, "print", $"{GuiDomain}/{label}");
        if (!result.Ok)
            return null;
        var print = LaunchdParsers.ParsePrint(result.StdOut);
        // Only real startup items (a plist); XPC services of running apps are listed too.
        if (print.Path is not { } path || !path.EndsWith(".plist", StringComparison.Ordinal))
            return null;
        var apple = path.StartsWith("/System/", StringComparison.Ordinal);
        if (apple && !includeApple)
            return null;
        var program = print.Program;
        return new Draft(apple ? LaunchSource.Apple : LaunchSource.AppBundled, path, label, true, program,
            program is null ? Array.Empty<string>() : new[] { program }, false, false, false, null,
            new LaunchState(true, print.Running, print.Pid, print.LastExit));
    }

    private async Task<LaunchState?> DaemonStateAsync(string label, CancellationToken ct)
    {
        var result = await RunAsync(ct, "print", "system/" + label);
        if (result.ExitCode == NotFound)
            return LaunchState.NotLoaded;
        if (!result.Ok)
            return null;
        var print = LaunchdParsers.ParsePrint(result.StdOut);
        return new LaunchState(true, print.Running, print.Pid, print.LastExit);
    }

    private async Task<IReadOnlyDictionary<string, SignatureInfo>> SignaturesAsync(IReadOnlyList<Draft> drafts, CancellationToken ct)
    {
        var paths = drafts.Where(d => d.Source != LaunchSource.Apple)
            .Select(d => TargetOf(d.Program, d.Arguments).Target)
            .OfType<string>()
            .Where(t => t.StartsWith('/') && !ProcessRules.IsSystemPath(t) && !_missing(t))
            .Select(SignaturePath).Distinct(StringComparer.Ordinal).ToList();
        var pairs = await ParallelAsync(paths, async p => (p, signature: await _signatures.GetAsync(p, ct)));
        return pairs.ToDictionary(p => p.p, p => p.signature, StringComparer.Ordinal);
    }

    private LaunchEntry Finish(Draft d, IReadOnlyDictionary<string, LaunchdListItem> loaded, IReadOnlyDictionary<string, bool> guiDisabled,
        IReadOnlyDictionary<string, bool> systemDisabled, IReadOnlyDictionary<string, LaunchState?> daemonStates,
        IReadOnlyDictionary<string, SignatureInfo> signatures)
    {
        var system = Domain(d.Source, d.PlistPath) == "system";
        var state = d.State;
        if (state is null && d.HasLabel)
        {
            if (d.Source == LaunchSource.Daemon)
                state = daemonStates.GetValueOrDefault(d.Label);
            else if (!system)
                state = loaded.TryGetValue(d.Label, out var item) ? new LaunchState(true, item.Pid is not null, item.Pid, item.LastExit) : LaunchState.NotLoaded;
        }
        var overrides = system ? systemDisabled : guiDisabled;
        var disabled = overrides.TryGetValue(d.Label, out var o) ? o : d.DisabledInPlist;

        var (target, shellCommand) = TargetOf(d.Program, d.Arguments);
        var problem = d.Problem;
        if (problem is null && d.Source != LaunchSource.Apple && target is not null && target.StartsWith('/') && _missing(target))
            problem = target == d.Program ? $"программа не найдена: {target}" : $"не найден файл, который она запускает: {target}";

        SignatureInfo? signature = null;
        var hints = new List<Hint>();
        if (d.Source != LaunchSource.Apple)
        {
            if (target is not null && target.StartsWith('/'))
                signature = signatures.GetValueOrDefault(SignaturePath(target));
            if (shellCommand is not null)
                hints.Add(new Hint("запускает команду оболочки: " + Shorten(shellCommand), false));
            hints.AddRange(ProcessRules.Hints(target ?? d.Program ?? "", signature, d.Label, d.PlistPath is null ? null : Path.GetFileNameWithoutExtension(d.PlistPath)));
            if (ProcessRules.ImpersonatesApple(d.Label, target, signature))
                hints.Add(new Hint("называется как служба Apple, но программа не от Apple", true));
        }
        return new LaunchEntry(d.Source, d.PlistPath, d.Label, d.HasLabel, d.Program, d.Arguments, target, d.RunAtLoad, d.KeepAlive, state, disabled,
            problem, signature, hints);
    }

    /// <summary>
    /// The file worth checking: the program itself, or for an interpreter (sh, python, osascript…) the script it runs.
    /// `sh -c "…"` gives the command text and the first non-system path in it.
    /// </summary>
    internal static (string? Target, string? ShellCommand) TargetOf(string? program, IReadOnlyList<string> arguments)
    {
        if (program is null)
            return (null, null);
        var name = Path.GetFileName(program);
        if (!InterpreterNames.Contains(name) && !name.StartsWith("python", StringComparison.Ordinal))
            return (program, null);

        var rest = arguments.Skip(1).ToList();
        var c = rest.IndexOf("-c");
        if (c < 0)
            c = rest.IndexOf("-e"); // osascript -e, perl -e, ruby -e, node -e
        if (c >= 0 && c + 1 < rest.Count)
        {
            var command = rest[c + 1];
            // "/tmp" alone is a folder to work in, not a program: take a path with a file name in it.
            var path = AbsolutePath.Matches(command).Select(m => m.Value)
                .FirstOrDefault(p => p.Count(ch => ch == '/') >= 2 && !p.EndsWith('/') && !ProcessRules.IsSystemPath(p));
            return (path ?? program, command);
        }
        return (rest.FirstOrDefault(a => a.StartsWith('/')) ?? program, null);
    }

    private static string SignaturePath(string target)
    {
        var at = target.IndexOf(".app/", StringComparison.Ordinal);
        return at > 0 ? target[..(at + 4)] : target;
    }

    private static string Shorten(string text) => text.Length <= 120 ? text : text[..117] + "…";

    private static string Reason(CommandResult result)
    {
        var text = (result.StdErr.Length > 0 ? result.StdErr : result.StdOut).Trim().Split('\n')[0].Trim();
        return text.Length == 0 ? "" : ": " + text;
    }

    private static async Task<IReadOnlyList<TResult>> ParallelAsync<T, TResult>(IReadOnlyList<T> items, Func<T, Task<TResult>> work, int width = 4)
    {
        using var gate = new SemaphoreSlim(width);
        return await Task.WhenAll(items.Select(async item =>
        {
            await gate.WaitAsync();
            try
            {
                return await work(item);
            }
            finally
            {
                gate.Release();
            }
        }));
    }
}
