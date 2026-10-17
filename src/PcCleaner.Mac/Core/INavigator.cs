namespace PcCleaner.Core;

/// <summary>The window's page list: lets a page open another one (e.g. «Открыть раздел» on the dashboard).</summary>
public interface INavigator
{
    IEnumerable<PageViewModel> Pages { get; }

    void NavigateTo(string pageKey);
}
