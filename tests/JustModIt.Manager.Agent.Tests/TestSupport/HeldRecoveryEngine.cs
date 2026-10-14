using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.Installation;
using JustModIt.Manager.Core.Jobs;

namespace JustModIt.Manager.Agent.Tests.TestSupport;

/// <summary>
/// An engine without installs whose crash recovery, which every job waits for, waits for the test, so a job keeps the
/// agent busy until <see cref="Release"/> and then fails, since no store reports its game.
/// </summary>
internal sealed class HeldRecoveryEngine : StubEngine
{
    private readonly TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public void Release() => _released.TrySetResult();

    /// <summary>Starts the player's job on a game no store reports.</summary>
    /// <param name="agent">A started agent composed with a held engine, so the job runs until it is released.</param>
    public static async Task<JobHandle> StartJobAsync(AgentHarness agent) =>
        (await agent.ServiceAsync<JobRunner>()).Start(new TestInstallJobRequest(new GameKey(StoreId.Steam, "1000"), "Example Game",
            [Path.Combine(agent.Directory, "example.zip")]));

    public override async Task<IReadOnlyList<RecoveredOperation>> RecoverAsync(CancellationToken cancellationToken)
    {
        await _released.Task.WaitAsync(cancellationToken);
        return [];
    }
}
