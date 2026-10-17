using System.Globalization;
using PcCleaner.Core;
using PcCleaner.Modules.Startup;

namespace PcCleaner.Modules.Processes;

/// <summary>A sort order of the process list.</summary>
public sealed record ProcessSort(string Text, Func<IEnumerable<ProcessRow>, IEnumerable<ProcessRow>> Apply)
{
    public override string ToString() => Text;
}

/// <summary>A row of the «Процессы» tab (and the details of the selected one).</summary>
public sealed class ProcessRow
{
    public ProcessRow(ProcessEntry entry, int ownUid)
    {
        Entry = entry;
        UserText = entry.Info.Uid == ownUid ? "вы" : entry.Info.Uid == 0 ? "root (система)" : $"служебный (uid {entry.Info.Uid.ToString(CultureInfo.InvariantCulture)})";
    }

    public ProcessEntry Entry { get; }

    public string Name => Entry.Info.Name;

    public string PathText => Entry.Info.HasPath ? Entry.Info.Executable : "путь неизвестен";

    public string CommandLine => Entry.Info.CommandLine ?? Entry.Info.Command;

    public string CpuText => Format.Percent(Entry.Info.CpuPercent);

    public string MemoryText => Format.Bytes(Entry.Info.MemoryBytes);

    public string UserText { get; }

    public string DetailsText => $"PID {Entry.Info.Pid.ToString(CultureInfo.InvariantCulture)} · пользователь: {UserText} · работает {Format.Duration(Entry.Info.Elapsed)}";

    public string SignatureText => Entry.Signature is { } s ? "Подпись: " + s.Text : Entry.IsSystem ? "Часть macOS" : "Подпись не проверялась";

    public IReadOnlyList<Hint> Hints => Entry.Hints;

    public bool HasHints => Entry.Hints.Count > 0;

    public bool HasStrongHint => Entry.HasStrongHint;

    public bool CanTerminate => Entry.Protected is null;

    public string ProtectedText => Entry.Protected is { } why ? "Завершить нельзя: " + why : "";

    public bool IsProtected => Entry.Protected is not null;

    public bool HasLocation => Entry.Location is not null;

    public bool CanHash => Entry.Info.HasPath;
}

/// <summary>A row of the «Автозагрузка» tab.</summary>
public sealed class StartupRow
{
    public StartupRow(LaunchEntry entry, Func<StartupRow, Task> toggle, Func<StartupRow, Task> remove, Func<StartupRow, Task> reveal)
    {
        Entry = entry;
        ToggleCommand = new AsyncRelayCommand(() => toggle(this));
        RemoveCommand = new AsyncRelayCommand(() => remove(this));
        RevealCommand = new AsyncRelayCommand(() => reveal(this));
    }

    public LaunchEntry Entry { get; }

    public string Label => Entry.Label;

    public string ProgramText => Entry.CommandLine is { Length: > 0 } line ? line : "программа не указана";

    public string StateText
    {
        get
        {
            if (Entry.Disabled)
                return "Отключена";
            return Entry.State switch
            {
                null => Entry.RunAtLoad ? "Запускается при входе" : "Запускается по событию",
                { Running: true, Pid: { } pid } => $"Работает (PID {pid.ToString(CultureInfo.InvariantCulture)})",
                { Running: true } => "Работает",
                { Loaded: true, LastExit: { } code and not 0 } => $"Загружена, последний запуск завершился с кодом {code.ToString(CultureInfo.InvariantCulture)}",
                { Loaded: true } => "Загружена, сейчас не работает",
                _ => "Не загружена",
            };
        }
    }

    public string SignatureText => Entry.Signature is { } s ? "Подпись: " + s.Text : Entry.ReadOnly ? "Часть macOS" : "";

    public bool HasSignature => SignatureText.Length > 0;

    public string ProblemText => Entry.Problem is { } problem ? "Сломана: " + problem : "";

    public bool IsBroken => Entry.IsBroken;

    public IReadOnlyList<Hint> Hints => Entry.Hints;

    public bool HasHints => Entry.Hints.Count > 0;

    public bool HasStrongHint => Entry.HasStrongHint;

    public bool CanToggle => !Entry.ReadOnly && Entry.HasLabel;

    public string ToggleText => Entry.Disabled ? "Включить" : "Отключить";

    public bool CanRemove => Entry.CanRemove;

    public bool HasPlist => Entry.PlistPath is not null;

    public AsyncRelayCommand ToggleCommand { get; }

    public AsyncRelayCommand RemoveCommand { get; }

    public AsyncRelayCommand RevealCommand { get; }
}

/// <summary>Startup items of one source, with a heading.</summary>
public sealed class StartupGroup
{
    public StartupGroup(LaunchSource source, IReadOnlyList<StartupRow> rows)
    {
        Source = source;
        Rows = rows;
    }

    public LaunchSource Source { get; }

    public IReadOnlyList<StartupRow> Rows { get; }

    public string Title => Source switch
    {
        LaunchSource.UserAgent => $"Ваши агенты ({Rows.Count})",
        LaunchSource.GlobalAgent => $"Агенты для всех пользователей ({Rows.Count})",
        LaunchSource.Daemon => $"Системные службы ({Rows.Count})",
        LaunchSource.AppBundled => $"Встроены в программы ({Rows.Count})",
        _ => $"macOS ({Rows.Count})",
    };

    public string Hint => Source switch
    {
        LaunchSource.UserAgent => "~/Library/LaunchAgents — запускаются при вашем входе. Свои записи можно удалить (файл уйдёт в Корзину).",
        LaunchSource.GlobalAgent => "/Library/LaunchAgents — ставят программы для всех пользователей. Отключение действует только для вас.",
        LaunchSource.Daemon => "/Library/LaunchDaemons — работают от имени системы ещё до входа. Отключение спросит пароль администратора.",
        LaunchSource.AppBundled => "Программы зарегистрировали их сами (SMAppService). Управлять ими удобнее в «Объектах входа» Системных настроек.",
        _ => "Записи самой macOS — только для просмотра.",
    };
}

/// <summary>A row of «Объекты входа».</summary>
public sealed class LoginItemRow
{
    public LoginItemRow(LoginItem item, Func<LoginItemRow, Task> remove)
    {
        Item = item;
        RemoveCommand = new AsyncRelayCommand(() => remove(this));
    }

    public LoginItem Item { get; }

    public string Name => Item.Name;

    public string PathText => Item.Hidden ? $"{Item.Path} · открывается скрытой" : Item.Path;

    public AsyncRelayCommand RemoveCommand { get; }
}
