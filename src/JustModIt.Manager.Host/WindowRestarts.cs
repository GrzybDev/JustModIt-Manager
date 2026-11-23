using System.Globalization;
using JustModIt.Manager.Core.Platform;
using JustModIt.Manager.Ui;

namespace JustModIt.Manager.Host;

/// <summary>
/// The window's start anew in the agent's version: once the window closed, ended its session and let go of its lock, the
/// process runs the app's file again as the window, which an update replaced in place; Steam and gamescope keep it as the
/// game they started, since the process stays.
/// </summary>
internal static class WindowRestarts
{
    // Carries the restart over to the window the process becomes, which takes it out of its environment at once.
    private const string Variable = "MANAGER_WINDOW_RESTARTED_FOR";

    /// <summary>Takes the restart this process came from out of its environment, so no program it starts inherits it.</summary>
    /// <returns>Null when the process did not start anew.</returns>
    public static WindowRestart? Take()
    {
        var text = Environment.GetEnvironmentVariable(Variable);
        Environment.SetEnvironmentVariable(Variable, null);
        return Parse(text);
    }

    /// <summary>The environment the window starts anew with: this process's own, less the AppImage runtime's, with the restart.</summary>
    /// <param name="restart">The restart to carry over.</param>
    public static Dictionary<string, string?> EnvironmentFor(WindowRestart restart)
    {
        var environment = ChildEnvironment.Current();
        // The runtime of the file started anew sets its own, for its own mount.
        ChildEnvironment.LeaveAppImage(environment);
        environment[Variable] = Format(restart);
        return environment;
    }

    /// <summary>
    /// Writes a restart as the variable carries it: the version, whether the player asked for it, then where the window stood,
    /// if anywhere.
    /// </summary>
    /// <param name="restart">The restart to write.</param>
    internal static string Format(WindowRestart restart)
    {
        var text = $"{restart.Version};{(restart.Asked ? 1 : 0)}";
        return restart.Placement is { } placement
            ? string.Create(CultureInfo.InvariantCulture,
                $"{text};{placement.X};{placement.Y};{placement.Width};{placement.Height};{(placement.Maximized ? 1 : 0)}")
            : text;
    }

    /// <summary>Reads a restart as <see cref="Format"/> wrote it.</summary>
    /// <param name="text">The variable's value; null when it is not set.</param>
    /// <returns>Null for no restart.</returns>
    internal static WindowRestart? Parse(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        var parts = text.Split(';');
        return new WindowRestart(parts[0], parts[1] == "1", parts is [_, _, var x, var y, var width, var height, var maximized]
            ? new WindowPlacement(int.Parse(x, CultureInfo.InvariantCulture), int.Parse(y, CultureInfo.InvariantCulture),
                double.Parse(width, CultureInfo.InvariantCulture), double.Parse(height, CultureInfo.InvariantCulture), maximized == "1")
            : null);
    }
}
