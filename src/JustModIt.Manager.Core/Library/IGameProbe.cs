using JustModIt.Manager.Core.Collections;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.Safety;

namespace JustModIt.Manager.Core.Library;

public interface IGameProbe
{
    /// <summary>
    /// Reads one game's store records again and looks for processes running from its folder right before its files are
    /// changed, since the snapshot alone is never trusted for writing.
    /// </summary>
    /// <param name="key">The game to probe.</param>
    /// <param name="cancellationToken">Cancels the probe.</param>
    Task<GameProbe> ProbeAsync(GameKey key, CancellationToken cancellationToken);
}

/// <summary>A game as its store describes it at this moment.</summary>
/// <param name="Key">The game probed.</param>
/// <param name="Game">Null when the store no longer lists the game.</param>
/// <param name="RunningProcesses">
/// Programs running from the game's folder, which make an installed game <see cref="GameInstallState.Running"/> unless
/// the store waits to update or repair it.
/// </param>
public sealed record GameProbe(GameKey Key, InstalledGame? Game, ValueList<RunningProcess> RunningProcesses)
{
    /// <summary>
    /// Whether the store's state lets the game's files change, once nothing runs from its folder any more: the game is
    /// installed, or it runs and its processes were found.
    /// </summary>
    /// <param name="forUninstall">
    /// An uninstall, which may also run while the store waits to update or repair the game: putting the originals back is
    /// just what either needs, and the uninstall leaves alone whatever the store changed meanwhile.
    /// </param>
    public bool AllowsChange(bool forUninstall) =>
        Game is { IsStale: false } game
        && (game.State == GameInstallState.Installed
            || (game.State == GameInstallState.Running && RunningProcesses.Count > 0)
            || (forUninstall && game.State is GameInstallState.UpdatePending or GameInstallState.Broken));
}
