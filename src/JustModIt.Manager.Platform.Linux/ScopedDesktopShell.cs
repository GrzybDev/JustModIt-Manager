using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.Platform;
using Microsoft.Extensions.Logging;
using Tmds.DBus.Protocol;
using Systemd = JustModIt.Manager.Platform.Linux.DBus.Manager;

namespace JustModIt.Manager.Platform.Linux;

/// <summary>
/// Starts what the agent opens in a systemd scope of its own, since stopping the agent's unit takes every process left in
/// it; each program gets the user's systemd environment as it is at that moment, since the unit keeps the one of the
/// session it started in, such as Game Mode's desktop after a switch to the desktop.
/// </summary>
/// <param name="desktopId">Names the scopes, as it names the app's desktop entry.</param>
/// <param name="sessionBus">The address of the session bus, on which the user's systemd answers; null without one.</param>
/// <param name="logger">Receives failures, without the target.</param>
public sealed partial class ScopedDesktopShell(string desktopId, string? sessionBus, ILogger<ScopedDesktopShell> logger) : IDesktopShell
{
    // systemd-run moves itself into the new scope before it runs the program, which took up to 306 ms on a Steam Deck.
    private static readonly TimeSpan ScopeTimeout = TimeSpan.FromSeconds(3);

    /// <summary>The program that starts a scope; tests put one of their own in its place.</summary>
    internal string ScopeRunner { get; init; } = "systemd-run";

    public bool Open(string target) => Start("xdg-open", Quote(target), workingDirectory: null, mustLeave: false) is not null;

    public bool Start(ProgramStart program) =>
        Start(program.Executable, program.Arguments, program.WorkingDirectory, mustLeave: false) is not null;

    // Returns once the program runs outside this process's unit, so an agent that exits right after, as for an update, keeps
    // it; a scope that is slow to start counts as a failure, since the unit's stop would take the program with the agent.
    public int? Run(string executable, IReadOnlyList<string> arguments, string? workingDirectory = null) =>
        Start(executable, string.Join(' ', arguments.Select(Quote)), workingDirectory, mustLeave: true);

    // The words of the program's own command line follow as given, so a scope runs it with the arguments a direct start would.
    private int? Start(string executable, string arguments, string? workingDirectory, bool mustLeave)
    {
        var environment = UserEnvironment();
        var scope = $"app-{desktopId}-{Random.Shared.Next().ToString("x8", CultureInfo.InvariantCulture)}";
        var start = environment is null
            ? new ProcessStartInfo(executable, arguments)
            : new ProcessStartInfo(ScopeRunner, $"--user --scope --collect --quiet --unit={scope} -- {Quote(executable)} {arguments}");
        start.UseShellExecute = false;
        start.WorkingDirectory = workingDirectory ?? "";
        if (environment is null)
        {
            ChildEnvironment.LeaveAppImage(start.Environment);
        }
        else
        {
            start.Environment.Clear();
            foreach (var variable in environment)
            {
                var separator = variable.IndexOf('=', StringComparison.Ordinal);
                if (separator > 0)
                {
                    start.Environment[variable[..separator]] = variable[(separator + 1)..];
                }
            }
        }

        try
        {
            using var process = Process.Start(start)!;
            return environment is null || LeftUnit(process, mustLeave) ? process.Id : null;
        }
        // The target is left out of the log, since folders often lie under the user's profile.
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
        {
            LogStartFailed(exception.GetType().Name);
            return null;
        }
    }

    // Read at every start over a connection of its own: the desktop imports its display into the user's systemd after the
    // agent started, and a start may come from a handler of the shared connection, which could not read the answer while
    // its handler waits for it.
    private string[]? UserEnvironment()
    {
        if (sessionBus is null)
        {
            return null;
        }

        try
        {
            using var connection = new DBusConnection(new DBusConnectionOptions(sessionBus) { AutoConnect = true });
            return new Systemd(connection, "org.freedesktop.systemd1", "/org/freedesktop/systemd1").GetEnvironmentAsync()
                .GetAwaiter().GetResult();
        }
        // A system without the user's systemd has no scopes either, so the program starts as this process's child.
        catch (DBusExceptionBase exception)
        {
            LogNoUserSystemd(exception.GetType().Name);
            return null;
        }
    }

    private bool LeftUnit(Process process, bool mustLeave)
    {
        var unit = ControlGroup("self");
        var waited = Stopwatch.StartNew();
        while (waited.Elapsed < ScopeTimeout)
        {
            if (process.HasExited)
            {
                if (process.ExitCode == 0)
                {
                    return true;
                }

                LogScopeFailed(process.ExitCode);
                return false;
            }

            if (ControlGroup(process.Id.ToString(CultureInfo.InvariantCulture)) is { } group && group != unit)
            {
                return true;
            }

            Thread.Sleep(5);
        }

        LogScopeSlow(ScopeTimeout);
        return !mustLeave;
    }

    // Null once the process has gone.
    private static string? ControlGroup(string process)
    {
        try
        {
            return File.ReadAllText(Path.Join("/proc", process, "cgroup"));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    // .NET splits a command line into words by Windows' rules on every system: backslashes count only before a quote.
    private static string Quote(string word)
    {
        var quoted = new StringBuilder().Append('"');
        var backslashes = 0;
        foreach (var character in word)
        {
            if (character == '\\')
            {
                backslashes++;
                continue;
            }

            quoted.Append('\\', character == '"' ? (backslashes * 2) + 1 : backslashes).Append(character);
            backslashes = 0;
        }

        return quoted.Append('\\', backslashes * 2).Append('"').ToString();
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The system could not open a folder, address or program ({Error})")]
    private partial void LogStartFailed(string error);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The system could not open a folder, address or program (exit code {ExitCode})")]
    private partial void LogScopeFailed(int exitCode);

    [LoggerMessage(Level = LogLevel.Warning, Message = "A program's scope did not start within {Timeout}")]
    private partial void LogScopeSlow(TimeSpan timeout);

    [LoggerMessage(Level = LogLevel.Information, Message = "The user's systemd did not answer ({Error}), so a program starts without a scope")]
    private partial void LogNoUserSystemd(string error);
}
