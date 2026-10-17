namespace PcCleaner.Core;

/// <summary>Opens links, folders and System Settings panes with /usr/bin/open.</summary>
public sealed class MacShell
{
    private readonly ICommandRunner _runner;

    public MacShell(ICommandRunner runner) => _runner = runner;

    /// <summary>A web address, an x-apple.systempreferences: pane or a folder.</summary>
    public Task<bool> OpenAsync(string target) => RunAsync(new[] { target });

    /// <summary>«Показать в Finder»: opens the enclosing folder with the item selected.</summary>
    public Task<bool> RevealAsync(string path) => RunAsync(new[] { "-R", path });

    private async Task<bool> RunAsync(string[] args)
    {
        var result = await _runner.RunAsync("/usr/bin/open", args, TimeSpan.FromSeconds(15));
        if (result.Ok)
            return true;
        Log.Warn($"Не удалось открыть {args[^1]}: {(result.StdErr.Trim() is { Length: > 0 } e ? e : $"код {result.ExitCode}")}");
        return false;
    }
}
