using JustModIt.Manager.Agent.Updates;

namespace JustModIt.Manager.Agent.Tests.TestSupport;

/// <summary>Releases that exist only in the test: what a check finds, and how the app would restart into them.</summary>
internal sealed class FakeUpdater : IAppUpdater
{
    private int _checks;
    private string? _pendingVersion;

    public bool IsSupported { get; init; } = true;

    public string? PendingVersion
    {
        get => PendingUnreadable ? throw new IOException("The test's packages folder is locked.") : _pendingVersion;
        set => _pendingVersion = value;
    }

    /// <summary>Reading what was downloaded fails, like a locked packages folder.</summary>
    public bool PendingUnreadable { get; init; }

    /// <summary>What the next check finds; null when this copy is the newest.</summary>
    public string? Newest { get; set; }

    public bool CheckFails { get; set; }

    public int Checks => Volatile.Read(ref _checks);

    public TaskCompletionSource<IReadOnlyList<string>> Applied { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Holds the first check until it is completed, like a slow feed.</summary>
    public TaskCompletionSource? SlowFirstCheck { get; init; }

    public TaskCompletionSource FirstCheckStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public async Task<string?> CheckAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Increment(ref _checks) == 1 && SlowFirstCheck is { } slow)
        {
            FirstCheckStarted.TrySetResult();
            await slow.Task.WaitAsync(cancellationToken);
        }

        return CheckFails ? throw new HttpRequestException("The test feed is offline.") : Newest;
    }

    public Task DownloadAsync(Action<int> progress, CancellationToken cancellationToken)
    {
        progress(50);
        progress(100);
        PendingVersion = Newest;
        return Task.CompletedTask;
    }

    public void ApplyAfterExit(IReadOnlyList<string> restartArgs) => Applied.TrySetResult(restartArgs);
}
