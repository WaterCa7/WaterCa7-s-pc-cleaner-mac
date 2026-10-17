using PcCleaner.Core;

namespace PcCleaner.Shell;

/// <summary>A section that arrives in a later build: the sidebar already shows it, the page says what it will do.</summary>
public sealed class UpcomingPageViewModel : PageViewModel
{
    private readonly string _key;

    public UpcomingPageViewModel(string key, string title, string subtitle, string icon, string description)
    {
        _key = key;
        Title = title;
        Subtitle = subtitle;
        Icon = icon;
        Description = description;
    }

    public override string Key => _key;

    public override string Title { get; }

    public override string Subtitle { get; }

    public override string Icon { get; }

    public string Description { get; }
}
