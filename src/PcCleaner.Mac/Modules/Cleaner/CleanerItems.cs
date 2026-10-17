using PcCleaner.Core;

namespace PcCleaner.Modules.Cleaner;

/// <summary>A category row of the «Очистка» tab.</summary>
public sealed class CategoryItem : ObservableObject
{
    private readonly Action _selectionChanged;
    private bool _isChecked;
    private bool _showDetails;
    private CategoryScan? _scan;

    public CategoryItem(CleanerCategory category, bool isChecked, Action selectionChanged)
    {
        Category = category;
        _isChecked = isChecked;
        _selectionChanged = selectionChanged;
        ToggleDetailsCommand = new RelayCommand(() => ShowDetails = !ShowDetails);
    }

    public bool ShowDetails
    {
        get => _showDetails;
        set
        {
            if (Set(ref _showDetails, value))
                OnPropertyChanged(nameof(DetailsButtonText));
        }
    }

    public string DetailsButtonText => ShowDetails ? "Скрыть подробности" : "Подробности";

    public RelayCommand ToggleDetailsCommand { get; }

    public CleanerCategory Category { get; }

    public string Title => Category.Title;

    public string Description => Category.Description;

    public bool IsChecked
    {
        get => _isChecked;
        set
        {
            if (Set(ref _isChecked, value))
                _selectionChanged();
        }
    }

    public long Bytes => _scan?.Bytes ?? 0;

    public string SizeText => _scan is null ? "—" : _scan.State == ScanState.NeedsFullDiskAccess ? "?" : Format.Bytes(Bytes);

    public bool NeedsFullDiskAccess => _scan?.State == ScanState.NeedsFullDiskAccess;

    /// <summary>Why something is not counted: no access, open programs, files that are only shown.</summary>
    public string StateText
    {
        get
        {
            if (_scan is null)
                return "ещё не проверено";
            var parts = new List<string>();
            switch (_scan.State)
            {
                case ScanState.NeedsFullDiskAccess:
                    parts.Add("нужен полный доступ к диску");
                    break;
                case ScanState.Error:
                    parts.Add(_scan.Note ?? "не удалось проверить");
                    break;
                case ScanState.Empty:
                    parts.Add("нечего удалять");
                    break;
                default:
                    parts.Add(Format.Count(_scan.FileCount, "файл", "файла", "файлов"));
                    break;
            }
            if (_scan.SkippedBytes > 0)
                parts.Add($"пропущено {Format.Bytes(_scan.SkippedBytes)}: " + string.Join("; ", _scan.SkipNotes));
            if (_scan.ShowOnlyBytes > 0)
                parts.Add($"ещё {Format.Bytes(_scan.ShowOnlyBytes)} только показываются");
            if (_scan.State is ScanState.Ready or ScanState.Empty && _scan.Note is { } note)
                parts.Add(note.TrimEnd('.'));
            return string.Join(" · ", parts);
        }
    }

    public IReadOnlyList<TargetLine> Details
        => _scan?.Targets.Where(t => t.Files.Count > 0)
               .GroupBy(t => t.Title)
               .Select(g => new TargetLine(g.Key, Format.Bytes(g.Sum(t => t.Bytes)), g.First().SkipReason))
               .ToList()
           ?? (IReadOnlyList<TargetLine>)Array.Empty<TargetLine>();

    public bool HasDetails => Details.Count > 0;

    public void Update(CategoryScan scan)
    {
        _scan = scan;
        foreach (var name in new[] { nameof(Bytes), nameof(SizeText), nameof(StateText), nameof(Details), nameof(HasDetails), nameof(NeedsFullDiskAccess) })
            OnPropertyChanged(name);
    }
}

/// <summary>A folder inside a category with its size; skipped ones say why.</summary>
public sealed record TargetLine(string Title, string SizeText, string? SkipReason)
{
    public bool HasSkipReason => SkipReason is not null;
}

public sealed record VolumeItem(string Name, string SizeText, double UsedPercent, string? PurgeableText)
{
    public bool HasPurgeable => PurgeableText is not null;

    public static VolumeItem From(VolumeInfo v) => new(
        v.Name,
        $"Свободно {Format.Bytes(v.FreeBytes)} из {Format.Bytes(v.TotalBytes)}",
        Math.Round(v.UsedPercent, 1),
        v.PurgeableBytes > 0 ? $"Ещё {Format.Bytes(v.PurgeableBytes)} macOS может освободить сама, когда понадобится место" : null);
}

public sealed class FolderItem
{
    public FolderItem(FolderSize folder, long biggest, Func<string, Task> reveal)
    {
        Name = folder.Name;
        SizeText = folder.Denied ? "нет доступа" : Format.Bytes(folder.Bytes);
        Percent = biggest > 0 ? Math.Round(100.0 * folder.Bytes / biggest, 1) : 0;
        RevealCommand = new AsyncRelayCommand(() => reveal(folder.Path));
    }

    public string Name { get; }

    public string SizeText { get; }

    public double Percent { get; }

    public AsyncRelayCommand RevealCommand { get; }
}

public sealed class UserItemRow
{
    public UserItemRow(UserItem item, string home, Func<string, Task> reveal, Func<UserItemRow, Task> moveToTrash)
    {
        Item = item;
        var folder = Path.GetDirectoryName(item.Path) ?? "";
        Folder = folder.StartsWith(home, StringComparison.Ordinal) ? "~" + folder[home.Length..] : folder;
        RevealCommand = new AsyncRelayCommand(() => reveal(item.Path));
        TrashCommand = new AsyncRelayCommand(() => moveToTrash(this));
    }

    public UserItem Item { get; }

    public string Name => Item.Name;

    public string Folder { get; }

    public string SizeText => Format.Bytes(Item.Bytes);

    public string DateText => Format.Date(Item.LastWriteUtc.ToLocalTime());

    public AsyncRelayCommand RevealCommand { get; }

    public AsyncRelayCommand TrashCommand { get; }
}

public sealed record ThresholdOption(string Text, long Bytes)
{
    public override string ToString() => Text;
}
