using PcCleaner.Core;

namespace PcCleaner.Modules.Programs;

public enum AppFilter
{
    All,
    LongUnused,
    IntelOnly,
    Unwanted,
    AppStore,
}

/// <summary>A choice of a combo box: the text and the value.</summary>
public sealed record Choice<T>(string Text, T Value)
{
    public override string ToString() => Text;
}

/// <summary>A row of the program list; the size arrives later (it is measured in the background).</summary>
public sealed class AppRow : ObservableObject
{
    private long? _size;

    public AppRow(InstalledApp app, DateTime nowUtc)
    {
        App = app;
        IsLongUnused = ProgramsRules.IsLongUnused(app, nowUtc);
    }

    public InstalledApp App { get; }

    public string Name => App.Name;

    public string SubText => App.Version is null ? App.BundleId ?? App.Path : $"{App.Version} · {App.BundleId ?? App.Path}";

    public bool IsLongUnused { get; }

    public long? Size
    {
        get => _size;
        set
        {
            if (Set(ref _size, value))
            {
                OnPropertyChanged(nameof(SizeText));
                OnPropertyChanged(nameof(UsageText));
            }
        }
    }

    public string SizeText => Size is { } bytes ? Format.Bytes(bytes) : "…";

    public string LastUsedText => App.LastUsedUtc is { } used ? Format.Date(used.ToLocalTime()) : "нет сведений";

    public bool IsPup => App.IsPup;

    public bool IsIntelOnly => App.Architecture == AppArchitecture.IntelOnly;

    public bool IsAppStore => App.IsAppStore;

    public bool CanUninstall => !App.IsProtected;

    public string PathText => App.Path;

    public string DetailsText => $"Версия {App.VersionText} · {App.BundleId ?? "без идентификатора"}";

    public string SourceText => "Источник и подпись: " + App.SourceText;

    public string ArchitectureText => "Процессор: " + App.ArchitectureText;

    public string UsageText => $"Размер: {SizeText} · последний запуск: {LastUsedText}";

    public string ProtectedText => App.IsApple ? "Встроена в macOS — удалить нельзя." : App.IsProtected ? "Это сама программа очистки." : "";

    public string UninstallerText => App.Uninstaller is { } path ? $"Есть свой деинсталлятор: {System.IO.Path.GetFileNameWithoutExtension(path)}" : "";

    public bool HasUninstaller => App.Uninstaller is not null && !App.IsProtected;
}

/// <summary>A leftover of the selected app with a check box.</summary>
public sealed class LeftoverRow : ObservableObject
{
    private bool _isChecked;

    public LeftoverRow(Leftover leftover, string home, Action changed)
    {
        Leftover = leftover;
        _isChecked = leftover.Checked;
        var library = System.IO.Path.Combine(home, "Library");
        PathText = leftover.Path.StartsWith(library, StringComparison.Ordinal) ? "~/Library" + leftover.Path[library.Length..].Replace('\\', '/') : leftover.Path;
        Changed = changed;
    }

    private Action Changed { get; }

    public Leftover Leftover { get; }

    public bool IsChecked
    {
        get => _isChecked;
        set
        {
            if (Set(ref _isChecked, value))
                Changed();
        }
    }

    public string What => Leftover.What;

    public string PathText { get; }

    public string SizeText => Format.Bytes(Leftover.Bytes);

    public string? Note => Leftover.Note;

    public bool HasNote => !string.IsNullOrEmpty(Leftover.Note);

    /// <summary>The leftover as it goes to removal (ticked or not, as the user left it).</summary>
    public Leftover Current => Leftover with { Checked = IsChecked };
}
