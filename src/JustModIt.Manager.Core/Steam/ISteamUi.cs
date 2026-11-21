using JustModIt.Manager.Core.Games;

namespace JustModIt.Manager.Core.Steam;

/// <summary>How a request to start Steam again was taken.</summary>
public enum SteamRestartResult
{
    /// <summary>Steam was asked to close, and starts again once it has.</summary>
    Restarting,

    /// <summary>Refused, since a Steam game runs and closing Steam would close it.</summary>
    GameRunning,

    /// <summary>Steam could not be asked to close.</summary>
    Failed,
}

/// <summary>Why a game's launch options were not changed.</summary>
public enum LaunchOptionsFailure
{
    /// <summary>Steam's interface cannot be reached now, e.g. since Steam is closed.</summary>
    Unreachable,

    /// <summary>Steam's interface refused the change or did not answer.</summary>
    Failed,
}

/// <summary>A game's launch options after a change, or why they were not changed.</summary>
/// <param name="Options">The game's launch options as Steam has them after the change; null when it failed.</param>
/// <param name="Failure">Why nothing changed; null when the change was made.</param>
public sealed record LaunchOptionsResult(string? Options, LaunchOptionsFailure? Failure = null);

/// <summary>
/// The app's way into Steam's interface, through its Millennium plugin or the classic mode, which shows achievement texts
/// and changes launch options.
/// </summary>
public interface ISteamUi
{
    /// <summary>What the app knows of its way into Steam's interface now.</summary>
    SteamUiStatus Status { get; }

    /// <summary>Raised with the new status whenever it changes.</summary>
    event EventHandler<SteamUiStatus>? Changed;

    /// <summary>
    /// Asks Steam to close and starts it again once it has, so it loads the plugin or opens the classic mode's port; returns
    /// as soon as Steam was asked, since closing it may take longer than a request is answered in, and what follows reaches
    /// <see cref="Status"/>.
    /// </summary>
    SteamRestartResult RestartSteam();

    /// <summary>
    /// Adds options a game's launch options lack and takes out those they have, leaving the player's other options as they
    /// are; <see cref="Status"/> holds the new options before the returned task completes.
    /// </summary>
    /// <param name="game">The Steam game.</param>
    /// <param name="add">The options to add where they are missing.</param>
    /// <param name="remove">The options to take out where they are present.</param>
    /// <param name="cancellationToken">Stops the change.</param>
    Task<LaunchOptionsResult> ChangeLaunchOptionsAsync(GameKey game, IReadOnlyList<string> add, IReadOnlyList<string> remove,
        CancellationToken cancellationToken);

    /// <summary>
    /// Stops changing Steam's folder and interface for good, once a change under way has ended, and takes out of Steam's folder
    /// what the app put there, as uninstalling the app does.
    /// </summary>
    Task WithdrawAsync();
}
