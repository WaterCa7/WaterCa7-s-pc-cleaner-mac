using System.Globalization;
using PcCleaner.Core;

namespace PcCleaner.Modules.Startup;

/// <summary>
/// Undoes a change recorded in the backups: a disabled or removed startup item starts again (the plist goes back to its
/// place when it is gone), an item that was enabled is disabled again, a login item is added back. System folders and
/// daemons only through the macOS password window.
/// </summary>
public sealed class BackupRestorer
{
    private const string Launchctl = "/bin/launchctl";
    private const int NotFound = 113;

    private readonly BackupStore _store;
    private readonly string _home;
    private readonly int _uid;
    private readonly ICommandRunner _runner;
    private readonly AdminPrompt _admin;
    private readonly LoginItemsService _loginItems;

    public BackupRestorer(BackupStore store, string home, int uid, ICommandRunner runner, AdminPrompt admin, LoginItemsService loginItems)
    {
        _store = store;
        _home = home;
        _uid = uid;
        _runner = runner;
        _admin = admin;
        _loginItems = loginItems;
    }

    public BackupStore Store => _store;

    /// <summary>What happened before the copy was made, for the list: "отключена", "удалена"…</summary>
    public static string Describe(BackupRecord record) => record.Kind == BackupKind.LoginItem
        ? "убран из объектов входа"
        : record.Details.GetValueOrDefault("action") switch
        {
            "удаление" => "запись удалена",
            "включение" => "запись включена",
            _ => "запись отключена",
        };

    /// <summary>What «Восстановить» will do, for the confirmation.</summary>
    public static string WhatRestoreDoes(BackupRecord record) => record.Kind == BackupKind.LoginItem
        ? "Программа снова будет открываться при входе."
        : record.Details.GetValueOrDefault("action") == "включение"
            ? "Запись снова будет отключена."
            : "Запись вернётся на место и снова будет запускаться при входе.";

    public async Task<StartupResult> RestoreAsync(BackupRecord record, CancellationToken ct)
    {
        if (record.Kind == BackupKind.LoginItem)
        {
            var path = record.Details.GetValueOrDefault("path") ?? record.OriginalPath;
            return path is null
                ? new StartupResult(false, "В копии нет пути к программе — восстановить нельзя.")
                : await _loginItems.AddAsync(path, record.Details.GetValueOrDefault("hidden") == "true", ct);
        }

        var label = record.Details.GetValueOrDefault("label");
        var domain = record.Details.GetValueOrDefault("domain");
        var original = record.OriginalPath;
        if (string.IsNullOrEmpty(label) || string.IsNullOrEmpty(domain) || original is null)
            return new StartupResult(false, "В копии нет метки или пути — восстановить её нельзя.");
        var target = $"{domain}/{label}";
        var system = domain == "system";
        var loaded = (await RunAsync(ct, "print", target)).ExitCode != NotFound;

        if (record.Details.GetValueOrDefault("action") == "включение")
            return await DisableAgainAsync(label, target, system, loaded, ct);

        var commands = new List<AdminCommand>();
        var needsAdmin = system;
        if (!File.Exists(original))
        {
            if (record.StoredFile is null || !File.Exists(record.StoredFile))
                return new StartupResult(false, "Файл копии не найден — восстановить нельзя.");
            if (IsInHome(original))
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(original)!);
                    File.Copy(record.StoredFile, original);
                    Log.Action($"Восстановлен {original} из копии {record.StoredFile}");
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    return new StartupResult(false, $"Не удалось вернуть файл на место: {ErrorText.For(ex)}");
                }
            }
            else
            {
                // /Library belongs to the system: the copy goes back with the owner and rights launchd expects.
                needsAdmin = true;
                commands.Add(new AdminCommand("/bin/cp", record.StoredFile, original));
                commands.Add(new AdminCommand("/usr/sbin/chown", "root:wheel", original));
                commands.Add(new AdminCommand("/bin/chmod", "644", original));
            }
        }

        var domainOnly = system ? "system" : GuiDomain;
        if (needsAdmin)
        {
            commands.Add(new AdminCommand(Launchctl, "enable", target));
            if (!loaded)
                commands.Add(new AdminCommand(Launchctl, "bootstrap", domainOnly, original));
            var result = await _admin.RunAsync($"Восстановить «{label}» в автозагрузке", commands, ct);
            return result.Ok
                ? new StartupResult(true, $"«{label}» восстановлена.")
                : new StartupResult(false, result.Cancelled ? "Отменено — ничего не изменено." : $"Не удалось восстановить «{label}»: {result.Error}");
        }

        var enable = await RunAsync(ct, "enable", target);
        if (!enable.Ok)
            return new StartupResult(false, $"Не удалось включить «{label}»: {FirstLine(enable)}");
        Log.Action($"Автозагрузка: восстановлено {target}");
        if (!loaded && !(await RunAsync(ct, "bootstrap", domainOnly, original)).Ok)
            return new StartupResult(true, $"«{label}» восстановлена и запустится при следующем входе.");
        return new StartupResult(true, $"«{label}» восстановлена.");
    }

    private async Task<StartupResult> DisableAgainAsync(string label, string target, bool system, bool loaded, CancellationToken ct)
    {
        if (system)
        {
            var commands = new List<AdminCommand> { new(Launchctl, "disable", target) };
            if (loaded)
                commands.Add(new AdminCommand(Launchctl, "bootout", target));
            var result = await _admin.RunAsync($"Снова отключить «{label}»", commands, ct);
            return result.Ok
                ? new StartupResult(true, $"«{label}» снова отключена.")
                : new StartupResult(false, result.Cancelled ? "Отменено — ничего не изменено." : $"Не удалось отключить «{label}»: {result.Error}");
        }
        var disable = await RunAsync(ct, "disable", target);
        if (!disable.Ok)
            return new StartupResult(false, $"Не удалось отключить «{label}»: {FirstLine(disable)}");
        Log.Action($"Автозагрузка: снова отключено {target}");
        if (loaded)
            await RunAsync(ct, "bootout", target);
        return new StartupResult(true, $"«{label}» снова отключена.");
    }

    private string GuiDomain => "gui/" + _uid.ToString(CultureInfo.InvariantCulture);

    private bool IsInHome(string path)
    {
        var home = _home.TrimEnd('/', '\\');
        return path.StartsWith(home + "/", StringComparison.Ordinal) || path.StartsWith(home + "\\", StringComparison.Ordinal);
    }

    private Task<CommandResult> RunAsync(CancellationToken ct, params string[] args) => _runner.RunAsync(Launchctl, args, null, ct);

    private static string FirstLine(CommandResult result)
        => (result.StdErr.Length > 0 ? result.StdErr : result.StdOut).Trim().Split('\n')[0].Trim() is { Length: > 0 } line ? line : $"код {result.ExitCode}";
}
