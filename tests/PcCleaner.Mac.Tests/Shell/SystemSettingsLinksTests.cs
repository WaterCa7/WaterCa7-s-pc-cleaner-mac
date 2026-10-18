using PcCleaner.Core;
using Xunit;

namespace PcCleaner.Mac.Tests.Shell;

public class SystemSettingsLinksTests
{
    [Theory]
    [InlineData(13)]
    [InlineData(15)]
    [InlineData(26)]
    public void LoginItems_Ventura_AndNewer_OpenGeneralLoginItems(int macOsMajor)
        => Assert.Equal("x-apple.systempreferences:com.apple.LoginItems-Settings.extension", SystemSettingsLinks.LoginItemsFor(macOsMajor));

    [Fact]
    public void LoginItems_Monterey_OpenUsersAndGroups()
        => Assert.Equal("x-apple.systempreferences:com.apple.preferences.users", SystemSettingsLinks.LoginItemsFor(12));
}
