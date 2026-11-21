using System.ComponentModel;
using System.Diagnostics;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.Platform;
using Microsoft.Extensions.Logging;

namespace JustModIt.Manager.Platform.Windows;

/// <summary>Opens and starts through the Windows shell, and runs the app's own programs without it.</summary>
/// <param name="logger">Receives failures, without the target.</param>
public sealed partial class WindowsDesktopShell(ILogger<WindowsDesktopShell> logger) : IDesktopShell
{
    public bool Open(string target)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(target);
        return TryStart(new ProcessStartInfo(target) { UseShellExecute = true }, out _);
    }

    // Through the shell, as a shortcut would start it, so a game that needs elevation asks for it.
    public bool Start(ProgramStart program)
    {
        return TryStart(new ProcessStartInfo(program.Executable, program.Arguments)
        {
            UseShellExecute = true,
            WorkingDirectory = program.WorkingDirectory,
        }, out _);
    }

    public int? Run(string executable, IReadOnlyList<string> arguments, string? workingDirectory = null) =>
        TryStart(new ProcessStartInfo(executable, arguments) { UseShellExecute = false, WorkingDirectory = workingDirectory ?? "" },
            out var processId) ? processId : null;

    private bool TryStart(ProcessStartInfo start, out int? processId)
    {
        try
        {
            // The shell may hand the target to a program that runs already, and then starts no process.
            using var process = Process.Start(start);
            processId = process?.Id;
            return true;
        }
        // The target is left out of the log, since folders often lie under the user's profile.
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
        {
            LogOpenFailed(exception.GetType().Name);
            processId = null;
            return false;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The system could not open a folder, address or program ({Error})")]
    private partial void LogOpenFailed(string error);
}
