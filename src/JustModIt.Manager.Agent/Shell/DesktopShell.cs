using System.ComponentModel;
using System.Diagnostics;
using JustModIt.Manager.Core.Games;
using Microsoft.Extensions.Logging;

namespace JustModIt.Manager.Agent.Shell;

/// <summary>Opens folders and addresses with what the system uses for them, and starts programs.</summary>
public interface IDesktopShell
{
    /// <summary>Opens a folder or an address with what the system uses for it.</summary>
    /// <param name="target">A folder's path, or an address such as a store's link.</param>
    /// <returns>False when the system could not open it.</returns>
    bool Open(string target);

    /// <summary>Starts a game's program.</summary>
    /// <param name="program">The program with its arguments and working folder.</param>
    /// <returns>False when the system could not start it.</returns>
    bool Start(ProgramStart program);
}

/// <summary>Opens and starts through the Windows shell.</summary>
/// <param name="logger">Receives failures, without the target.</param>
internal sealed partial class DesktopShell(ILogger<DesktopShell> logger) : IDesktopShell
{
    public bool Open(string target)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(target);
        return Run(new ProcessStartInfo(target) { UseShellExecute = true });
    }

    // Through the shell, as a shortcut would start it, so a game that needs elevation asks for it.
    public bool Start(ProgramStart program)
    {
        ArgumentNullException.ThrowIfNull(program);
        return Run(new ProcessStartInfo(program.Executable, program.Arguments)
        {
            UseShellExecute = true,
            WorkingDirectory = program.WorkingDirectory,
        });
    }

    private bool Run(ProcessStartInfo start)
    {
        try
        {
            using var process = Process.Start(start);
            return true;
        }
        // The target is left out of the log, since folders often lie under the user's profile.
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
        {
            LogOpenFailed(exception.GetType().Name);
            return false;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The system could not open a folder, address or program ({Error})")]
    private partial void LogOpenFailed(string error);
}
