using System.Runtime.Versioning;
using JustModIt.Manager.Core.Installation;
using JustModIt.Manager.Core.Packages;
using JustModIt.Manager.Core.Patching;
using JustModIt.Manager.Core.Tests.TestSupport;

namespace JustModIt.Manager.Core.Tests.Installation;

public sealed class RollbackTests : IAsyncDisposable
{
    private readonly InstallHarness _harness = new();
    private readonly Dictionary<string, string> _before;

    public RollbackTests() => _before = _harness.Snapshot();

    public ValueTask DisposeAsync() => _harness.DisposeAsync();

    [Fact]
    public async Task Undoes_everything_when_a_later_step_fails()
    {
        var patch = await TestPatch.CreateAsync(InstallHarness.OriginalPak, InstallHarness.ChangedPak);
        var broken = patch with { Info = patch.Info with { TargetSha256 = new string('0', 64) } };

        var install = () => _harness.InstallAsync(_harness.ManifestPackage(new TestPackage()
            .Rename("bin/library.dll", "bin/library_original.dll")
            .Copy("bin/library.dll", "proxy")
            .Delete("movies/intro.bik")
            .Copy("data/lang/text.txt", "Text")
            .Patch("data/strings.pak", broken)));

        var failure = (await install.Should().ThrowAsync<InstallFailedException>()).Which;
        failure.RolledBack.Should().BeTrue();
        failure.InnerException.Should().BeOfType<PatchException>().Which.Error.Should().Be(PatchError.OutputMismatch);
        _harness.Snapshot().Should().BeEquivalentTo(_before);
        _harness.AppFiles().Should().BeEmpty();
    }

    [Fact]
    public async Task Undoes_everything_when_a_payload_does_not_match_its_hash()
    {
        var package = _harness.ManifestPackage(new TestPackage()
            .Copy("readme.txt", "Changed")
            .Entry("files/other.txt", "tampered")
            .Operation(new CopyOperation("other.txt", "files/other.txt", 8, new string('0', 64))));

        var install = () => _harness.InstallAsync(package);

        (await install.Should().ThrowAsync<InstallFailedException>()).Which.InnerException.Should()
            .BeOfType<InvalidPackageException>().Which.Error.Should().Be(PackageError.PayloadMismatch);
        _harness.Snapshot().Should().BeEquivalentTo(_before);
    }

    [Fact]
    public async Task Undoes_everything_when_cancelled_part_way()
    {
        using var cancellation = new CancellationTokenSource();
        _harness.BeforeChange = (kind, _) =>
        {
            if (kind == ChangeKind.Write)
            {
                cancellation.Cancel();
            }
        };

        var install = () => _harness.InstallAsync(_harness.ManifestPackage(new TestPackage()
            .Copy("readme.txt", "Changed")
            .Copy("a.txt", "a")
            .Copy("readme.txt", "Changed again")), cancellationToken: cancellation.Token);

        await install.Should().ThrowAsync<OperationCanceledException>();
        _harness.Snapshot().Should().BeEquivalentTo(_before);
        _harness.AppFiles().Should().BeEmpty();
    }

    [Fact]
    [SupportedOSPlatform("windows")]
    public async Task Undoes_everything_at_once_when_the_journal_cannot_be_written()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Only Windows refuses writes to a locked range.");
        var package = _harness.ManifestPackage(new TestPackage()
            .Copy("readme.txt", "Changed")
            .Copy("a.txt", "a")
            .Copy("readme.txt", "Changed again"));
        InstallFailedException failure;
        using (_harness.FailJournalWritesFrom(ChangeKind.Write))
        {
            var install = () => _harness.InstallAsync(package);

            failure = (await install.Should().ThrowAsync<InstallFailedException>()).Which;
        }

        failure.RolledBack.Should().BeTrue();
        failure.InnerException.Should().BeOfType<IOException>();
        _harness.Snapshot().Should().BeEquivalentTo(_before);
        _harness.AppFiles().Should().BeEmpty();
    }

    [Fact]
    public async Task Leaves_a_rollback_that_fails_to_recovery()
    {
        _harness.BeforeChange = (kind, _) =>
        {
            switch (kind)
            {
                case ChangeKind.Record:
                    throw new InvalidOperationException("The disk is full.");
                case ChangeKind.Restore:
                    throw new IOException("The file is in use.");
            }
        };

        var install = () => _harness.InstallAsync(_harness.ManifestPackage(new TestPackage()
            .Copy("readme.txt", "Changed")
            .Delete("movies/intro.bik")));

        (await install.Should().ThrowAsync<InstallFailedException>()).Which.RolledBack.Should().BeFalse();
        (await _harness.InstallBlockedAsync(_harness.ManifestPackage(new TestPackage().Copy("a.txt", "a")))).Issues.Should().ContainSingle()
            .Which.Should().BeOfType<RecoveryPendingIssue>();

        _harness.BeforeChange = null;
        var recovered = await _harness.CreateEngine().RecoverAsync(InstallHarness.Token);

        recovered.Should().ContainSingle().Which.Outcome.Should().Be(RecoveryOutcome.RolledBack);
        _harness.Snapshot().Should().BeEquivalentTo(_before);
        _harness.AppFiles().Should().BeEmpty();
    }

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
