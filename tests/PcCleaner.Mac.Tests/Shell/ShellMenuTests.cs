using PcCleaner.Core;
using PcCleaner.Shell;
using Xunit;

namespace PcCleaner.Mac.Tests.Shell;

public class ShellMenuTests
{
    internal sealed class Page(string key) : PageViewModel
    {
        public override string Title => "Страница " + key;

        public override string Subtitle => "";

        public override string Icon => "Home";

        public override string Key => key;
    }

    internal static readonly string[] AllKeys =
        { "Dashboard", "Cleaner", "Processes", "Startup", "Programs", "Disk", "Security", "Network", "Profile", "Settings" };

    internal static List<PageViewModel> Pages(params string[] keys) => keys.Select(k => (PageViewModel)new Page(k)).ToList();

    [Fact]
    public void TheMenu_HasSixSections_WithTheTabsOfTheSpec()
    {
        var sections = ShellMenu.Build(Pages(AllKeys));

        Assert.Equal(new[] { "Dashboard", "Cleaner", "System", "Protection", "Profile", "Settings" }, sections.Select(s => s.Key));
        Assert.Equal(new[] { "Processes", "Startup", "Programs", "Disk" }, sections[2].Tabs.Select(p => p.Key));
        Assert.Equal(new[] { "Security", "Network" }, sections[3].Tabs.Select(p => p.Key));
        Assert.Equal(new[] { "Система", "Защита" }, new[] { sections[2].Title, sections[3].Title });
        Assert.Equal("Страница Cleaner", sections[1].Title);
        Assert.Equal(new[] { SectionPlace.Bottom, SectionPlace.Bottom }, sections.Skip(4).Select(s => s.Place));
        Assert.True(sections[2].ShowTabs);
        Assert.False(sections[1].ShowTabs);
    }

    [Fact]
    public void MissingPages_AreSkipped_AndAnUnknownPageGetsItsOwnSectionBeforeTheBottom()
    {
        var sections = ShellMenu.Build(Pages("Dashboard", "Phone", "Settings"));

        Assert.Equal(new[] { "Dashboard", "Phone", "Settings" }, sections.Select(s => s.Key));
        Assert.Equal(SectionPlace.Top, sections[1].Place);
    }

    [Fact]
    public void EntryTab_IsTheLastOpenedTab_ElseTheFirst()
    {
        var system = ShellMenu.Build(Pages(AllKeys)).Single(s => s.Key == "System");
        Assert.Equal("Processes", system.EntryTab.Key);

        system.Remember(system.Tabs.Single(p => p.Key == "Disk"));
        Assert.Equal("Disk", system.EntryTab.Key);
    }
}

/// <summary>The shell over the menu: two sidebar lists, the tab strip, navigation by page key.</summary>
public class ShellSectionsTests
{
    private static MainViewModel Shell() => new(ShellMenuTests.Pages(ShellMenuTests.AllKeys));

    [Fact]
    public void TheSidebar_HasFourSectionsOnTop_AndProfileAndSettingsAtTheBottom()
    {
        var shell = Shell();

        Assert.Equal(new[] { "Dashboard", "Cleaner", "System", "Protection" }, shell.TopSections.Select(s => s.Key));
        Assert.Equal(new[] { "Profile", "Settings" }, shell.BottomSections.Select(s => s.Key));
        Assert.Equal("Dashboard", shell.SelectedSection!.Key);
    }

    [Theory]
    [InlineData("Dashboard", "Dashboard")]
    [InlineData("Cleaner", "Cleaner")]
    [InlineData("Processes", "System")]
    [InlineData("Startup", "System")]
    [InlineData("Programs", "System")]
    [InlineData("Disk", "System")]
    [InlineData("Security", "Protection")]
    [InlineData("Network", "Protection")]
    [InlineData("Profile", "Profile")]
    [InlineData("Settings", "Settings")]
    public void NavigateTo_APageKey_OpensItsSectionAndTab(string pageKey, string sectionKey)
    {
        var shell = Shell();

        shell.NavigateTo(pageKey);

        Assert.Equal(pageKey, shell.SelectedPage!.Key);
        Assert.Equal(sectionKey, shell.SelectedSection!.Key);
    }

    [Fact]
    public void ASection_OpensOnTheLastOpenedTab()
    {
        var shell = Shell();
        var system = shell.TopSections.Single(s => s.Key == "System");

        shell.SelectedTopSection = system;
        Assert.Equal("Processes", shell.SelectedPage!.Key);

        shell.NavigateTo("Disk");
        shell.NavigateTo("Dashboard");
        shell.SelectedTopSection = system;
        Assert.Equal("Disk", shell.SelectedPage!.Key);
    }

    [Fact]
    public void OnlyTheListOfTheOpenSection_HasASelection_AndANullWriteIsIgnored()
    {
        var shell = Shell();

        shell.NavigateTo("Settings");
        Assert.Null(shell.SelectedTopSection);
        Assert.Equal("Settings", shell.SelectedBottomSection!.Key);

        shell.SelectedTopSection = null;
        Assert.Equal("Settings", shell.SelectedPage!.Key);
    }

    [Fact]
    public void TheHeader_ShowsTheSection_WhenThereAreTabs_ElseThePage()
    {
        var shell = Shell();

        shell.NavigateTo("Network");
        Assert.Equal("Защита", shell.HeaderTitle);

        shell.NavigateTo("Cleaner");
        Assert.Equal("Страница Cleaner", shell.HeaderTitle);
    }
}
