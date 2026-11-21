namespace JustModIt.Manager.Host;

/// <summary>The way a launch goes where the agent runs as the user's systemd unit.</summary>
internal enum LaunchRoute
{
    /// <summary>Starts the agent's unit and nothing else, as a start with the system and the updater's restart do.</summary>
    Background,

    /// <summary>
    /// Starts the unit and hands the command line to the agent without becoming the window, since the session shows only
    /// the windows of programs Steam started; the agent keeps the page for the window Steam opens.
    /// </summary>
    HandOver,

    /// <summary>Starts the unit and becomes the window, or hands the command line over to the window that runs.</summary>
    Window,
}
