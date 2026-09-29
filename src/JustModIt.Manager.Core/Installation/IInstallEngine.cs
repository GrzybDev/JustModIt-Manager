using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.Packages;

namespace JustModIt.Manager.Core.Installation;

/// <inheritdoc cref="InstallEngine"/>
public interface IInstallEngine
{
    /// <summary>Undoes or finishes what a crash interrupted; meant to be queued first after a start.</summary>
    /// <param name="cancellationToken">Stops recovery.</param>
    Task<IReadOnlyList<RecoveredOperation>> RecoverAsync(CancellationToken cancellationToken);

    /// <summary>Everything that would stop the install, and what to tell the player, without changing anything.</summary>
    /// <param name="request">The package and the game.</param>
    /// <param name="progress">Reports the check.</param>
    /// <param name="cancellationToken">Stops the check.</param>
    /// <exception cref="InvalidPackageException">The package is malformed, unsafe or made for a newer app.</exception>
    /// <exception cref="InstallBlockedException">The game folder is gone.</exception>
    Task<PreflightReport> CheckInstallAsync(InstallRequest request, IProgress<InstallProgress>? progress,
        CancellationToken cancellationToken);

    /// <summary>Installs a package as one journaled operation, backing up every original it replaces.</summary>
    /// <param name="request">The package and the game.</param>
    /// <param name="progress">Reports the check and the install.</param>
    /// <param name="cancellationToken">Stops the install; what it changed by then is undone.</param>
    /// <exception cref="InvalidPackageException">The package is malformed, unsafe or made for a newer app.</exception>
    /// <exception cref="InstallBlockedException">Refused before any change.</exception>
    /// <exception cref="InstallFailedException">Failed part-way; the changes were undone unless it says otherwise.</exception>
    Task<InstallResult> InstallAsync(InstallRequest request, IProgress<InstallProgress>? progress,
        CancellationToken cancellationToken);

    /// <summary>
    /// Whether a package would install once the given install is uninstalled, as an update or repair needs to know before
    /// it removes anything; files the install touched count as the uninstall will leave them, all others as they are.
    /// </summary>
    /// <param name="recordId">The install to be replaced.</param>
    /// <param name="replacement">The package to install in its place.</param>
    /// <param name="progress">Reports the check.</param>
    /// <param name="cancellationToken">Stops the check.</param>
    /// <exception cref="InvalidPackageException">The package is malformed, unsafe or made for a newer app.</exception>
    /// <exception cref="InstallBlockedException">The install cannot be read or its game folder is gone.</exception>
    Task<PreflightReport> PredictReplacementAsync(Guid recordId, InstallRequest replacement, IProgress<InstallProgress>? progress,
        CancellationToken cancellationToken);

    /// <summary>Puts an install's originals back and removes what it added, leaving alone files changed since.</summary>
    /// <param name="recordId">The install's record id.</param>
    /// <param name="progress">Reports the uninstall.</param>
    /// <param name="cancellationToken">Stops the uninstall; what it changed by then is undone.</param>
    /// <exception cref="InstallBlockedException">Refused before any change.</exception>
    Task<UninstallResult> UninstallAsync(Guid recordId, IProgress<InstallProgress>? progress, CancellationToken cancellationToken);

    /// <summary>Checks an install's files against what it installed.</summary>
    /// <param name="recordId">The install's record id.</param>
    /// <param name="progress">Reports the check.</param>
    /// <param name="cancellationToken">Stops the check.</param>
    /// <exception cref="InstallBlockedException">The game folder is gone.</exception>
    Task<VerificationResult> VerifyAsync(Guid recordId, IProgress<InstallProgress>? progress, CancellationToken cancellationToken);

    /// <summary>Moves an install's backups to where the settings now keep them.</summary>
    /// <param name="recordId">The install's record id.</param>
    /// <param name="destination">Where the settings now keep originals.</param>
    /// <param name="progress">Reports each backup.</param>
    /// <param name="cancellationToken">Stops the move, which is then undone.</param>
    /// <returns>The install's record with its new backup folder.</returns>
    /// <exception cref="InstallBlockedException">Refused before any change.</exception>
    Task<InstallRecord> MoveBackupsAsync(Guid recordId, BackupChoice destination, IProgress<InstallProgress>? progress,
        CancellationToken cancellationToken);

    /// <summary>
    /// Points an install at the folder its store moved the game to, when the install's files are there, without changing
    /// anything in the game.
    /// </summary>
    /// <param name="recordId">The install's record id.</param>
    /// <param name="game">The game as its store reports it now.</param>
    /// <param name="progress">Reports the check of the install's files.</param>
    /// <param name="cancellationToken">Stops the check.</param>
    /// <returns>The install at its new folder; null when the files there are not the install's.</returns>
    /// <exception cref="InstallBlockedException">The install or the new folder cannot be used.</exception>
    Task<InstallRecord?> AdoptMovedAsync(Guid recordId, InstalledGame game, IProgress<InstallProgress>? progress,
        CancellationToken cancellationToken);

    /// <summary>Drops a record and the backups it lists without touching the game, e.g. when the game is gone for good.</summary>
    /// <exception cref="InstallBlockedException">An interrupted operation on it has to be recovered first.</exception>
    Task ForgetAsync(Guid recordId, CancellationToken cancellationToken);

    /// <summary>
    /// Forgets an install whose game folder is gone for good: its drive is attached and the folder is not on it. A folder
    /// on an unplugged drive is kept, since forgetting it would strand the installed files.
    /// </summary>
    /// <returns>Whether the install was forgotten.</returns>
    Task<bool> ForgetIfGoneAsync(Guid recordId, CancellationToken cancellationToken);

    /// <summary>The installs the app can read.</summary>
    /// <param name="cancellationToken">Stops the read.</param>
    Task<IReadOnlyList<InstallRecord>> LoadRecordsAsync(CancellationToken cancellationToken);

    /// <summary>The files an install touched.</summary>
    /// <param name="recordId">The install's record id.</param>
    /// <param name="cancellationToken">Stops the read.</param>
    Task<InstalledFiles> LoadFilesAsync(Guid recordId, CancellationToken cancellationToken);

    /// <summary>Backups that outlived their game, with their size, so the player can remove them.</summary>
    /// <param name="isGameGone">
    /// Whether the store confirms the game is gone, from a fresh look with every library online; an unplugged drive is
    /// not a removed game.
    /// </param>
    /// <param name="cancellationToken">Stops the search.</param>
    Task<IReadOnlyList<LeftoverBackup>> FindLeftoverBackupsAsync(Func<InstallRecord, bool> isGameGone,
        CancellationToken cancellationToken);
}
