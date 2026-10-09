using JustModIt.Manager.Core.Tests.TestSupport;

namespace JustModIt.Manager.Core.Tests.Installation;

public sealed class OperationQueueTests : IAsyncDisposable
{
    private readonly InstallHarness _harness = new();

    public ValueTask DisposeAsync() => _harness.DisposeAsync();

    [Fact]
    public async Task Runs_one_operation_at_a_time()
    {
        var first = _harness.InstallAsync(_harness.ManifestPackage(new TestPackage().Copy("a.txt", "a")));
        var second = _harness.InstallAsync(_harness.ManifestPackage(new TestPackage().Copy("b.txt", "b")));

        await Task.WhenAll(first, second);

        (await _harness.Engine.LoadRecordsAsync(InstallHarness.Token)).Should().HaveCount(2);
    }

    [Fact]
    public async Task Drops_an_operation_cancelled_while_it_waits_for_its_turn()
    {
        using var release = new ManualResetEventSlim();
        _harness.BeforeChange = (_, _) => release.Wait(InstallHarness.Token);
        var first = _harness.InstallAsync(_harness.ManifestPackage(new TestPackage().Copy("a.txt", "a")));
        using var cancellation = new CancellationTokenSource();
        var second = _harness.InstallAsync(_harness.ManifestPackage(new TestPackage().Copy("b.txt", "b")), cancellationToken: cancellation.Token);

        await cancellation.CancelAsync();

        try
        {
            var waiting = () => second.WaitAsync(TimeSpan.FromSeconds(30), InstallHarness.Token);
            await waiting.Should().ThrowAsync<OperationCanceledException>();
            first.IsCompleted.Should().BeFalse("the first operation still holds the queue");
        }
        finally
        {
            release.Set();
        }

        await first;
        File.Exists(_harness.GameFile("b.txt")).Should().BeFalse();
    }

    [Fact]
    public async Task Refuses_work_after_it_was_disposed()
    {
        var engine = _harness.CreateEngine();
        await engine.DisposeAsync();

        var recover = () => engine.RecoverAsync(InstallHarness.Token);

        await recover.Should().ThrowAsync<ObjectDisposedException>();
    }
}
