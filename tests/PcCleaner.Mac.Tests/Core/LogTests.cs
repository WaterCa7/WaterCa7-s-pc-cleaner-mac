using PcCleaner.Core;
using Xunit;

namespace PcCleaner.Mac.Tests.Core;

public class LogTests
{
    private static string ReadTodayLog()
    {
        var file = Path.Combine(AppPaths.LogsDir, $"{DateTime.Now:yyyy-MM-dd}.log");
        using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        return new StreamReader(stream).ReadToEnd();
    }

    [Fact]
    public void Action_GoesToEntriesAndToTheDailyFile()
    {
        var marker = "Тест журнала " + Guid.NewGuid();
        Log.Action(marker);

        Assert.Contains(Log.Entries.ToList(), e => e.Message == marker && e.Level == LogLevel.Action);
        Assert.Contains(marker, ReadTodayLog());
    }

    [Fact]
    public void Error_ShowsRussianText_TheFileKeepsTheOriginal()
    {
        var marker = "Ошибка теста " + Guid.NewGuid();
        Log.Error(marker, new UnauthorizedAccessException("Access to the path '/x' is denied."));

        var entry = Log.Entries.ToList().Last(e => e.Message.StartsWith(marker));
        Assert.Contains("нет доступа", entry.Message);
        Assert.DoesNotContain("Access to the path", entry.Message);
        Assert.Contains("Access to the path", ReadTodayLog());
    }
}
