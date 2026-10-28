using System.Collections.ObjectModel;
using System.ComponentModel;
using PcCleaner.Core;
using PcCleaner.Core.Updates;
using PcCleaner.Shell;

namespace PcCleaner;

/// <summary>The window: the sections of the sidebar and their tabs, navigation, the update strip and the bottom line.</summary>
public sealed class MainViewModel : ObservableObject, INavigator
{
    private PageViewModel? _selectedPage;
    private SectionViewModel? _selectedSection;
    private UpdateService? _updates;
    private bool _updateBannerDismissed;

    public MainViewModel(IReadOnlyList<PageViewModel> pages)
    {
        Pages = new ObservableCollection<PageViewModel>(pages);
        Sections = ShellMenu.Build(Pages);
        TopSections = Sections.Where(s => s.Place == SectionPlace.Top).ToList();
        BottomSections = Sections.Where(s => s.Place == SectionPlace.Bottom).ToList();
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

    /// <summary>Every section of the menu (<see cref="ShellMenu"/>); a section's tabs are pages of <see cref="Pages"/>.</summary>
    public IReadOnlyList<SectionViewModel> Sections { get; }

    /// <summary>The main list of the sidebar.</summary>
    public IReadOnlyList<SectionViewModel> TopSections { get; }

    /// <summary>The list at the bottom of the sidebar (Профиль, Настройки).</summary>
    public IReadOnlyList<SectionViewModel> BottomSections { get; }

    /// <summary>The section of the open page.</summary>
    public SectionViewModel? SelectedSection => _selectedSection;

    /// <summary>The selection of the main list (null while a bottom section is open); a write opens the section, null is ignored.</summary>
    public SectionViewModel? SelectedTopSection
    {
        get => SelectedSection is { Place: SectionPlace.Top } s ? s : null;
        set => Open(value);
    }

    /// <summary>The selection of the bottom list; see <see cref="SelectedTopSection"/>.</summary>
    public SectionViewModel? SelectedBottomSection
    {
        get => SelectedSection is { Place: SectionPlace.Bottom } s ? s : null;
        set => Open(value);
    }

    /// <summary>The title above the page: the section when it has tabs, else the page itself.</summary>
    public string HeaderTitle => SelectedSection is { ShowTabs: true } s ? s.Title : _selectedPage?.Title ?? "";

    private void Open(SectionViewModel? section)
    {
        if (section is not null && !ReferenceEquals(section.EntryTab, _selectedPage))
            SelectedPage = section.EntryTab;
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
        var section = Sections.FirstOrDefault(s => s.Contains(page));
        section?.Remember(page);
        // The section first, while the old page is still open: the tab strip swaps its tabs, clears its selection and writes null back
        // (SelectedPage ignores it). Then the new page, one of the new tabs, becomes the strip's selection. With the page switched
        // before the section, the strip was left without a selection (window test ASectionWithTabs_ShowsTheStrip…).
        if (!ReferenceEquals(section, _selectedSection))
        {
            _selectedSection = section;
            OnPropertyChanged(nameof(SelectedSection));
            OnPropertyChanged(nameof(SelectedTopSection));
            OnPropertyChanged(nameof(SelectedBottomSection));
        }
        _selectedPage = page;
        OnPropertyChanged(nameof(HeaderTitle));
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
