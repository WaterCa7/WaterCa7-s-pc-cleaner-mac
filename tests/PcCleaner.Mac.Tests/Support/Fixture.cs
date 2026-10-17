namespace PcCleaner.Mac.Tests.Support;

/// <summary>Captured macOS tool output in tests/PcCleaner.Mac.Tests/Fixtures (copied next to the test assembly).</summary>
public static class Fixture
{
    public static string PathOf(string relative) => Path.Combine(AppContext.BaseDirectory, "Fixtures", relative);

    public static string Text(string relative) => File.ReadAllText(PathOf(relative));
}
