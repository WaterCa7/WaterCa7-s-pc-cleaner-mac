using System.Globalization;
using System.Text.RegularExpressions;

namespace PcCleaner.Modules.Processes;

/// <summary>
/// A running process as `ps` sees it. <see cref="Command"/> is usually the full path of the executable, but just a name
/// when the program was started without one (e.g. "dotnet" from Terminal); <see cref="ExecutablePath"/> is then the path
/// macOS knows for it (proc_pidpath), when it tells.
/// </summary>
public sealed record ProcessInfo(int Pid, int Ppid, int Uid, double CpuPercent, long MemoryBytes, TimeSpan Elapsed, string Command, string? CommandLine,
    string? ExecutablePath = null)
{
    /// <summary>The executable: its full path when known, else what `ps` printed.</summary>
    public string Executable => ExecutablePath ?? Command;

    public bool HasPath => Executable.StartsWith('/');

    public string Name => Path.GetFileName(Executable.TrimEnd('/')) is { Length: > 0 } name ? name : Executable;

    /// <summary>The app bundle the executable belongs to ("/Applications/Safari.app"), or null.</summary>
    public string? AppBundle
    {
        get
        {
            var at = Executable.IndexOf(".app/", StringComparison.Ordinal);
            return HasPath && at > 0 ? Executable[..(at + 4)] : null;
        }
    }
}

public static class ProcessParsers
{
    private static readonly Regex CommLine = new(@"^\s*(\d+)\s+(\d+)\s+(\d+)\s+([\d.,]+)\s+(\d+)\s+(\S+)\s+(.+?)\s*$", RegexOptions.CultureInvariant);
    private static readonly Regex ArgsLine = new(@"^\s*(\d+)\s(.*)$", RegexOptions.CultureInvariant);

    /// <summary>`ps -axww -o pid=,ppid=,uid=,pcpu=,rss=,etime=,comm=` (the last column may contain spaces).</summary>
    public static IReadOnlyList<ProcessInfo> ParseComm(string text)
    {
        var result = new List<ProcessInfo>();
        foreach (var line in text.Split('\n'))
        {
            var m = CommLine.Match(line);
            if (!m.Success)
                continue;
            result.Add(new ProcessInfo(
                int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture),
                int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture),
                int.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture),
                double.Parse(m.Groups[4].Value.Replace(',', '.'), CultureInfo.InvariantCulture),
                long.Parse(m.Groups[5].Value, CultureInfo.InvariantCulture) * 1024,
                ParseElapsed(m.Groups[6].Value),
                m.Groups[7].Value,
                null));
        }
        return result;
    }

    /// <summary>`ps -axww -o pid=,args=`: command lines by PID.</summary>
    public static IReadOnlyDictionary<int, string> ParseArgs(string text)
    {
        var result = new Dictionary<int, string>();
        foreach (var line in text.Split('\n'))
        {
            var m = ArgsLine.Match(line.TrimEnd('\r'));
            if (m.Success && int.TryParse(m.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var pid))
                result[pid] = m.Groups[2].Value.Trim();
        }
        return result;
    }

    /// <summary>`etime`: "[[dd-]hh:]mm:ss".</summary>
    public static TimeSpan ParseElapsed(string text)
    {
        var days = 0;
        var dash = text.IndexOf('-');
        if (dash > 0)
        {
            int.TryParse(text[..dash], NumberStyles.None, CultureInfo.InvariantCulture, out days);
            text = text[(dash + 1)..];
        }
        var parts = text.Split(':').Select(p => int.TryParse(p, NumberStyles.None, CultureInfo.InvariantCulture, out var v) ? v : 0).ToArray();
        return parts.Length switch
        {
            3 => new TimeSpan(days, parts[0], parts[1], parts[2]),
            2 => new TimeSpan(days, 0, parts[0], parts[1]),
            1 => TimeSpan.FromSeconds(parts[0]),
            _ => TimeSpan.Zero,
        };
    }

    /// <summary>Both `ps` outputs joined by PID.</summary>
    public static IReadOnlyList<ProcessInfo> Join(string comm, string args)
    {
        var lines = ParseArgs(args);
        return ParseComm(comm).Select(p => p with { CommandLine = lines.GetValueOrDefault(p.Pid) }).ToList();
    }
}
