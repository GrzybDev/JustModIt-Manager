using JustModIt.Manager.Core.Platform;

namespace JustModIt.Manager.Core.Installation;

/// <summary>
/// Keeps this app's owner file in a game's hidden folder while the app has an install or an unfinished operation on the
/// game, and only then, so other apps leave the game alone exactly as long as this one manages files in it.
/// </summary>
/// <param name="store">Tells whether other installs or operations of this app are on a game.</param>
/// <param name="options">The app's instance and hidden folder names.</param>
internal sealed class GameFolderOwnership(InstallRecordStore store, InstallEngineOptions options)
{
    /// <summary>Marks the game as managed by this app.</summary>
    /// <param name="gameRoot">The game's folder.</param>
    /// <param name="cancellationToken">Stops the write.</param>
    public Task ClaimAsync(string gameRoot, CancellationToken cancellationToken) =>
        AppFolders.ClaimAsync(HiddenDirectoryOf(gameRoot), options.InstanceName, cancellationToken);

    /// <summary>
    /// Gives the game up unless another install or unfinished operation of this app is on it; called before the leaving
    /// operation's journal goes, so a crash or a failure here leaves the journal that brings recovery back to try again.
    /// </summary>
    /// <param name="gameRoot">The game's folder.</param>
    /// <param name="leaving">The record id of the install or operation that leaves the game.</param>
    public async Task ReleaseAsync(string gameRoot, Guid leaving)
    {
        if (!await store.HasOtherOnAsync(gameRoot, leaving, CancellationToken.None).ConfigureAwait(false))
        {
            AppFolders.Release(HiddenDirectoryOf(gameRoot), options.InstanceName);
        }
    }

    /// <summary>The other apps that manage the game or a game inside it that the operation reaches.</summary>
    /// <param name="gameRoot">The game's folder.</param>
    /// <param name="paths">Full paths in the game the operation changes, which may lie in a game another app manages.</param>
    public IReadOnlyList<GameManagedByOtherAppIssue> OtherApps(string gameRoot, IEnumerable<string>? paths = null) =>
        [.. AppFolders.FindOthers(PhysicalFileSystem.Instance, gameRoot, options.InstanceName, paths)
            .Select(other => new GameManagedByOtherAppIssue(other.App, other.Directory))];

    private string HiddenDirectoryOf(string gameRoot) => Path.Join(gameRoot, options.HiddenFolderName);
}
