namespace JustModIt.Manager.Protocol.Views;

/// <summary>How the app's shortcut in Steam's library stands, each state winning over those after it.</summary>
public enum SteamShortcutState
{
    /// <summary>Steam was asked to add the shortcut, which has not appeared yet.</summary>
    Adding,

    /// <summary>A shortcut starts this copy of the app.</summary>
    Added,

    /// <summary>Steam does not run, and only Steam itself adds a shortcut.</summary>
    SteamNotRunning,

    /// <summary>The shortcut did not appear within <see cref="SteamShortcutView.AddTimeLimit"/> of asking.</summary>
    Failed,

    /// <summary>A shortcut of the app starts a file that is gone or another copy of the app.</summary>
    Elsewhere,

    /// <summary>No shortcut starts the app.</summary>
    Missing,
}

/// <summary>
/// The app's shortcut in the library of the Steam behind the home folder on Linux, from which a gamescope session such as
/// the Steam Deck's Game Mode opens the window.
/// </summary>
/// <param name="State">How the shortcut stands.</param>
/// <param name="HasGameSession">The system offers a gamescope session, where the window opens only from the shortcut.</param>
/// <param name="Program">The file a shortcut added by hand starts: this copy of the app.</param>
public sealed record SteamShortcutView(SteamShortcutState State, bool HasGameSession, string Program)
{
    /// <summary>How long Steam has to add the shortcut before adding it counts as failed.</summary>
    public static readonly TimeSpan AddTimeLimit = TimeSpan.FromSeconds(30);

    /// <summary>The file that the shortcut of another copy starts, in the state <see cref="SteamShortcutState.Elsewhere"/>.</summary>
    public string? Other { get; init; }

    /// <summary>The file <see cref="Other"/> names is not there anymore.</summary>
    public bool OtherMissing { get; init; }
}
