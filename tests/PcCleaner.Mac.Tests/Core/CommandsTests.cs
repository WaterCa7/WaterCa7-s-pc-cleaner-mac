using PcCleaner.Core;
using Xunit;

namespace PcCleaner.Mac.Tests.Core;

public class CommandsTests
{
    [Fact]
    public void RelayCommand_Exception_IsLoggedNotThrown()
    {
        var marker = "сбой " + Guid.NewGuid();
        new RelayCommand(() => throw new InvalidOperationException(marker)).Execute(null);
        Assert.Contains(Log.Snapshot(), e => e.Level == LogLevel.Error && e.Message.Contains(marker));
    }

    [Fact]
    public void Refresh_RaisesCanExecuteChanged()
    {
        var raised = 0;
        var command = new RelayCommand(() => { });
        command.CanExecuteChanged += (_, _) => raised++;
        CommandBase.Refresh();
        Assert.True(raised >= 1);
    }

    [Fact]
    public async Task AsyncRelayCommand_BlocksReentryWhileRunning()
    {
        var gate = new TaskCompletionSource();
        var runs = 0;
        var command = new AsyncRelayCommand(async () =>
        {
            runs++;
            await gate.Task;
        });
        var first = command.ExecuteAsync();
        Assert.False(command.CanExecute(null));
        await command.ExecuteAsync();
        gate.SetResult();
        await first;
        Assert.Equal(1, runs);
        Assert.True(command.CanExecute(null));
    }
}
