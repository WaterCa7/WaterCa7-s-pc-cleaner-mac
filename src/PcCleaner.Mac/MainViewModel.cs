using System.Collections.ObjectModel;
using PcCleaner.Core;

namespace PcCleaner;

/// <summary>The window: the page list, navigation and the status line.</summary>
public sealed class MainViewModel : ObservableObject, INavigator
{
    private PageViewModel? _selectedPage;
    private string _statusText = "Готово";

    public MainViewModel(IReadOnlyList<PageViewModel> pages)
    {
        Pages = new ObservableCollection<PageViewModel>(pages);
        Log.EntryAdded += OnLogEntry;
        if (Pages.Count > 0)
            SelectedPage = Pages[0];
    }

    public ObservableCollection<PageViewModel> Pages { get; }

    IEnumerable<PageViewModel> INavigator.Pages => Pages;

    public PageViewModel? SelectedPage
    {
        get => _selectedPage;
        set
        {
            if (value is not null && !ReferenceEquals(value, _selectedPage))
                _ = NavigateAsync(value);
        }
    }

    public string StatusText
    {
        get => _statusText;
        private set => Set(ref _statusText, value);
    }

    public string VersionText => "v" + AppInfo.Version;

    /// <summary>The status line shows the latest log entry (log entries arrive on the UI thread).</summary>
    internal void OnLogEntry(LogEntry entry) => StatusText = entry.Message;

    public void NavigateTo(string pageKey)
    {
        var page = Pages.FirstOrDefault(p => string.Equals(p.Key, pageKey, StringComparison.OrdinalIgnoreCase));
        if (page is not null)
            SelectedPage = page;
    }

    private async Task NavigateAsync(PageViewModel page)
    {
        _selectedPage?.OnNavigatedFrom();
        _selectedPage = page;
        OnPropertyChanged(nameof(SelectedPage));
        try
        {
            await page.OnNavigatedToAsync();
        }
        catch (Exception ex)
        {
            Log.Error($"{page.Title}: не удалось открыть страницу", ex);
        }
    }
}

/// <summary>Lets pages created before the window navigate once it exists.</summary>
public sealed class DeferredNavigator : INavigator
{
    public INavigator? Target { get; set; }

    public IEnumerable<PageViewModel> Pages => Target?.Pages ?? Array.Empty<PageViewModel>();

    public void NavigateTo(string pageKey) => Target?.NavigateTo(pageKey);
}
