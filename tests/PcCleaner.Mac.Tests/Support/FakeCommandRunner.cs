using PcCleaner.Core;

namespace PcCleaner.Mac.Tests.Support;

/// <summary>
/// Answers with captured output for the commands it knows; anything else "is not installed" (exit code 127).
/// Records every call as "exe arg1 arg2".
/// </summary>
public sealed class FakeCommandRunner : ICommandRunner
{
    private readonly Dictionary<string, Func<CommandResult>> _answers = new();
    private readonly List<string> _calls = new();

    public IReadOnlyList<string> Calls
    {
        get
        {
            lock (_calls)
                return _calls.ToList();
        }
    }

    public static string Key(string exe, IReadOnlyList<string> args) => args.Count == 0 ? exe : exe + " " + string.Join(" ", args);

    public FakeCommandRunner On(string exe, IReadOnlyList<string> args, CommandResult result)
    {
        _answers[Key(exe, args)] = () => result;
        return this;
    }

    public FakeCommandRunner OnFixture(string exe, IReadOnlyList<string> args, string fixture, int exitCode = 0)
    {
        _answers[Key(exe, args)] = () => new CommandResult(exitCode, Fixture.Text(fixture), "");
        return this;
    }

    public Task<CommandResult> RunAsync(string exe, IReadOnlyList<string> args, TimeSpan? timeout = null, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var key = Key(exe, args);
        lock (_calls)
            _calls.Add(key);
        return Task.FromResult(_answers.TryGetValue(key, out var answer) ? answer() : CommandResult.NotStarted($"нет образца для {key}"));
    }
}
