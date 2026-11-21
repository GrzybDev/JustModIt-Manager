namespace JustModIt.Manager.Ui;

/// <summary>Where the window stood, so it opens there again once it started anew.</summary>
/// <param name="X">Its left edge on the screen, in pixels.</param>
/// <param name="Y">Its top edge on the screen, in pixels.</param>
/// <param name="Width">Its width.</param>
/// <param name="Height">Its height.</param>
/// <param name="Maximized">Whether it filled the screen's working area.</param>
public sealed record WindowPlacement(int X, int Y, double Width, double Height, bool Maximized);

/// <summary>The window's start anew from the app's file, in the same process, which the window it becomes is told of.</summary>
/// <param name="Version">The agent's version, which the window starts again for.</param>
/// <param name="Asked">The player asked for it on the update card, so the window that starts anew does not say it did.</param>
/// <param name="Placement">Where the window stood; null for its usual place.</param>
public sealed record WindowRestart(string Version, bool Asked = false, WindowPlacement? Placement = null);
