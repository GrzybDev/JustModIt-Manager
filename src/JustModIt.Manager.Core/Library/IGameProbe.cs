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
/// <param name="RunningProcesses">Programs running from the game's folder, which make it <see cref="GameInstallState.Running"/>.</param>
public sealed record GameProbe(GameKey Key, InstalledGame? Game, ValueList<RunningProcess> RunningProcesses)
{
    public bool IsGone => Game is null;

    /// <summary>The store reports the game installed and idle, and nothing runs from its folder.</summary>
    public bool CanWrite => Game is { IsReady: true } && RunningProcesses.Count == 0;
}
