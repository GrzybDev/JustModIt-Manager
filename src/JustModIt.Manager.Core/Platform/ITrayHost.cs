namespace JustModIt.Manager.Core.Platform;

/// <summary>An entry of the tray icon's menu; picking it reports its <see cref="Command"/>.</summary>
/// <param name="Command">What <see cref="ITrayHost.CommandInvoked"/> carries; empty for a separator.</param>
/// <param name="Text">The entry's label.</param>
public sealed record TrayMenuItem(string Command, string Text)
{
    public static TrayMenuItem Separator { get; } = new("", "");

    public bool IsEnabled { get; init; } = true;

    /// <summary>Shown in bold: what clicking the icon itself does.</summary>
    public bool IsDefault { get; init; }

    public bool IsSeparator => Command.Length == 0;
}

/// <summary>
/// The app's icon in the notification area with its tooltip, menu and notifications, whose events are raised on the
/// tray's own thread, so handlers must return at once.
/// </summary>
public interface ITrayHost : IDisposable
{
    /// <summary>The player clicked the icon, or chose it with the keyboard.</summary>
    event EventHandler? Activated;

    /// <summary>The player picked a menu entry; carries its command.</summary>
    event EventHandler<string>? CommandInvoked;

    /// <summary>The player clicked a notification; carries the tag it was shown with.</summary>
    event EventHandler<string?>? NotificationClicked;

    /// <summary>Shows the icon, or updates it when it is shown, ending texts longer than the system shows in an ellipsis.</summary>
    /// <param name="tooltip">The product name and the status, shown when the pointer rests on the icon.</param>
    /// <param name="menu">The entries of the icon's menu, in order.</param>
    void Show(string tooltip, IReadOnlyList<TrayMenuItem> menu);

    /// <summary>Shows a notification from the icon once the icon is shown, cut like the tooltip.</summary>
    /// <param name="title">The notification's first line.</param>
    /// <param name="message">The notification's text.</param>
    /// <param name="tag">What <see cref="NotificationClicked"/> carries when the player clicks it.</param>
    void ShowNotification(string title, string message, string? tag);
}
