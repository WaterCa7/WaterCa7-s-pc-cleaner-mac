namespace PcCleaner.Core;

/// <summary>Where a section sits in the sidebar: the main list or the block at the bottom (Профиль, Настройки).</summary>
public enum SectionPlace
{
    Top,
    Bottom,
}

/// <summary>
/// A sidebar item: one or more pages shown as tabs in a strip at the top of the page (spec 2026-10-27, part A); a section of one
/// page shows no strip. The Mac app has no simple mode, so every tab is always visible.
/// </summary>
public sealed class SectionViewModel : ObservableObject
{
    private readonly string? _title;
    private readonly string? _icon;
    private PageViewModel? _lastTab;

    /// <param name="title">The section's own title; null — the title of its first page.</param>
    /// <param name="icon">An icon name of Themes/Icons.axaml without «Icon.»; null — the icon of its first page.</param>
    public SectionViewModel(string key, string? title, string? icon, IReadOnlyList<PageViewModel> tabs, SectionPlace place)
    {
        if (tabs.Count == 0)
            throw new ArgumentException("A section needs at least one page.", nameof(tabs));
        Key = key;
        _title = title;
        _icon = icon;
        Tabs = tabs;
        Place = place;
    }

    public string Key { get; }

    public string Title => _title ?? Tabs[0].Title;

    public string Icon => _icon ?? Tabs[0].Icon;

    public SectionPlace Place { get; }

    public IReadOnlyList<PageViewModel> Tabs { get; }

    public bool ShowTabs => Tabs.Count > 1;

    /// <summary>The tab the section opens on: the last one opened, else the first.</summary>
    public PageViewModel EntryTab => _lastTab ?? Tabs[0];

    public bool Contains(PageViewModel page) => Tabs.Contains(page);

    internal void Remember(PageViewModel page)
    {
        if (Tabs.Contains(page))
            _lastTab = page;
    }
}
