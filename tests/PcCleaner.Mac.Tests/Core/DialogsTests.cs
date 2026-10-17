using PcCleaner.Core;
using Xunit;

namespace PcCleaner.Mac.Tests.Core;

[Collection("dialogs")]
public class DialogsTests
{
    [Fact]
    public async Task ByDefault_EveryConfirmationIsDeclined()
    {
        Dialogs.UseSilentMode();
        Assert.False(await Dialogs.ConfirmAsync("Удалить?", "Список"));
        Assert.False(await Dialogs.AskAsync("Открыть?", "Отчёт"));
    }

    [Fact]
    public async Task Handlers_GetTheKind()
    {
        var seen = new List<DialogRequest>();
        Dialogs.ConfirmHandler = r =>
        {
            seen.Add(r);
            return Task.FromResult(true);
        };
        try
        {
            Assert.True(await Dialogs.ConfirmAsync("Удалить?", "Список"));
            Assert.True(await Dialogs.AskAsync("Открыть?", "Отчёт"));
            Assert.Equal(new[] { DialogKind.Warning, DialogKind.Question }, seen.Select(r => r.Kind));
        }
        finally
        {
            Dialogs.UseSilentMode();
        }
    }

    [Fact]
    public async Task SilentErrors_GoToTheLog()
    {
        Dialogs.UseSilentMode();
        var marker = "сообщение " + Guid.NewGuid();
        await Dialogs.ErrorAsync("Ошибка", marker);
        Assert.Contains(Log.Snapshot(), e => e.Level == LogLevel.Warning && e.Message.Contains(marker));
    }
}
