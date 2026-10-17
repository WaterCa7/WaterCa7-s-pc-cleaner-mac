using System.Collections.Concurrent;

namespace PcCleaner.Core;

public enum SignatureKind
{
    Unknown,
    Unsigned,

    /// <summary>Signed without a certificate: nobody vouches for who made it.</summary>
    AdHoc,

    /// <summary>A certificate Apple does not vouch for (self-signed, development).</summary>
    OtherCertificate,

    DeveloperId,
    AppStore,
    Apple,

    /// <summary>The signature does not match the file any more.</summary>
    Invalid,
}

/// <summary>Who signed a program, as `codesign` reports it.</summary>
public sealed record SignatureInfo(SignatureKind Kind, string? Authority = null, string? TeamId = null, string? Identifier = null)
{
    public static readonly SignatureInfo Unknown = new(SignatureKind.Unknown);

    private const string DeveloperIdPrefix = "Developer ID Application: ";

    /// <summary>Apple, the App Store or a Developer ID — someone Apple knows.</summary>
    public bool FromKnownDeveloper => Kind is SignatureKind.Apple or SignatureKind.AppStore or SignatureKind.DeveloperId;

    public string Text => Kind switch
    {
        SignatureKind.Apple => "Apple",
        SignatureKind.AppStore => TeamId is null ? "App Store" : $"App Store ({TeamId})",
        SignatureKind.DeveloperId => "Developer ID: " + (Authority?.StartsWith(DeveloperIdPrefix, StringComparison.Ordinal) == true
            ? Authority[DeveloperIdPrefix.Length..]
            : Authority ?? TeamId ?? "?"),
        SignatureKind.OtherCertificate => $"сертификат «{Authority}» (не проверен Apple)",
        SignatureKind.AdHoc => "ad-hoc — без разработчика",
        SignatureKind.Unsigned => "без подписи",
        SignatureKind.Invalid => "подпись повреждена или файл изменён",
        _ => "не удалось проверить",
    };
}

/// <summary>What Gatekeeper decides about an app: `spctl --assess`.</summary>
public sealed record GatekeeperVerdict(bool Accepted, string? Source)
{
    public bool Notarized => Source?.Contains("Notarized", StringComparison.Ordinal) == true;
}

public static class CodeSignatureParser
{
    /// <summary>`codesign -dv --verbose=2 &lt;path&gt;` (the details come on stderr; pass both streams).</summary>
    public static SignatureInfo Parse(string output, int exitCode)
    {
        if (output.Contains("not signed at all", StringComparison.Ordinal))
            return new SignatureInfo(SignatureKind.Unsigned);
        if (exitCode != 0)
            return SignatureInfo.Unknown;

        var authorities = new List<string>();
        string? identifier = null, team = null;
        var adHoc = false;
        foreach (var raw in output.Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith("Authority=", StringComparison.Ordinal))
                authorities.Add(line["Authority=".Length..]);
            else if (line.StartsWith("Identifier=", StringComparison.Ordinal))
                identifier = line["Identifier=".Length..];
            else if (line.StartsWith("TeamIdentifier=", StringComparison.Ordinal) && line != "TeamIdentifier=not set")
                team = line["TeamIdentifier=".Length..];
            else if (line == "Signature=adhoc" || (line.StartsWith("CodeDirectory", StringComparison.Ordinal) && line.Contains("(adhoc)", StringComparison.Ordinal)))
                adHoc = true;
        }
        if (adHoc || authorities.Count == 0)
            return new SignatureInfo(SignatureKind.AdHoc, null, team, identifier);
        var leaf = authorities[0];
        var kind = leaf switch
        {
            "Software Signing" or "macOS Software Signing" when authorities.Contains("Apple Root CA") => SignatureKind.Apple,
            "Apple Mac OS Application Signing" => SignatureKind.AppStore,
            _ when leaf.StartsWith("Developer ID Application: ", StringComparison.Ordinal) && authorities.Contains("Apple Root CA") => SignatureKind.DeveloperId,
            _ => SignatureKind.OtherCertificate,
        };
        return new SignatureInfo(kind, leaf, team, identifier);
    }

    /// <summary>`codesign --verify --strict`: false when the signature no longer matches the file.</summary>
    public static bool VerifyPassed(int exitCode, string output)
        => exitCode == 0 || output.Contains("not signed at all", StringComparison.Ordinal);

    /// <summary>`spctl --assess --type execute -vv &lt;app&gt;` (stderr).</summary>
    public static GatekeeperVerdict ParseSpctl(string output, int exitCode)
    {
        string? source = null;
        foreach (var raw in output.Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith("source=", StringComparison.Ordinal))
                source = line["source=".Length..];
        }
        return new GatekeeperVerdict(exitCode == 0 && output.Contains(": accepted", StringComparison.Ordinal), source);
    }
}

/// <summary>Signatures of programs, cached by path and modification time (codesign is slow on hundreds of processes).</summary>
public sealed class CodeSignatureService
{
    private readonly ICommandRunner _runner;
    private readonly ConcurrentDictionary<(string Path, DateTime Changed), SignatureInfo> _cache = new();

    public CodeSignatureService(ICommandRunner runner) => _runner = runner;

    public async Task<SignatureInfo> GetAsync(string path, CancellationToken ct)
    {
        DateTime changed;
        try
        {
            changed = Directory.Exists(path) ? Directory.GetLastWriteTimeUtc(path) : File.Exists(path) ? File.GetLastWriteTimeUtc(path) : default;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            changed = default;
        }
        if (changed == default)
            return SignatureInfo.Unknown;
        if (_cache.TryGetValue((path, changed), out var cached))
            return cached;

        var details = await _runner.RunAsync("/usr/bin/codesign", new[] { "-dv", "--verbose=2", path }, TimeSpan.FromSeconds(30), ct);
        var info = CodeSignatureParser.Parse(details.StdErr + details.StdOut, details.ExitCode);
        if (info.Kind is not (SignatureKind.Unsigned or SignatureKind.Unknown))
        {
            var verify = await _runner.RunAsync("/usr/bin/codesign", new[] { "--verify", "--strict", path }, TimeSpan.FromSeconds(60), ct);
            if (!CodeSignatureParser.VerifyPassed(verify.ExitCode, verify.StdErr + verify.StdOut))
                info = info with { Kind = SignatureKind.Invalid };
        }
        _cache[(path, changed)] = info;
        return info;
    }

    public async Task<GatekeeperVerdict> AssessAsync(string appPath, CancellationToken ct)
    {
        var result = await _runner.RunAsync("/usr/sbin/spctl", new[] { "--assess", "--type", "execute", "-vv", appPath }, TimeSpan.FromSeconds(60), ct);
        return CodeSignatureParser.ParseSpctl(result.StdErr + result.StdOut, result.ExitCode);
    }
}
