using JustModIt.Manager.Core.Games;

namespace JustModIt.Manager.Core.Platform;

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

    /// <summary>Starts a program directly, without the shell, such as the app's own window.</summary>
    /// <param name="executable">The program's full path.</param>
    /// <param name="arguments">Its arguments, each one word.</param>
    /// <param name="workingDirectory">The folder it starts in; null for the agent's own.</param>
    /// <returns>The started process's id; null when the system could not start it.</returns>
    int? Run(string executable, IReadOnlyList<string> arguments, string? workingDirectory = null);
}
