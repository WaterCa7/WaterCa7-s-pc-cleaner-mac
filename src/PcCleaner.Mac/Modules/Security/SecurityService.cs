using System.Globalization;
using PcCleaner.Core;
using PcCleaner.Modules.Startup;

namespace PcCleaner.Modules.Security;

/// <summary>Files the checks read (a test points these at a temporary folder).</summary>
public sealed record SecurityPaths(string Hosts, string LoginWindowPlist, string UpdatePlist, string XProtectPlist)
{
    public static readonly SecurityPaths Default = new(
        HostsFile.DefaultPath,
        "/Library/Preferences/com.apple.loginwindow.plist",
        "/Library/Preferences/com.apple.SoftwareUpdate.plist",
        "/Library/Apple/System/Library/CoreServices/XProtect.bundle/Contents/Info.plist");
}

/// <summary>
/// The read-only checks of «Безопасность и сеть». Every tool is asked through <see cref="ICommandRunner"/>; a tool that
/// does not answer makes its check "нет данных" with the reason, never a failure of the whole page. `softwareupdate -l`
/// is slow and asks Apple's servers, so it runs apart (<see cref="UpdatesAsync"/>).
/// </summary>
public sealed class SecurityService
{
    public static readonly TimeSpan UpdatesTimeout = TimeSpan.FromSeconds(60);
    public static readonly TimeSpan XProtectMaxAge = TimeSpan.FromDays(60);

    private const string Firewall = "/usr/libexec/ApplicationFirewall/socketfilterfw";
    private const string Networksetup = "/usr/sbin/networksetup";

    private readonly ICommandRunner _runner;
    private readonly SecurityPaths _paths;
    private readonly Func<DateTime> _nowUtc;

    public SecurityService(ICommandRunner runner, SecurityPaths? paths = null, Func<DateTime>? nowUtc = null)
    {
        _runner = runner;
        _paths = paths ?? SecurityPaths.Default;
        _nowUtc = nowUtc ?? (() => DateTime.UtcNow);
    }

    public SecurityPaths Paths => _paths;

    public async Task<SecurityReport> CheckAsync(CancellationToken ct)
    {
        var checks = new List<SecurityCheck>();

        // ---- macOS protection
        var sip = SecurityParsers.Sip((await RunAsync(ct, "/usr/bin/csrutil", "status")).StdOut);
        checks.Add(sip switch
        {
            true => Ok("sip", CheckGroup.MacOs, "Защита целостности системы (SIP)", "включена", "Не даёт программам менять файлы macOS."),
            false => new SecurityCheck("sip", CheckGroup.MacOs, "Защита целостности системы (SIP)", SecurityStatus.Danger, "выключена",
                "Без SIP любая программа с паролем администратора может менять файлы macOS и прятаться в них. Включается только из режима " +
                "восстановления: выключите Mac, нажмите и держите кнопку питания до «Загрузка параметров запуска», выберите «Параметры» → " +
                "«Утилиты» → «Терминал», введите csrutil enable и перезагрузите Mac.", IssueSeverity.Critical),
            _ => Unknown("sip", CheckGroup.MacOs, "Защита целостности системы (SIP)", "csrutil не ответил"),
        });

        var gatekeeper = SecurityParsers.Gatekeeper((await RunAsync(ct, "/usr/sbin/spctl", "--status")).StdOut);
        checks.Add(gatekeeper switch
        {
            true => Ok("gatekeeper", CheckGroup.MacOs, "Gatekeeper", "включён", "Программы без подписи и проверки Apple не открываются без вашего разрешения."),
            false => new SecurityCheck("gatekeeper", CheckGroup.MacOs, "Gatekeeper", SecurityStatus.Warning, "выключен",
                "Любая скачанная программа откроется без проверки подписи и нотаризации Apple — так проще всего заразить Mac.", IssueSeverity.High)
            {
                Fix = FixKind.Gatekeeper, FixText = "Включить Gatekeeper",
            },
            _ => Unknown("gatekeeper", CheckGroup.MacOs, "Gatekeeper", "spctl не ответил"),
        });

        var fileVault = SecurityParsers.FileVault((await RunAsync(ct, "/usr/bin/fdesetup", "status")).StdOut);
        checks.Add(fileVault switch
        {
            true => Ok("filevault", CheckGroup.MacOs, "FileVault (шифрование диска)", "включён", "Если Mac потеряют или украдут, ваши файлы не прочитать без пароля."),
            false => new SecurityCheck("filevault", CheckGroup.MacOs, "FileVault (шифрование диска)", SecurityStatus.Warning, "выключен",
                "Без шифрования файлы можно прочитать, вынув диск или загрузив Mac с другого носителя. Включите FileVault в настройках и " +
                "сохраните ключ восстановления в надёжном месте.", IssueSeverity.High)
            {
                SettingsLink = SecurityLinks.FileVault, SettingsText = "Открыть настройки FileVault",
            },
            _ => Unknown("filevault", CheckGroup.MacOs, "FileVault (шифрование диска)", "fdesetup не ответил"),
        });

        var firewall = SecurityParsers.Firewall((await RunAsync(ct, Firewall, "--getglobalstate")).StdOut);
        checks.Add(firewall switch
        {
            true => Ok("firewall", CheckGroup.MacOs, "Брандмауэр", "включён", "Входящие подключения к программам разрешаются только с вашего согласия."),
            false => new SecurityCheck("firewall", CheckGroup.MacOs, "Брандмауэр", SecurityStatus.Warning, "выключен",
                "Программы на Mac могут принимать подключения из сети без вашего ведома. В общественных сетях это особенно опасно.",
                IssueSeverity.Medium)
            {
                Fix = FixKind.Firewall, FixText = "Включить брандмауэр",
            },
            _ => Unknown("firewall", CheckGroup.MacOs, "Брандмауэр", "socketfilterfw не ответил"),
        });
        var stealth = SecurityParsers.Stealth((await RunAsync(ct, Firewall, "--getstealthmode")).StdOut);
        checks.Add(stealth switch
        {
            true => Ok("stealth", CheckGroup.MacOs, "Режим невидимости", "включён", "Mac не отвечает на проверки из сети (ping)."),
            false => new SecurityCheck("stealth", CheckGroup.MacOs, "Режим невидимости", SecurityStatus.Info, "выключен",
                "В режиме невидимости Mac не отвечает на «прощупывание» из сети — его сложнее найти в общественном Wi-Fi.", IssueSeverity.Info)
            {
                Fix = FixKind.Stealth, FixText = "Включить невидимость",
            },
            _ => Unknown("stealth", CheckGroup.MacOs, "Режим невидимости", "socketfilterfw не ответил"),
        });

        checks.Add(await AutoUpdatesAsync(ct));
        var major = await MacOsMajorAsync(ct);
        checks.Add(MacOsCheck(major, Array.Empty<int>()));
        checks.Add(await XProtectAsync(ct));

        // ---- login and sharing
        checks.AddRange(await LoginAsync(ct));
        checks.AddRange(await SharingAsync(ct));

        // ---- profiles
        var profilesJson = await RunAsync(ct, "/usr/sbin/system_profiler", "SPConfigurationProfileDataType", "-json");
        var profiles = profilesJson.Ok ? ProfileParsers.Parse(profilesJson.StdOut) : Array.Empty<ConfigProfile>();
        var enrolled = SecurityParsers.MdmEnrolled((await RunAsync(ct, "/usr/bin/profiles", "status", "-type", "enrollment")).StdOut);
        checks.AddRange(ProfileChecks(profiles, enrolled, profilesJson.Ok));

        // ---- network
        var services = NetworkParsers.Services((await RunAsync(ct, Networksetup, "-listallnetworkservices")).StdOut).Where(s => s.Enabled).ToList();
        var proxies = new List<ServiceProxies>();
        var manualDns = new List<(string, IReadOnlyList<string>)>();
        foreach (var service in services)
        {
            var (pacUrl, pacEnabled) = NetworkParsers.Pac((await RunAsync(ct, Networksetup, "-getautoproxyurl", service.Name)).StdOut);
            proxies.Add(new ServiceProxies(service.Name,
                NetworkParsers.Proxy((await RunAsync(ct, Networksetup, "-getwebproxy", service.Name)).StdOut),
                NetworkParsers.Proxy((await RunAsync(ct, Networksetup, "-getsecurewebproxy", service.Name)).StdOut),
                NetworkParsers.Proxy((await RunAsync(ct, Networksetup, "-getsocksfirewallproxy", service.Name)).StdOut),
                pacUrl, pacEnabled,
                NetworkParsers.AutoDiscovery((await RunAsync(ct, Networksetup, "-getproxyautodiscovery", service.Name)).StdOut)));
            var dns = NetworkParsers.DnsServers((await RunAsync(ct, Networksetup, "-getdnsservers", service.Name)).StdOut);
            if (dns.Count > 0)
                manualDns.Add((service.Name, dns));
        }
        var effective = NetworkParsers.ScutilProxy((await RunAsync(ct, "/usr/sbin/scutil", "--proxy")).StdOut);
        checks.Add(ProxyCheck(proxies, effective));
        checks.Add(DnsCheck(manualDns));

        var (hostsText, hostsEntries, hostsCheck) = ReadHosts();
        checks.Add(hostsCheck);
        checks.Add(WifiCheck(NetworkParsers.Wifi((await RunAsync(ct, "/usr/sbin/system_profiler", "SPAirPortDataType", "-json")).StdOut)));

        return new SecurityReport(checks, proxies, manualDns, hostsText, hostsEntries, profiles);
    }

    /// <summary>Updates offered by `softwareupdate -l` (slow: asks Apple's servers, up to a minute).</summary>
    public async Task<SecurityCheck> UpdatesAsync(CancellationToken ct)
    {
        var result = await _runner.RunAsync("/usr/sbin/softwareupdate", new[] { "-l" }, UpdatesTimeout, ct);
        const string title = "Обновления macOS";
        if (result.TimedOut || (!result.Ok && result.StdOut.Length == 0))
            return Unknown("updates", CheckGroup.MacOs, title, result.TimedOut ? "сервер обновлений не ответил за минуту" : "softwareupdate не ответил");
        var updates = SecurityParsers.SoftwareUpdates(result.StdOut);
        if (updates.Count == 0)
            return Ok("updates", CheckGroup.MacOs, title, "установлены все", "Новых обновлений нет.");

        var current = await MacOsMajorAsync(ct);
        // Updates of this macOS (and security content) matter; a new major version is an offer, not a fix.
        var relevant = updates.Where(u => u.MacOsMajor is null || u.MacOsMajor == current).ToList();
        var security = relevant.Where(u => u.IsSecurity || u.MacOsMajor == current).ToList();
        var list = string.Join(", ", updates.Select(u => u.Version is null ? u.Title : $"{u.Title} {u.Version}"));
        if (security.Count > 0)
        {
            return new SecurityCheck("updates", CheckGroup.MacOs, title, SecurityStatus.Warning, "есть обновления безопасности",
                $"Доступно: {list}. Обновления этой версии macOS закрывают уязвимости — установите их.", IssueSeverity.High)
            {
                SettingsLink = SecurityLinks.SoftwareUpdate, SettingsText = "Открыть «Обновление ПО»",
            };
        }
        return new SecurityCheck("updates", CheckGroup.MacOs, title, SecurityStatus.Info, "есть обновления", $"Доступно: {list}.",
            relevant.Count > 0 ? IssueSeverity.Medium : IssueSeverity.Info)
        {
            SettingsLink = SecurityLinks.SoftwareUpdate, SettingsText = "Открыть «Обновление ПО»",
        };
    }

    /// <summary>The check of the macOS version with the majors `softwareupdate` offers (they may be newer than the code knows).</summary>
    public static SecurityCheck MacOsCheck(int? major, IEnumerable<int> offered)
    {
        const string title = "Версия macOS";
        if (major is not { } current)
            return Unknown("macos", CheckGroup.MacOs, title, "sw_vers не ответил");
        var supported = MacOsVersions.Supported(current, offered);
        var name = MacOsName(current);
        if (supported.Contains(current))
            return Ok("macos", CheckGroup.MacOs, title, name, $"Apple выпускает для неё обновления безопасности (поддерживаются macOS {string.Join(", ", supported)}).");
        return new SecurityCheck("macos", CheckGroup.MacOs, title, SecurityStatus.Warning, $"{name} — устарела",
            $"Apple выпускает исправления уязвимостей только для трёх последних версий macOS ({string.Join(", ", supported)}). Обновите macOS " +
            "в «Обновлении ПО», если Mac это позволяет; если нет — будьте особенно осторожны с программами и сайтами.", IssueSeverity.High)
        {
            SettingsLink = SecurityLinks.SoftwareUpdate, SettingsText = "Открыть «Обновление ПО»",
        };
    }

    private static string MacOsName(int major) => Dashboard.MacOsNames.Marketing(major.ToString(CultureInfo.InvariantCulture));

    private async Task<int?> MacOsMajorAsync(CancellationToken ct)
    {
        var version = (await RunAsync(ct, "/usr/bin/sw_vers", "-productVersion")).StdOut.Trim();
        var dot = version.IndexOf('.');
        return int.TryParse(dot > 0 ? version[..dot] : version, NumberStyles.None, CultureInfo.InvariantCulture, out var major) ? major : null;
    }

    private async Task<SecurityCheck> AutoUpdatesAsync(CancellationToken ct)
    {
        const string title = "Автоматические обновления";
        var plist = await PlistFile.ReadAsync(_paths.UpdatePlist, _runner, ct);
        var prefs = plist.Value is { } values ? SecurityParsers.UpdatePrefs(values) : new UpdatePrefs(true, true, true, false);
        if (prefs.AutomaticCheck && prefs.SecurityResponses)
            return Ok("autoupdate", CheckGroup.MacOs, title, "включены", "macOS сама проверяет обновления и ставит срочные исправления безопасности.");
        var off = new List<string>();
        if (!prefs.AutomaticCheck)
            off.Add("проверка обновлений");
        if (!prefs.SecurityResponses)
            off.Add("установка ответов безопасности и системных файлов");
        return new SecurityCheck("autoupdate", CheckGroup.MacOs, title, SecurityStatus.Warning, "выключены: " + string.Join(", ", off),
            "Срочные исправления безопасности и базы XProtect приходят только с этими настройками. Включите их в «Обновлении ПО» → " +
            "«Автоматические обновления».", IssueSeverity.High)
        {
            SettingsLink = SecurityLinks.SoftwareUpdate, SettingsText = "Открыть «Обновление ПО»",
        };
    }

    private async Task<SecurityCheck> XProtectAsync(CancellationToken ct)
    {
        const string title = "XProtect (встроенная защита от вредоносных программ)";
        var info = SecurityParsers.XProtect((await RunAsync(ct, "/usr/bin/xprotect", "version")).StdOut);
        var installed = info?.InstalledUtc;
        if (installed is null)
        {
            try
            {
                installed = File.Exists(_paths.XProtectPlist) ? File.GetLastWriteTimeUtc(_paths.XProtectPlist) : null;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                installed = null;
            }
        }
        if (installed is not { } date)
            return Unknown("xprotect", CheckGroup.MacOs, title, "не удалось узнать дату баз");
        var version = info?.Version is { } v ? $"версия {v}, " : "";
        var age = _nowUtc() - date;
        if (age <= XProtectMaxAge)
            return Ok("xprotect", CheckGroup.MacOs, title, $"{version}обновлена {Format.Date(date.ToLocalTime())}", "Базы свежие.");
        return new SecurityCheck("xprotect", CheckGroup.MacOs, title, SecurityStatus.Info, $"{version}обновлена {Format.Date(date.ToLocalTime())}",
            $"Базы не обновлялись {(int)age.TotalDays} дн. Обычно Apple обновляет их раз в несколько недель — проверьте, что включена " +
            "«Установка ответов безопасности и системных файлов».", IssueSeverity.Low)
        {
            SettingsLink = SecurityLinks.SoftwareUpdate, SettingsText = "Открыть «Обновление ПО»",
        };
    }

    private async Task<IEnumerable<SecurityCheck>> LoginAsync(CancellationToken ct)
    {
        var plist = await PlistFile.ReadAsync(_paths.LoginWindowPlist, _runner, ct);
        if (plist.Value is not { } values)
        {
            // No file: neither automatic login nor guest was ever turned on.
            return new[]
            {
                Ok("autologin", CheckGroup.Access, "Вход без пароля", "выключен", "При включении Mac нужно ввести пароль."),
                Ok("guest", CheckGroup.Access, "Гостевая учётная запись", "выключена", "Без пароля войти нельзя."),
            };
        }
        var prefs = SecurityParsers.LoginWindow(values);
        return new[]
        {
            prefs.AutoLogin
                ? new SecurityCheck("autologin", CheckGroup.Access, "Вход без пароля", SecurityStatus.Warning, "включён",
                    "Mac входит в вашу учётную запись сам — любой, кто его включит, увидит ваши файлы и почту. Выключите «Автоматический вход» " +
                    "в «Пользователях и группах».", IssueSeverity.High) { SettingsLink = SecurityLinks.Users, SettingsText = "Открыть «Пользователи и группы»" }
                : Ok("autologin", CheckGroup.Access, "Вход без пароля", "выключен", "При включении Mac нужно ввести пароль."),
            prefs.GuestEnabled
                ? new SecurityCheck("guest", CheckGroup.Access, "Гостевая учётная запись", SecurityStatus.Info, "включена",
                    "Кто угодно может войти без пароля. Ваши файлы гостю не видны, но если гость вам не нужен — выключите его.", IssueSeverity.Low)
                    { SettingsLink = SecurityLinks.Users, SettingsText = "Открыть «Пользователи и группы»" }
                : Ok("guest", CheckGroup.Access, "Гостевая учётная запись", "выключена", "Без пароля войти нельзя."),
        };
    }

    private async Task<IEnumerable<SecurityCheck>> SharingAsync(CancellationToken ct)
    {
        var disabled = await RunAsync(ct, "/bin/launchctl", "print-disabled", "system");
        var overrides = disabled.Ok ? LaunchdParsers.ParsePrintDisabled(disabled.StdOut) : new Dictionary<string, bool>();
        var ssh = overrides.TryGetValue("com.openssh.sshd", out var sshDisabled) && !sshDisabled;
        var screen = (overrides.TryGetValue("com.apple.screensharing", out var screenDisabled) && !screenDisabled)
                     || (await RunAsync(ct, "/bin/launchctl", "print", "system/com.apple.screensharing")).Ok;
        SecurityCheck Remote(string id, string title, bool on, string what)
            => on
                ? new SecurityCheck(id, CheckGroup.Access, title, SecurityStatus.Warning, "включён",
                    $"{what} Если вы не пользуетесь этим, выключите в «Общем доступе».", IssueSeverity.Medium)
                    { SettingsLink = SecurityLinks.Sharing, SettingsText = "Открыть «Общий доступ»" }
                : Ok(id, CheckGroup.Access, title, "выключен", "Подключиться к Mac по сети нельзя.");
        return new[]
        {
            Remote("ssh", "Удалённый вход (SSH)", ssh, "К Mac можно подключиться по сети и выполнять команды, зная пароль."),
            Remote("screensharing", "Общий экран", screen, "Ваш экран можно смотреть и управлять Mac по сети."),
        };
    }

    private static IEnumerable<SecurityCheck> ProfileChecks(IReadOnlyList<ConfigProfile> profiles, bool? enrolled, bool read)
    {
        if (!read)
            yield return Unknown("profiles", CheckGroup.Profiles, "Профили конфигурации", "system_profiler не ответил");
        else if (profiles.Count == 0)
            yield return Ok("profiles", CheckGroup.Profiles, "Профили конфигурации", "нет", "Никакие профили не меняют настройки Mac.");

        foreach (var profile in profiles.Where(p => p.IsRisky))
        {
            yield return new SecurityCheck("profile:" + profile.Name, CheckGroup.Profiles, $"Профиль «{profile.Name}»", SecurityStatus.Danger,
                profile.WhatText,
                $"Профиль{(profile.Organization is { } org ? $" от «{org}»" : "")} меняет {profile.WhatText}. Так рекламные программы подменяют " +
                "поиск и читают трафик. Если вы его не ставили — удалите его в «Профилях».", IssueSeverity.High)
            {
                SettingsLink = SecurityLinks.Profiles, SettingsText = "Открыть «Профили»",
            };
        }
        var managed = profiles.Where(p => p.IsMdm).ToList();
        if (managed.Count > 0 || enrolled == true)
        {
            var org = managed.Select(p => p.Organization).FirstOrDefault(o => o is not null);
            yield return new SecurityCheck("mdm", CheckGroup.Profiles, "Управление организацией (MDM)", SecurityStatus.Info,
                org is null ? "Mac управляется организацией" : $"Mac управляется «{org}»",
                "Организация может ставить программы, менять настройки и видеть сведения о Mac. Для рабочего Mac это обычно; если Mac " +
                "личный — узнайте, откуда профиль.", IssueSeverity.Info)
            {
                SettingsLink = SecurityLinks.Profiles, SettingsText = "Открыть «Профили»",
            };
        }
        foreach (var profile in profiles.Where(p => !p.IsRisky && !p.IsMdm))
            yield return new SecurityCheck("profile:" + profile.Name, CheckGroup.Profiles, $"Профиль «{profile.Name}»", SecurityStatus.Ok, profile.WhatText,
                profile.Organization is { } o ? $"Установлен «{o}»." : "Меняет обычные настройки macOS.") { SettingsLink = SecurityLinks.Profiles, SettingsText = "Открыть «Профили»" };
    }

    private static SecurityCheck ProxyCheck(IReadOnlyList<ServiceProxies> proxies, ServiceProxies effective)
    {
        const string title = "Прокси";
        var set = proxies.Where(p => p.Any).ToList();
        if (set.Count == 0 && !effective.Any)
            return Ok("proxy", CheckGroup.Network, title, "не используется", "Сайты открываются напрямую.");
        var where = set.Count > 0 ? string.Join("; ", set.Select(p => $"{p.Service}: {p.Text}")) : "задан профилем или программой: " + effective.Text;
        return new SecurityCheck("proxy", CheckGroup.Network, title, SecurityStatus.Warning, "включён",
            $"{where}. Через прокси идут все сайты — его владелец видит, куда вы заходите, и может подменять страницы. Так делают " +
            "рекламные программы; ещё так делают VPN и корпоративные программы. Если вы его не включали — выключите.", IssueSeverity.High)
        {
            Fix = set.Count > 0 ? FixKind.Proxy : FixKind.None,
            FixText = "Выключить прокси",
            SettingsLink = SecurityLinks.Network, SettingsText = "Открыть «Сеть»",
        };
    }

    private static SecurityCheck DnsCheck(IReadOnlyList<(string Service, IReadOnlyList<string> Servers)> manual)
    {
        const string title = "DNS";
        var unexpected = manual.SelectMany(m => m.Servers.Where(DnsRules.IsUnexpected).Select(s => (m.Service, Server: s))).ToList();
        if (unexpected.Count == 0)
        {
            var known = manual.SelectMany(m => m.Servers).Select(s => DnsRules.KnownName(s) ?? s).Distinct().ToList();
            return Ok("dns", CheckGroup.Network, title, known.Count == 0 ? "от роутера или провайдера" : string.Join(", ", known),
                "Адреса сайтов узнаются у знакомого сервера.");
        }
        return new SecurityCheck("dns", CheckGroup.Network, title, SecurityStatus.Warning, "задан вручную незнакомый сервер",
            $"{string.Join("; ", unexpected.Select(u => $"{u.Service}: {u.Server}"))}. Этот сервер решает, какой сайт откроется по адресу, — " +
            "так вирусы подменяют страницы банков. Если вы его не задавали — верните автоматический DNS.", IssueSeverity.Medium)
        {
            Fix = FixKind.Dns, FixText = "Вернуть автоматический DNS",
            SettingsLink = SecurityLinks.Network, SettingsText = "Открыть «Сеть»",
        };
    }

    private (string? Text, IReadOnlyList<HostsEntry> Entries, SecurityCheck Check) ReadHosts()
    {
        const string title = "Файл hosts";
        string text;
        try
        {
            text = File.ReadAllText(_paths.Hosts);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return (null, Array.Empty<HostsEntry>(), Unknown("hosts", CheckGroup.Network, title, ErrorText.For(ex)));
        }
        var entries = HostsFile.Read(text);
        var bad = entries.Where(e => e.Risk is SecurityStatus.Warning or SecurityStatus.Danger).ToList();
        if (bad.Count == 0)
            return (text, entries, Ok("hosts", CheckGroup.Network, title, "в порядке", "Сайты не перенаправлены и не заблокированы опасным образом."));
        var danger = bad.Any(e => e.Risk == SecurityStatus.Danger);
        return (text, entries, new SecurityCheck("hosts", CheckGroup.Network, title, danger ? SecurityStatus.Danger : SecurityStatus.Warning,
            $"подозрительных строк: {bad.Count}", string.Join(" ", bad.Select(e => e.Reason).Distinct()), IssueSeverity.High)
        {
            Fix = FixKind.Hosts, FixText = "Закомментировать отмеченные строки",
        });
    }

    private static SecurityCheck WifiCheck(WifiSecurity wifi) => wifi switch
    {
        WifiSecurity.Open => new SecurityCheck("wifi", CheckGroup.Network, "Wi-Fi", SecurityStatus.Warning, "открытая сеть без пароля",
            "Всё, что передаётся без HTTPS, могут прочитать соседи по сети. Не вводите пароли на сайтах без замочка и по возможности " +
            "пользуйтесь своей сетью или VPN.", IssueSeverity.Medium),
        WifiSecurity.Wep => new SecurityCheck("wifi", CheckGroup.Network, "Wi-Fi", SecurityStatus.Danger, "устаревшая защита WEP",
            "WEP взламывается за минуты. Включите в настройках роутера WPA2 или WPA3.", IssueSeverity.High),
        WifiSecurity.Protected => Ok("wifi", CheckGroup.Network, "Wi-Fi", "сеть защищена паролем", "Трафик в сети зашифрован."),
        _ => Ok("wifi", CheckGroup.Network, "Wi-Fi", "не подключён", "Mac не подключён к Wi-Fi."),
    };

    private Task<CommandResult> RunAsync(CancellationToken ct, string exe, params string[] args) => _runner.RunAsync(exe, args, null, ct);

    private static SecurityCheck Ok(string id, CheckGroup group, string title, string summary, string details)
        => new(id, group, title, SecurityStatus.Ok, summary, details);

    private static SecurityCheck Unknown(string id, CheckGroup group, string title, string reason)
        => new(id, group, title, SecurityStatus.Unknown, "нет данных", $"Не удалось проверить: {reason}.");
}
