using PcCleaner.Shell;
using Xunit;

namespace PcCleaner.Mac.Tests.Shell;

public class PageCaptureTests
{
    private static MainViewModel Shell() => new(ShellMenuTests.Pages(ShellMenuTests.AllKeys));

    [Fact]
    public void WithoutRequests_EveryTabIsShot_NumberedByPlace()
    {
        var shots = PageCapture.PagesToShoot(Shell(), Array.Empty<string>());

        Assert.Equal(Enumerable.Range(1, 10), shots.Select(s => s.Number));
    }

    [Theory]
    [InlineData("System", new[] { "Processes", "Startup", "Programs", "Disk" })]
    [InlineData("protection/network", new[] { "Network" })]
    [InlineData("Disk", new[] { "Disk" })]
    [InlineData("Dashboard,System/Startup", new[] { "Dashboard", "Startup" })]
    [InlineData("System/Network", new string[0])]
    [InlineData("Nowhere", new string[0])]
    public void ARequest_NamesAPage_ASection_OrASectionTab(string request, string[] expected)
        => Assert.Equal(expected, PageCapture.PagesToShoot(Shell(), request.Split(',')).Select(s => s.Page.Key));

    [Theory]
    [InlineData("System,Nowhere", new[] { "Nowhere" })]
    [InlineData("Nowhere,System/Network,Dashboard", new[] { "Nowhere", "System/Network" })]
    [InlineData("Disk,protection/network", new string[0])]
    public void UnmatchedRequests_AreThoseNamingNoTab(string requests, string[] expected)
        => Assert.Equal(expected, PageCapture.UnmatchedRequests(Shell(), requests.Split(',')));

    [Fact]
    public void UnmatchedRequests_AreNone_WithoutRequests()
        => Assert.Empty(PageCapture.UnmatchedRequests(Shell(), Array.Empty<string>()));

    [Fact]
    public void Files_AreNamedBySectionAndTab_AndASinglePageSectionByItsKey()
    {
        var vm = Shell();

        Assert.Equal("01-Dashboard.png", PageCapture.FileName(1, vm.Pages[0], vm.Sections));
        Assert.Equal("06-System-Disk.png", PageCapture.FileName(6, vm.Pages[5], vm.Sections));
        Assert.Equal("08-Protection-Network.png", PageCapture.FileName(8, vm.Pages[7], vm.Sections));
        Assert.Equal("10-Settings.png", PageCapture.FileName(10, vm.Pages[9], vm.Sections));
    }
}
