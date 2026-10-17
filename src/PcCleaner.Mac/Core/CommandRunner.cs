using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace PcCleaner.Core;

/// <summary>What a macOS tool printed and how it ended.</summary>
public sealed record CommandResult(int ExitCode, string StdOut, string StdErr, bool TimedOut = false)
{
    /// <summary>Exit code of a program that could not be started (as the shell reports "command not found").</summary>
    public const int NotStartedCode = 127;

    public bool Ok => ExitCode == 0 && !TimedOut;

    public static CommandResult NotStarted(string error) => new(NotStartedCode, "", error);
}

/// <summary>
/// Runs macOS tools. Every check of the system goes through this interface, so tests replace it with captured output
/// (FakeCommandRunner) and never touch the real system.
/// </summary>
public interface ICommandRunner
{
    /// <param name="exe">Full path of the tool, e.g. "/bin/launchctl" (never looked up in PATH).</param>
    /// <param name="args">Arguments as a list — no shell, nothing is parsed or expanded.</param>
    /// <param name="timeout">Kills the tool after this time (default 30 s); the result then has TimedOut set.</param>
    Task<CommandResult> RunAsync(string exe, IReadOnlyList<string> args, TimeSpan? timeout = null, CancellationToken ct = default);
}

public sealed class ProcessCommandRunner : ICommandRunner
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

    public async Task<CommandResult> RunAsync(string exe, IReadOnlyList<string> args, TimeSpan? timeout = null, CancellationToken ct = default)
    {
        if (!Path.IsPathRooted(exe))
            throw new ArgumentException($"Нужен полный путь к программе, а не «{exe}»", nameof(exe));

        var psi = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            CreateNoWindow = true,
        };
        foreach (var arg in args)
            psi.ArgumentList.Add(arg);
        // English, UTF-8 output: the parsers expect the tools' own (untranslated) wording.
        psi.Environment["LC_ALL"] = "en_US.UTF-8";
        psi.Environment["LANG"] = "en_US.UTF-8";

        using var process = new Process { StartInfo = psi };
        try
        {
            if (!process.Start())
                return CommandResult.NotStarted($"{exe} не запустился");
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            return CommandResult.NotStarted(ex.Message);
        }

        process.StandardInput.Close();
        var stdout = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        var stderr = process.StandardError.ReadToEndAsync(CancellationToken.None);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(timeout ?? DefaultTimeout);
        try
        {
            await process.WaitForExitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException)
        {
            Kill(process);
            if (ct.IsCancellationRequested)
                throw;
            return new CommandResult(-1, await Finish(stdout), await Finish(stderr), TimedOut: true);
        }
        return new CommandResult(process.ExitCode, await stdout, await stderr);
    }

    private static void Kill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or NotSupportedException)
        {
            // Already gone.
        }
    }

    /// <summary>Output read so far from a killed process (the pipe closes when it exits).</summary>
    private static async Task<string> Finish(Task<string> read)
    {
        try
        {
            return await read.WaitAsync(TimeSpan.FromSeconds(2));
        }
        catch (Exception ex) when (ex is TimeoutException or IOException or ObjectDisposedException)
        {
            return "";
        }
    }
}
