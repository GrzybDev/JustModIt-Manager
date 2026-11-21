namespace JustModIt.Manager.Ui;

/// <summary>Who draws the window's title bar and its buttons.</summary>
public enum WindowFrame
{
    /// <summary>The window draws its own title bar with its caption buttons, as on Windows.</summary>
    Own,

    /// <summary>The system's window manager draws the frame and its buttons above the window's own header.</summary>
    System,

    /// <summary>
    /// The window fills a screen of its own without a frame, as in gamescope, and its title bar offers its controls and its
    /// close button.
    /// </summary>
    Screen,
}
