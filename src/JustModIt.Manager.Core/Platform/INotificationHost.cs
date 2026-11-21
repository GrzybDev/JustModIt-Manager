namespace JustModIt.Manager.Core.Platform;

/// <summary>The system's notifications, whose events are raised on a thread of the system's, so handlers must return at once.</summary>
public interface INotificationHost
{
    /// <summary>The player clicked a notification; carries the tag it was shown with.</summary>
    event EventHandler<string?>? Clicked;

    /// <summary>Whether clicking a notification raises <see cref="Clicked"/>, which a server that offers no actions never does.</summary>
    bool IsClickable { get; }

    /// <summary>Shows a notification.</summary>
    /// <param name="title">The notification's first line.</param>
    /// <param name="message">The notification's text.</param>
    /// <param name="tag">What <see cref="Clicked"/> carries when the player clicks it.</param>
    /// <returns>False when the session shows no notifications.</returns>
    bool Notify(string title, string message, string? tag);
}
