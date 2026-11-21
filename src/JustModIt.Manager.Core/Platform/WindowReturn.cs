namespace JustModIt.Manager.Core.Platform;

/// <summary>How the player gets the app's window back once it is closed, which the session the app runs in decides.</summary>
public enum WindowReturn
{
    /// <summary>The app's icon in the system tray opens it.</summary>
    Tray,

    /// <summary>The app's entry in the system's list of apps opens it, since the desktop has no tray.</summary>
    AppMenu,

    /// <summary>
    /// The app's shortcut in the store's library opens it, in a store's game session, which shows only the programs the
    /// store starts.
    /// </summary>
    StoreLibrary,
}
