using PcCleaner.Core;
using PcCleaner.Modules.Dashboard;
using Xunit;

namespace PcCleaner.Mac.Tests.Core;

[Collection("settings-file")]
public class AppSettingsTests
{
    [Fact]
    public void SetModuleValue_IsSavedAndLoaded()
    {
        var settings = new AppSettings();
        settings.SetModuleValue("Dashboard", "Test", "значение");
        Assert.Equal("значение", AppSettings.Load().GetModuleValue("Dashboard", "Test"));
    }

    [Fact]
    public void SetModuleValue_Null_RemovesTheValue()
    {
        var settings = new AppSettings();
        settings.SetModuleValue("Dashboard", "Gone", "1");
        settings.SetModuleValue("Dashboard", "Gone", null);
        Assert.Null(AppSettings.Load().GetModuleValue("Dashboard", "Gone"));
    }

    [Fact]
    public void Load_BrokenFile_GivesDefaults()
    {
        AppPaths.EnsureDir(AppPaths.DataDir);
        File.WriteAllText(AppPaths.SettingsFile, "{ не json");
        Assert.Empty(AppSettings.Load().ModuleData);
    }

    [Fact]
    public void ScanSummary_RoundTripsThroughSettings()
    {
        var settings = new AppSettings();
        new ScanSummary { Score = 81, Time = new DateTime(2026, 10, 17, 12, 0, 0), IssueCount = 2, TipCount = 1 }.Save(settings);
        var loaded = ScanSummary.Load(settings);
        Assert.Equal(81, loaded!.Score);
        Assert.Equal(1, loaded.ProblemCount);
    }
}
