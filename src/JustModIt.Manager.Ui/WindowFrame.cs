namespace JustModIt.Manager.Ui;

/// <summary>Who draws the window's title bar and its buttons.</summary>
public enum WindowFrame
{
    /// <summary>
    /// The window draws its own title bar with its caption buttons inside the system's frame, which keeps the edges, the
    /// shadow and snapping, as on Windows.
    /// </summary>
    Own,

    /// <summary>
    /// The system's window manager draws the frame and its buttons above the window's own header, as on a Linux desktop where
    /// the player chose the system's title bar.
    /// </summary>
    System,

    /// <summary>
    /// The window fills a screen of its own without a frame, as in gamescope, and its title bar offers its controls and its
    /// close button.
    /// </summary>
    Screen,

    /// <summary>
    /// The system draws no frame, so the window draws its title bar with its caption buttons, its edges that resize it and an
    /// outline, as on a Linux desktop unless the player chose the system's title bar.
    /// </summary>
    Frameless,
}
