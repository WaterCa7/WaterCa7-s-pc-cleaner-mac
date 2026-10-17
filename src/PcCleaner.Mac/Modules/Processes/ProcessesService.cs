using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using PcCleaner.Core;

namespace PcCleaner.Modules.Processes;

/// <summary>A process with what the app knows about it.</summary>
/// <param name="IsSystem">Part of macOS (hidden by «Скрыть системные»).</param>
/// <param name="Protected">Why it may not be stopped from the app, or null.</param>
public sealed record ProcessEntry(ProcessInfo Info, bool IsSystem, string? Protected, SignatureInfo? Signature, IReadOnlyList<Hint> Hints)
{
    public bool HasStrongHint => Hints.Any(h => h.Strong);

    /// <summary>What to show in Finder and check: the app bundle when there is one, else the executable.</summary>
    public string? Location => Info.AppBundle ?? (Info.HasPath ? Info.Executable : null);
}

public sealed record ProcessList(IReadOnlyList<ProcessEntry> Items, string? Error)
{
    public static ProcessList Failed(string error) => new(Array.Empty<ProcessEntry>(), error);
}

/// <param name="StillRunning">The process did not end (the user may try «Завершить принудительно»).</param>
public sealed record TerminateResult(bool Ok, bool StillRunning, string Message);

/// <summary>Running processes (`ps`), their signatures, and stopping the user's own ones (`kill`).</summary>
public sealed class ProcessesService
{
    private static readonly string[] CommArgs = { "-axww", "-o", "pid=,ppid=,uid=,pcpu=,rss=,etime=,comm=" };
    private static readonly string[] ArgsArgs = { "-axww", "-o", "pid=,args=" };

    // Where programs are installed and updated in place: a missing file there usually means an update, not a trick.
    private static readonly string[] InstallPrefixes = { "/Applications/", "/Library/", "/opt/", "/usr/local/" };

    private readonly ICommandRunner _runner;
    private readonly CodeSignatureService _signatures;
    private readonly int _ownPid;
    private readonly Func<string, bool> _missing;
    private readonly Func<int, string?> _executablePath;
    private readonly TimeSpan _exitWait;

    /// <param name="missing">Whether a program file is gone (default: from the disk; a file the app may not read is not "gone").</param>
    /// <param name="exitWait">How long to wait for a process to end after the signal.</param>
    /// <param name="executablePath">The path of a process `ps` knows only by name (default: asks macOS).</param>
    public ProcessesService(ICommandRunner runner, CodeSignatureService signatures, int ownPid, int ownUid,
        Func<string, bool>? missing = null, TimeSpan? exitWait = null, Func<int, string?>? executablePath = null)
    {
        _runner = runner;
        _signatures = signatures;
        _ownPid = ownPid;
        OwnUid = ownUid;
        _missing = missing ?? FileMissing;
        _exitWait = exitWait ?? TimeSpan.FromSeconds(5);
        _executablePath = executablePath ?? MacNative.ProcessPath;
    }

    public int OwnUid { get; }

    public async Task<ProcessList> ListAsync(CancellationToken ct)
    {
        var commTask = _runner.RunAsync("/bin/ps", CommArgs, null, ct);
        var argsTask = _runner.RunAsync("/bin/ps", ArgsArgs, null, ct);
        var comm = await commTask;
        var args = await argsTask;
        if (!comm.Ok)
            return ProcessList.Failed("не удалось получить список процессов" + Reason(comm));

        var processes = ProcessParsers.Join(comm.StdOut, args.Ok ? args.StdOut : "")
            .Select(p => p.HasPath || _executablePath(p.Pid) is not { } path || !path.StartsWith('/') ? p : p with { ExecutablePath = path })
            .ToList();
        var paths = processes.Where(p => p.HasPath && !ProcessRules.IsSystemPath(p.Executable)).Select(SignaturePath).Distinct().ToList();
        var signatures = await SignaturesAsync(paths, ct);
        return new ProcessList(processes.Select(p => Entry(p, signatures)).ToList(), null);
    }

    /// <summary>Sends SIGTERM (or SIGKILL when <paramref name="force"/>) and waits a little for the process to end.</summary>
    public async Task<TerminateResult> TerminateAsync(ProcessEntry entry, bool force, CancellationToken ct)
    {
        var name = entry.Info.Name;
        if (entry.Protected is { } why)
            return new TerminateResult(false, true, $"«{name}» нельзя завершить: {why}.");

        var pid = entry.Info.Pid.ToString(CultureInfo.InvariantCulture);
        // The number may belong to another program by now: stop only the same program of the same user.
        var now = await _runner.RunAsync("/bin/ps", new[] { "-p", pid, "-o", "uid=,comm=" }, null, ct);
        if (!now.Ok || !SameProcess(now.StdOut, entry.Info))
            return new TerminateResult(true, false, $"«{name}» уже завершился.");

        var kill = await _runner.RunAsync("/bin/kill", new[] { force ? "-KILL" : "-TERM", pid }, null, ct);
        if (!kill.Ok)
        {
            return await IsAliveAsync(entry.Info.Pid, ct)
                ? new TerminateResult(false, true, $"Не удалось завершить «{name}»" + Reason(kill) + ".")
                : new TerminateResult(true, false, $"«{name}» уже завершился.");
        }
        Log.Action($"Процесс «{name}» (PID {pid}) — {(force ? "завершён принудительно" : "отправлен сигнал завершения")}");

        var deadline = DateTime.UtcNow + _exitWait;
        while (await IsAliveAsync(entry.Info.Pid, ct))
        {
            if (DateTime.UtcNow >= deadline)
            {
                return new TerminateResult(false, true, force
                    ? $"«{name}» не завершается даже принудительно — возможно, ждёт диска или сети."
                    : $"«{name}» ещё работает — возможно, спрашивает о сохранении. Можно завершить принудительно.");
            }
            await Task.Delay(200, ct);
        }
        return new TerminateResult(true, false, $"«{name}» завершён.");
    }

    /// <summary>`kill -0`: whether the user's own process is still running (for another user's it always says no).</summary>
    public async Task<bool> IsAliveAsync(int pid, CancellationToken ct)
        => (await _runner.RunAsync("/bin/kill", new[] { "-0", pid.ToString(CultureInfo.InvariantCulture) }, null, ct)).Ok;

    /// <summary>SHA-256 of a file in lowercase hex (for looking it up on VirusTotal), or null when it cannot be read.</summary>
    public static async Task<string?> Sha256Async(string path, CancellationToken ct)
    {
        try
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 16, useAsync: true);
            return Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, ct));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>True only when the file is really not there (no permission to look is not "missing").</summary>
    public static bool FileMissing(string path)
    {
        try
        {
            File.GetAttributes(path);
            return false;
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private ProcessEntry Entry(ProcessInfo process, IReadOnlyDictionary<string, SignatureInfo> signatures)
    {
        var system = process.HasPath ? ProcessRules.IsSystemPath(process.Executable) : process.Uid != OwnUid;
        var signature = process.HasPath ? signatures.GetValueOrDefault(SignaturePath(process)) : null;
        var hints = new List<Hint>();
        if (process.HasPath && !system && _missing(process.Executable))
        {
            hints.Add(InstallPrefixes.Any(p => process.Executable.StartsWith(p, StringComparison.Ordinal))
                ? new Hint("программа обновилась или удалена после запуска — её стоит перезапустить", false)
                : new Hint("файл программы удалён после запуска — так часто делают вредоносные программы", true));
        }
        var appName = process.AppBundle is { } bundle ? Path.GetFileNameWithoutExtension(bundle) : null;
        hints.AddRange(ProcessRules.Hints(process.Executable, signature, appName, signature?.Identifier));
        return new ProcessEntry(process, system, ProcessRules.WhyProtected(process, _ownPid, OwnUid), signature, hints);
    }

    // Helpers of one app (Chrome has dozens) share the app's signature: check the bundle once.
    private static string SignaturePath(ProcessInfo process) => process.AppBundle ?? process.Executable;

    private async Task<IReadOnlyDictionary<string, SignatureInfo>> SignaturesAsync(IReadOnlyList<string> paths, CancellationToken ct)
    {
        var result = new ConcurrentDictionary<string, SignatureInfo>(StringComparer.Ordinal);
        using var gate = new SemaphoreSlim(4);
        await Task.WhenAll(paths.Select(async path =>
        {
            await gate.WaitAsync(ct);
            try
            {
                result[path] = await _signatures.GetAsync(path, ct, verify: false);
            }
            finally
            {
                gate.Release();
            }
        }));
        return result;
    }

    // Compared with what `ps` printed for it (the name, for a program started by name), not with the resolved path.
    private static bool SameProcess(string psLine, ProcessInfo expected)
    {
        var line = psLine.Trim();
        var space = line.IndexOf(' ');
        return space > 0
               && int.TryParse(line[..space], NumberStyles.None, CultureInfo.InvariantCulture, out var uid) && uid == expected.Uid
               && line[(space + 1)..].Trim() == expected.Command;
    }

    private static string Reason(CommandResult result)
    {
        var text = (result.StdErr.Length > 0 ? result.StdErr : result.StdOut).Trim().Split('\n')[0].Trim();
        return text.Length == 0 ? "" : ": " + text;
    }
}
