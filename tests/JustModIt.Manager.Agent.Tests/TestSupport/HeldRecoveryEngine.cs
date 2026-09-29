using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.Installation;
using JustModIt.Manager.Core.Jobs;

namespace JustModIt.Manager.Agent.Tests.TestSupport;

/// <summary>
/// An engine without installs whose crash recovery, which every job waits for, waits for the test, so a job keeps the
/// agent busy until <see cref="Release"/> and then fails, since no store reports its game.
/// </summary>
internal sealed class HeldRecoveryEngine : IInstallEngine
{
    private readonly TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public void Release() => _released.TrySetResult();

    /// <summary>Starts the player's job on a game no store reports.</summary>
    /// <param name="agent">A started agent composed with a held engine, so the job runs until it is released.</param>
    public static async Task<JobHandle> StartJobAsync(AgentHarness agent) =>
        (await agent.ServiceAsync<JobRunner>()).Start(new TestInstallJobRequest(new GameKey(StoreId.Steam, "1000"), "Example Game",
            Path.Combine(agent.Directory, "example.zip")));

    public async Task<IReadOnlyList<RecoveredOperation>> RecoverAsync(CancellationToken cancellationToken)
    {
        await _released.Task.WaitAsync(cancellationToken);
        return [];
    }

    public Task<IReadOnlyList<InstallRecord>> LoadRecordsAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<InstallRecord>>([]);

    public Task<IReadOnlyList<LeftoverBackup>> FindLeftoverBackupsAsync(Func<InstallRecord, bool> isGameGone,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<LeftoverBackup>>([]);

    public Task<IReadOnlyList<GameManagedByOtherAppIssue>> FindOtherAppsAsync(string gameDirectory, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<PreflightReport> CheckInstallAsync(InstallRequest request, IProgress<InstallProgress>? progress,
        CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<InstallResult> InstallAsync(InstallRequest request, IProgress<InstallProgress>? progress,
        CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<PreflightReport> PredictReplacementAsync(Guid recordId, InstallRequest replacement,
        IProgress<InstallProgress>? progress, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<UninstallResult> UninstallAsync(Guid recordId, IProgress<InstallProgress>? progress,
        CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<VerificationResult> VerifyAsync(Guid recordId, IProgress<InstallProgress>? progress,
        CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<InstallRecord> MoveBackupsAsync(Guid recordId, BackupChoice destination, IProgress<InstallProgress>? progress,
        CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<InstallRecord?> AdoptMovedAsync(Guid recordId, InstalledGame game, IProgress<InstallProgress>? progress,
        CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task ForgetAsync(Guid recordId, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<bool> ForgetIfGoneAsync(Guid recordId, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<InstalledFiles> LoadFilesAsync(Guid recordId, CancellationToken cancellationToken) =>
        throw new NotSupportedException();
}
