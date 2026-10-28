using System.Collections.ObjectModel;
using System.ComponentModel;
using PcCleaner.Core;
using PcCleaner.Core.Updates;

namespace PcCleaner;

/// <summary>The window: the page list, navigation, the update strip and the bottom line.</summary>
public sealed class MainViewModel : ObservableObject, INavigator
{
    private PageViewModel? _selectedPage;
    private UpdateService? _updates;
    private bool _updateBannerDismissed;

    public MainViewModel(IReadOnlyList<PageViewModel> pages)
    {
        Pages = new ObservableCollection<PageViewModel>(pages);
        UpdateBannerCommand = new AsyncRelayCommand(RunUpdateBannerActionAsync);
        DismissUpdateBannerCommand = new RelayCommand(() =>
        {
            _updateBannerDismissed = true;
            OnUpdateBannerChanged();
        });
        if (Pages.Count > 0)
            SelectedPage = Pages[0];
    }

    /// <summary>The updater (null in tests and the screenshot mode): its state drives the strip above the page.</summary>
    public UpdateService? Updates
    {
        get => _updates;
        set
        {
            if (_updates is not null)
                _updates.PropertyChanged -= OnUpdatesChanged;
            _updates = value;
            if (value is not null)
                value.PropertyChanged += OnUpdatesChanged;
            OnUpdateBannerChanged();
        }
    }

    /// <summary>«Перезапустить сейчас»: installs the staged update and quits (provided by the app).</summary>
    public Func<Task>? InstallUpdateNow { get; set; }

    /// <summary>Opens a web page (provided by the app).</summary>
    public Action<string>? OpenUrl { get; set; }

    private UpdateBanner? Banner => _updateBannerDismissed || Updates is null ? null : UpdateBanner.For(Updates.State, Updates.Manifest, Updates.CurrentVersion);

    public bool ShowUpdateBanner => Banner is not null;

    public string UpdateBannerText => Banner?.Text ?? "";

    public string UpdateBannerAction => Banner?.Action ?? "";

    public AsyncRelayCommand UpdateBannerCommand { get; }

    public RelayCommand DismissUpdateBannerCommand { get; }

    private void OnUpdatesChanged(object? sender, PropertyChangedEventArgs e) => Ui.Post(OnUpdateBannerChanged);

    private void OnUpdateBannerChanged()
    {
        OnPropertyChanged(nameof(ShowUpdateBanner));
        OnPropertyChanged(nameof(UpdateBannerText));
        OnPropertyChanged(nameof(UpdateBannerAction));
        OnPropertyChanged(nameof(FooterText));
    }

    private async Task RunUpdateBannerActionAsync()
    {
        if (Updates is null)
            return;
        switch (Updates.State)
        {
            case UpdateState.Ready:
                var busy = Pages.Where(p => p.IsBusy).Select(p => p.Title).ToList();
                if (busy.Count > 0 && !await Dialogs.ConfirmAsync("Установка обновления", UpdateBanner.BusyWarning(busy)))
                    return;
                if (InstallUpdateNow is not null)
                    await InstallUpdateNow();
                break;
            case UpdateState.Updated:
                await Dialogs.InfoAsync($"Что нового в версии {Updates.CurrentVersion.ToString(3)}",
                    string.IsNullOrWhiteSpace(Updates.Manifest?.Notes) ? "Список изменений — на странице релизов программы." : Updates.Manifest!.Notes);
                break;
            case UpdateState.ManualRequired:
                OpenUrl?.Invoke(UpdateService.ReleasesUrl);
                break;
        }
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

    /// <summary>The corner of the bottom line: «v1.2.0 · последняя версия» (<see cref="UpdateFooter"/>); the version alone without an updater.</summary>
    public string FooterText => Updates is null ? "v" + AppInfo.Version : UpdateFooter.Text(Updates.State, Updates.Manifest, Updates.CurrentVersion);

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
