using PcCleaner.Core;

namespace PcCleaner.Shell;

/// <summary>The sidebar of the Mac app (spec 2026-10-27, part A): which pages are the tabs of which section.</summary>
public static class ShellMenu
{
    private sealed record Entry(string Key, string? Title, string? Icon, string[] PageKeys, SectionPlace Place);

    private static readonly Entry[] Entries =
    {
        new("Dashboard", null, null, new[] { "Dashboard" }, SectionPlace.Top),
        new("Cleaner", null, null, new[] { "Cleaner" }, SectionPlace.Top),
        new("System", "Система", "Pulse", new[] { "Processes", "Startup", "Programs", "Disk" }, SectionPlace.Top),
        new("Protection", "Защита", "Shield", new[] { "Security", "Network" }, SectionPlace.Top),
        new("Profile", null, null, new[] { "Profile" }, SectionPlace.Bottom),
        new("Settings", null, null, new[] { "Settings" }, SectionPlace.Bottom),
    };

    /// <summary>
    /// The sections over <paramref name="pages"/>, in menu order. A section gets the pages it names that exist and is left out when
    /// there are none; a page no section names gets a section of its own at the end of the main list.
    /// </summary>
    public static List<SectionViewModel> Build(IReadOnlyList<PageViewModel> pages)
    {
        PageViewModel? Find(string key) => pages.FirstOrDefault(p => string.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase));

        var sections = new List<SectionViewModel>();
        foreach (var entry in Entries)
        {
            var tabs = entry.PageKeys.Select(Find).OfType<PageViewModel>().ToList();
            if (tabs.Count > 0)
                sections.Add(new SectionViewModel(entry.Key, entry.Title, entry.Icon, tabs, entry.Place));
        }
        var placed = sections.SelectMany(s => s.Tabs).ToHashSet();
        var firstBottom = sections.FindIndex(s => s.Place == SectionPlace.Bottom);
        var at = firstBottom < 0 ? sections.Count : firstBottom;
        foreach (var page in pages.Where(p => !placed.Contains(p)))
            sections.Insert(at++, new SectionViewModel(page.Key, null, null, new[] { page }, SectionPlace.Top));
        return sections;
    }
}
