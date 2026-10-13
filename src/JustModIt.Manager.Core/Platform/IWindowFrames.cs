namespace JustModIt.Manager.Core.Platform;

/// <summary>
/// The frame the system draws around windows. A window that draws its own title bar has none above its content, while
/// the system still counts one when it keeps the window at its smallest size.
/// </summary>
public interface IWindowFrames
{
    /// <summary>How far the system's title bar and frame would reach above a window's content.</summary>
    /// <param name="window">The window's native handle.</param>
    /// <returns>The height in the window's own pixels; 0 where the system cannot tell.</returns>
    int TitleBarHeight(nint window);
}
