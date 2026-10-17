namespace PcCleaner.Core;

/// <summary>A program and its arguments to run with administrator rights.</summary>
public sealed record AdminCommand(string Exe, IReadOnlyList<string> Args)
{
    public AdminCommand(string exe, params string[] args)
        : this(exe, (IReadOnlyList<string>)args)
    {
    }

    public override string ToString() => Args.Count == 0 ? Exe : $"{Exe} {string.Join(' ', Args)}";
}

public sealed record AdminResult(bool Ok, bool Cancelled, string? Error);

/// <summary>
/// Runs commands as administrator through the macOS password window (`do shell script … with administrator privileges`).
/// The AppleScript text never changes: the window's text and every command word arrive as arguments and are quoted with
/// `quoted form of`, so nothing read from files or settings can become code. One user action = one password window.
/// </summary>
public sealed class AdminPrompt
{
    /// <summary>Separates commands in the argument list; no command word may equal it.</summary>
    internal const string Separator = "\u0001&&\u0001";

    internal static readonly string[] Script =
    {
        "-e", "on run argv",
        "-e", "set promptText to item 1 of argv",
        "-e", "set sep to item 2 of argv",
        "-e", "set commandText to \"\"",
        "-e", "repeat with i from 3 to count of argv",
        "-e", "set itemText to item i of argv",
        "-e", "if itemText is sep then",
        "-e", "set commandText to commandText & \"&& \"",
        "-e", "else",
        "-e", "set commandText to commandText & (quoted form of itemText) & \" \"",
        "-e", "end if",
        "-e", "end repeat",
        "-e", "do shell script commandText with prompt promptText with administrator privileges",
        "-e", "end run",
    };

    private readonly ICommandRunner _runner;

    public AdminPrompt(ICommandRunner runner) => _runner = runner;

    /// <param name="reason">The text of the password window: what will be done and why.</param>
    public async Task<AdminResult> RunAsync(string reason, IReadOnlyList<AdminCommand> commands, CancellationToken ct)
    {
        var result = await _runner.RunAsync("/usr/bin/osascript", Arguments(reason, commands), TimeSpan.FromMinutes(5), ct);
        if (result.Ok)
        {
            foreach (var command in commands)
                Log.Action($"С правами администратора: {command}");
            return new AdminResult(true, false, null);
        }
        if (result.StdErr.Contains("-128", StringComparison.Ordinal))
        {
            Log.Info("Окно пароля администратора закрыто — ничего не изменено");
            return new AdminResult(false, true, "отменено — ничего не изменено");
        }
        Log.Warn($"Команда с правами администратора не выполнена (код {result.ExitCode}): {result.StdErr.Trim()}");
        return new AdminResult(false, false, result.TimedOut ? "окно пароля не дождалось ответа" : "macOS не выполнила команду — подробности в журнале");
    }

    internal static string[] Arguments(string reason, IReadOnlyList<AdminCommand> commands)
    {
        if (commands.Count == 0)
            throw new ArgumentException("Нет команд", nameof(commands));
        var args = new List<string>(Script) { reason, Separator };
        for (var i = 0; i < commands.Count; i++)
        {
            var command = commands[i];
            if (!Path.IsPathRooted(command.Exe))
                throw new ArgumentException($"Нужен полный путь к программе, а не «{command.Exe}»", nameof(commands));
            if (i > 0)
                args.Add(Separator);
            foreach (var word in command.Args.Prepend(command.Exe))
            {
                if (word == Separator)
                    throw new ArgumentException("Недопустимый аргумент команды", nameof(commands));
                args.Add(word);
            }
        }
        return args.ToArray();
    }
}
