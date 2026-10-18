using PcCleaner.Core;
using PcCleaner.Modules.Startup;

namespace PcCleaner.Modules.Security;

/// <summary>The proxy and DNS settings of one network service, as saved before a change (and restored from).</summary>
public sealed record NetworkSnapshot(string Service, ProxySetting Web, ProxySetting Secure, ProxySetting Socks, string? PacUrl, bool PacEnabled,
    IReadOnlyList<string> Dns);

/// <summary>
/// The changes «Безопасность и сеть» can make, each through one macOS password window with the commands as data. Network
/// settings and /etc/hosts are copied into the backups first; without a copy nothing changes.
/// </summary>
public sealed class SecurityFixes
{
    private const string Networksetup = "/usr/sbin/networksetup";
    private const string Firewall = "/usr/libexec/ApplicationFirewall/socketfilterfw";

    private readonly AdminPrompt _admin;
    private readonly BackupStore _backups;
    private readonly string _tempDir;
    private readonly Func<string, string> _readText;

    /// <param name="tempDir">Where the new hosts text is written before it is copied over /etc/hosts.</param>
    public SecurityFixes(AdminPrompt admin, BackupStore backups, string tempDir, Func<string, string>? readText = null)
    {
        _admin = admin;
        _backups = backups;
        _tempDir = tempDir;
        _readText = readText ?? File.ReadAllText;
    }

    /// <summary>What the fix runs (shown in the confirmation). Hosts: the temporary file is named by the caller.</summary>
    public static IReadOnlyList<AdminCommand> Commands(FixKind kind, SecurityReport report, string? hostsTemp = null, string hostsPath = HostsFile.DefaultPath)
        => kind switch
        {
            FixKind.Gatekeeper => new[] { new AdminCommand("/usr/sbin/spctl", report.MacOsMajor is < 15 ? "--master-enable" : "--global-enable") },
            FixKind.Firewall => new[] { new AdminCommand(Firewall, "--setglobalstate", "on") },
            FixKind.Stealth => new[] { new AdminCommand(Firewall, "--setstealthmode", "on") },
            FixKind.Proxy => report.Proxies.Where(p => p.Any).SelectMany(ProxyOff).ToList(),
            FixKind.Dns => DnsServices(report).Select(s => new AdminCommand(Networksetup, "-setdnsservers", s, "Empty")).ToList(),
            FixKind.Hosts => new[]
            {
                new AdminCommand("/bin/cp", hostsTemp ?? "<новый текст>", hostsPath),
                new AdminCommand("/usr/bin/dscacheutil", "-flushcache"),
                new AdminCommand("/usr/bin/killall", "-HUP", "mDNSResponder"),
            },
            _ => Array.Empty<AdminCommand>(),
        };

    /// <summary>A plain description of the fix for the confirmation.</summary>
    public static string Describe(FixKind kind, SecurityReport report, int hostsLines = 0) => kind switch
    {
        FixKind.Gatekeeper => "Включить Gatekeeper: macOS снова будет проверять подпись и нотаризацию скачанных программ.",
        FixKind.Firewall => "Включить брандмауэр macOS.",
        FixKind.Stealth => "Включить режим невидимости брандмауэра.",
        FixKind.Proxy => "Выключить прокси для: " + string.Join(", ", report.Proxies.Where(p => p.Any).Select(p => $"{p.Service} ({p.Text})")) +
                         ". Прежние значения сохранятся в «Настройках» → «Резервные копии».",
        FixKind.Dns => "Вернуть автоматический DNS (от роутера) для: " +
                       string.Join(", ", report.ManualDns.Where(m => DnsServices(report).Contains(m.Service)).Select(m => $"{m.Service} ({string.Join(", ", m.Servers)})")) +
                       ". Прежние адреса сохранятся в «Резервных копиях».",
        FixKind.Hosts => $"Закомментировать в /etc/hosts строк: {hostsLines} (перед ними появится «#», строки останутся в файле). Копия файла " +
                         "сохранится в «Резервных копиях».",
        _ => "",
    };

    public async Task<StartupResult> ApplyAsync(FixKind kind, SecurityReport report, IReadOnlyCollection<HostsEntry>? hostsLines, string hostsPath,
        CancellationToken ct)
    {
        switch (kind)
        {
            case FixKind.Gatekeeper or FixKind.Firewall or FixKind.Stealth:
                return Outcome(await _admin.RunAsync(Reason(kind), Commands(kind, report), ct), kind);

            case FixKind.Proxy or FixKind.Dns:
            {
                var commands = Commands(kind, report);
                if (commands.Count == 0)
                    return new StartupResult(false, "Менять нечего — проверьте снова.");
                try
                {
                    _backups.SaveJson(BackupKind.NetworkSettings, kind == FixKind.Proxy ? "Прокси" : "DNS", Snapshots(report),
                        new Dictionary<string, string> { ["action"] = kind == FixKind.Proxy ? "прокси" : "dns" });
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    return new StartupResult(false, $"Не удалось сохранить резервную копию — ничего не изменено: {ErrorText.For(ex)}");
                }
                return Outcome(await _admin.RunAsync(Reason(kind), commands, ct), kind);
            }

            case FixKind.Hosts:
                return await HostsAsync(report, hostsLines ?? Array.Empty<HostsEntry>(), hostsPath, ct);

            default:
                return new StartupResult(false, "Это нельзя исправить автоматически.");
        }
    }

    private async Task<StartupResult> HostsAsync(SecurityReport report, IReadOnlyCollection<HostsEntry> lines, string hostsPath, CancellationToken ct)
    {
        if (lines.Count == 0)
            return new StartupResult(false, "Не выбрано ни одной строки.");
        string newText;
        try
        {
            var current = _readText(hostsPath);
            if (report.HostsText is null || current != report.HostsText)
                throw new HostsChangedException("Файл hosts изменился после проверки. Нажмите «Проверить снова» и выберите строки ещё раз.");
            newText = HostsFile.CommentOut(current, lines);
        }
        catch (HostsChangedException ex)
        {
            return new StartupResult(false, ex.Message + " Ничего не изменено.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new StartupResult(false, $"Не удалось прочитать файл hosts — ничего не изменено: {ErrorText.For(ex)}");
        }

        string temp;
        try
        {
            _backups.SaveFile(BackupKind.HostsFile, hostsPath, "Файл hosts");
            temp = Path.Combine(AppPaths.EnsureDir(_tempDir), $"hosts-{Guid.NewGuid():N}");
            await File.WriteAllTextAsync(temp, newText, ct);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new StartupResult(false, $"Не удалось сохранить резервную копию — ничего не изменено: {ErrorText.For(ex)}");
        }
        try
        {
            return Outcome(await _admin.RunAsync(Reason(FixKind.Hosts), Commands(FixKind.Hosts, report, temp, hostsPath), ct), FixKind.Hosts);
        }
        finally
        {
            try
            {
                File.Delete(temp);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Log.Warn($"Не удалось удалить временный файл {temp}: {ErrorText.For(ex)}");
            }
        }
    }

    /// <summary>The settings to save before a proxy or DNS change: every service, with its manual DNS servers.</summary>
    public static IReadOnlyList<NetworkSnapshot> Snapshots(SecurityReport report)
        => report.Proxies.Select(p => new NetworkSnapshot(p.Service, p.Web, p.Secure, p.Socks, p.PacUrl, p.PacEnabled,
            report.ManualDns.FirstOrDefault(m => m.Service == p.Service).Servers ?? Array.Empty<string>())).ToList();

    /// <summary>Commands that put a service's saved settings back.</summary>
    public static IReadOnlyList<AdminCommand> RestoreCommands(NetworkSnapshot s)
    {
        var commands = new List<AdminCommand>();
        void Proxy(string set, string state, ProxySetting p)
        {
            if (p.Enabled && p.Server.Length > 0)
                commands.Add(new AdminCommand(Networksetup, set, s.Service, p.Server, p.Port.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            else
                commands.Add(new AdminCommand(Networksetup, state, s.Service, "off"));
        }
        Proxy("-setwebproxy", "-setwebproxystate", s.Web);
        Proxy("-setsecurewebproxy", "-setsecurewebproxystate", s.Secure);
        Proxy("-setsocksfirewallproxy", "-setsocksfirewallproxystate", s.Socks);
        if (s.PacUrl is { Length: > 0 } url)
            commands.Add(new AdminCommand(Networksetup, "-setautoproxyurl", s.Service, url));
        commands.Add(new AdminCommand(Networksetup, "-setautoproxystate", s.Service, s.PacEnabled ? "on" : "off"));
        commands.Add(new AdminCommand(Networksetup, new[] { "-setdnsservers", s.Service }.Concat(s.Dns.Count == 0 ? new[] { "Empty" } : s.Dns).ToArray()));
        return commands;
    }

    private static IEnumerable<AdminCommand> ProxyOff(ServiceProxies p)
    {
        if (p.Web.Enabled)
            yield return new AdminCommand(Networksetup, "-setwebproxystate", p.Service, "off");
        if (p.Secure.Enabled)
            yield return new AdminCommand(Networksetup, "-setsecurewebproxystate", p.Service, "off");
        if (p.Socks.Enabled)
            yield return new AdminCommand(Networksetup, "-setsocksfirewallproxystate", p.Service, "off");
        if (p.PacEnabled)
            yield return new AdminCommand(Networksetup, "-setautoproxystate", p.Service, "off");
    }

    private static IReadOnlyList<string> DnsServices(SecurityReport report)
        => report.ManualDns.Where(m => m.Servers.Any(DnsRules.IsUnexpected)).Select(m => m.Service).ToList();

    private static string Reason(FixKind kind) => kind switch
    {
        FixKind.Gatekeeper => "Включить Gatekeeper",
        FixKind.Firewall => "Включить брандмауэр",
        FixKind.Stealth => "Включить режим невидимости брандмауэра",
        FixKind.Proxy => "Выключить прокси",
        FixKind.Dns => "Вернуть автоматический DNS",
        FixKind.Hosts => "Исправить файл hosts",
        _ => "Исправить",
    };

    private static StartupResult Outcome(AdminResult result, FixKind kind)
    {
        if (result.Ok)
        {
            return new StartupResult(true, kind switch
            {
                FixKind.Gatekeeper => "Gatekeeper включён.",
                FixKind.Firewall => "Брандмауэр включён.",
                FixKind.Stealth => "Режим невидимости включён.",
                FixKind.Proxy => "Прокси выключен (прежние значения — в «Резервных копиях»).",
                FixKind.Dns => "DNS снова автоматический (прежние адреса — в «Резервных копиях»).",
                FixKind.Hosts => "Строки в файле hosts закомментированы (копия файла — в «Резервных копиях»).",
                _ => "Готово.",
            });
        }
        return new StartupResult(false, result.Cancelled ? "Отменено — ничего не изменено." : $"Не удалось: {result.Error}");
    }
}
