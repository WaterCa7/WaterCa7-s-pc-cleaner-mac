using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using PcCleaner.Core;

namespace PcCleaner.Modules.Security;

/// <summary>How a check (or a hosts line) looks: the same scale as in the Windows version.</summary>
public enum SecurityStatus
{
    Unknown = 0,
    Ok = 1,
    Info = 2,
    Warning = 3,
    Danger = 4,
}

public static class SecurityStatusExtensions
{
    public static int Rank(this SecurityStatus s) => s switch
    {
        SecurityStatus.Ok => 0,
        SecurityStatus.Info => 1,
        SecurityStatus.Unknown => 2,
        SecurityStatus.Warning => 3,
        SecurityStatus.Danger => 4,
        _ => 0,
    };
}

/// <summary>One active (non-comment) line of the hosts file (as in the Windows version).</summary>
public sealed class HostsEntry : ObservableObject
{
    private bool _isSelected;

    /// <summary>0-based index of the line in the file.</summary>
    public int LineIndex { get; init; }

    public int LineNumber => LineIndex + 1;

    /// <summary>The line exactly as in the file (without the line break).</summary>
    public string RawLine { get; init; } = "";

    public string Address { get; init; } = "";

    public IReadOnlyList<string> Hostnames { get; init; } = Array.Empty<string>();

    public string HostnamesText => string.Join(", ", Hostnames);

    public string? Comment { get; init; }

    public bool IsValidAddress { get; init; }

    public SecurityStatus Risk { get; set; } = SecurityStatus.Ok;

    public string Reason { get; set; } = "";

    /// <summary>Checked for commenting out in the UI.</summary>
    public bool IsSelected
    {
        get => _isSelected;
        set => Set(ref _isSelected, value);
    }
}

/// <summary>Thrown when the hosts file changed between the check and an edit.</summary>
public sealed class HostsChangedException : Exception
{
    public HostsChangedException(string message) : base(message)
    {
    }
}

/// <summary>
/// Reading and classifying /etc/hosts, and preparing an edit: chosen lines are commented out ("#" in front), every other
/// line, the line breaks and the encoding stay as they were. Writing the file needs the administrator password, so it is
/// done by the caller through the password window.
/// </summary>
public static class HostsFile
{
    public const string DefaultPath = "/etc/hosts";

    public static List<HostsEntry> Read(string text)
    {
        var entries = Parse(text);
        foreach (var e in entries)
            HostsRules.Classify(e);
        return entries;
    }

    /// <summary>Parses the hosts text into active entries (comments and blank lines are skipped).</summary>
    public static List<HostsEntry> Parse(string text)
    {
        var result = new List<HostsEntry>();
        var lines = SplitLines(text);
        for (var i = 0; i < lines.Count; i++)
        {
            var raw = lines[i].Content;
            var hash = raw.IndexOf('#');
            var body = hash >= 0 ? raw[..hash] : raw;
            var comment = hash >= 0 ? raw[(hash + 1)..].Trim() : null;
            body = body.Trim().TrimStart('﻿');
            if (body.Length == 0)
                continue;
            var tokens = Regex.Split(body, @"\s+").Where(t => t.Length > 0).ToArray();
            if (tokens.Length == 0)
                continue;
            var address = tokens[0];
            result.Add(new HostsEntry
            {
                LineIndex = i,
                RawLine = raw,
                Address = address,
                Hostnames = tokens.Skip(1).ToArray(),
                Comment = string.IsNullOrEmpty(comment) ? null : comment,
                IsValidAddress = TryParseAddress(address, out _),
            });
        }
        return result;
    }

    /// <summary>
    /// The text with the given lines commented out ("# " + the line). The lines are matched by index AND exact text:
    /// if the file changed since the check, <see cref="HostsChangedException"/> is thrown and nothing is prepared.
    /// </summary>
    public static string CommentOut(string text, IReadOnlyCollection<HostsEntry> lines)
    {
        var parts = SplitLines(text);
        var chosen = new HashSet<int>();
        foreach (var line in lines)
        {
            if (line.LineIndex < 0 || line.LineIndex >= parts.Count || parts[line.LineIndex].Content != line.RawLine)
                throw new HostsChangedException("Файл hosts изменился после проверки. Нажмите «Проверить снова» и выберите строки ещё раз.");
            chosen.Add(line.LineIndex);
        }
        if (chosen.Count == 0)
            throw new ArgumentException("Не выбрано ни одной строки.", nameof(lines));

        var sb = new StringBuilder(text.Length + chosen.Count * 2);
        for (var i = 0; i < parts.Count; i++)
            sb.Append(chosen.Contains(i) ? "# " + parts[i].Content : parts[i].Content).Append(parts[i].Ending);
        return sb.ToString();
    }

    internal static bool TryParseAddress(string text, out IPAddress? address)
    {
        address = null;
        // IPAddress.TryParse accepts things like "1" or "1.2" — require a full dotted quad or an IPv6 address.
        if (text.Contains(':'))
            return IPAddress.TryParse(text, out address) && address.AddressFamily == AddressFamily.InterNetworkV6;
        return Regex.IsMatch(text, @"^\d{1,3}(\.\d{1,3}){3}$") && IPAddress.TryParse(text, out address);
    }

    /// <summary>Splits text into lines, keeping each line's original terminator ("\r\n", "\n", "\r" or "").</summary>
    internal static List<(string Content, string Ending)> SplitLines(string text)
    {
        var result = new List<(string, string)>();
        var start = 0;
        var i = 0;
        while (i < text.Length)
        {
            var c = text[i];
            if (c == '\r' || c == '\n')
            {
                var ending = c == '\r' && i + 1 < text.Length && text[i + 1] == '\n' ? "\r\n" : c.ToString();
                result.Add((text[start..i], ending));
                i += ending.Length;
                start = i;
            }
            else
            {
                i++;
            }
        }
        if (start < text.Length)
            result.Add((text[start..], ""));
        return result;
    }
}
