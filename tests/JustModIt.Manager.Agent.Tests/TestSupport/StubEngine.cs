using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.Installation;

namespace JustModIt.Manager.Agent.Tests.TestSupport;

/// <summary>
/// An engine without installs or leftovers whose recovery is done at once; a test overrides what its jobs need, and the rest is
/// not supported.
/// </summary>
internal class StubEngine : IInstallEngine
{
    public virtual Task<IReadOnlyList<RecoveredOperation>> RecoverAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<RecoveredOperation>>([]);

    public virtual Task<IReadOnlyList<InstallRecord>> LoadRecordsAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<InstallRecord>>([]);

    public Task<IReadOnlyList<LeftoverBackup>> FindLeftoverBackupsAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<LeftoverBackup>>([]);

    public Task<bool> RemoveLeftoverBackupsAsync(Guid recordId, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<IReadOnlyList<GameManagedByOtherAppIssue>> FindOtherAppsAsync(string gameDirectory, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<GameManagedByOtherAppIssue>>([]);

    public Task<PreflightReport> CheckInstallAsync(InstallRequest request, IProgress<InstallProgress>? progress,
        CancellationToken cancellationToken) => throw new NotSupportedException();

    public virtual Task<InstallResult> InstallAsync(InstallRequest request, IProgress<InstallProgress>? progress,
        CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<PreflightReport> PredictReplacementAsync(Guid recordId, InstallRequest replacement,
        IProgress<InstallProgress>? progress, CancellationToken cancellationToken) => throw new NotSupportedException();

    public virtual Task<UninstallResult> UninstallAsync(Guid recordId, IReadOnlyList<string>? replacedBy, IProgress<InstallProgress>? progress,
        CancellationToken cancellationToken) => throw new NotSupportedException();

    public virtual Task<VerificationResult> VerifyAsync(Guid recordId, IProgress<InstallProgress>? progress,
        CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<InstallRecord> MoveBackupsAsync(Guid recordId, BackupChoice destination, IProgress<InstallProgress>? progress,
        CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<InstallRecord?> AdoptMovedAsync(Guid recordId, InstalledGame game, IProgress<InstallProgress>? progress,
        CancellationToken cancellationToken) => throw new NotSupportedException();

    public virtual Task<ForgetOutcome> ForgetIfGoneAsync(Guid recordId, bool keepBackups, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<InstalledFiles> LoadFilesAsync(Guid recordId, CancellationToken cancellationToken) =>
        throw new NotSupportedException();
}
