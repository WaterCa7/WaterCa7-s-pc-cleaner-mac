using PcCleaner.Core;

namespace PcCleaner.Modules.Security;

/// <summary>A check on the page: the status picture and the buttons it may have.</summary>
public sealed class CheckItem
{
    public CheckItem(SecurityCheck check, Func<CheckItem, Task> fix, Func<CheckItem, Task> openSettings)
    {
        Check = check;
        FixCommand = new AsyncRelayCommand(() => fix(this));
        SettingsCommand = new AsyncRelayCommand(() => openSettings(this));
    }

    public SecurityCheck Check { get; }

    public string Title => Check.Title;

    public string Summary => Check.Summary;

    public string Details => Check.Details;

    public string IconName => Check.Status switch
    {
        SecurityStatus.Ok => "ShieldCheckmark",
        SecurityStatus.Info => "Info",
        SecurityStatus.Warning => "Warning",
        SecurityStatus.Danger => "Error",
        _ => "Question",
    };

    public string BrushKey => Check.Status switch
    {
        SecurityStatus.Ok => "SuccessBrush",
        SecurityStatus.Info => "InfoBrush",
        SecurityStatus.Warning => "WarningBrush",
        SecurityStatus.Danger => "DangerBrush",
        _ => "MutedBrush",
    };

    public bool IsOk => Check.Status == SecurityStatus.Ok;

    public bool CanFix => Check.Fix != FixKind.None && !IsOk;

    public string FixText => Check.FixText ?? "Исправить";

    public bool HasSettings => Check.SettingsLink is not null;

    public string SettingsText => Check.SettingsText ?? "Открыть настройки";

    public AsyncRelayCommand FixCommand { get; }

    public AsyncRelayCommand SettingsCommand { get; }
}

/// <summary>Checks of one group with a heading.</summary>
public sealed class CheckGroupItem
{
    public CheckGroupItem(CheckGroup group, IReadOnlyList<CheckItem> items)
    {
        Group = group;
        Items = items;
    }

    public CheckGroup Group { get; }

    public IReadOnlyList<CheckItem> Items { get; }

    public string Title => Group switch
    {
        CheckGroup.MacOs => "Защита macOS",
        CheckGroup.Access => "Вход и общий доступ",
        CheckGroup.Profiles => "Профили конфигурации",
        _ => "Сеть",
    };
}
