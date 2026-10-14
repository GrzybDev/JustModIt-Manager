using JustModIt.Manager.Core.Catalog;
using JustModIt.Manager.Core.Installation;
using JustModIt.Manager.Core.Jobs;
using JustModIt.Manager.Core.Packages;
using JustModIt.Manager.Core.Tests.TestSupport;
using static JustModIt.Manager.Core.Tests.TestSupport.TestProjects;

namespace JustModIt.Manager.Core.Tests.Jobs;

public sealed class ReplacementMarkerTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_replacement_keeps_its_marker_from_the_uninstall_until_the_new_install_commits()
    {
        await using var jobs = await JobsHarness.CreateAsync();
        var record = jobs.Installed();
        jobs.PublishReleaseTwo();
        IReadOnlyList<ReplacementMarker>? whileInstalling = null;
        jobs.Engine.NextInstalls.Enqueue(async (request, _, _) =>
        {
            whileInstalling = await jobs.Markers.LoadAllAsync(Token);
            return await jobs.Engine.InstallNowAsync(request);
        });

        var result = await jobs.RunAsync(new UpdateJobRequest(record));

        result.Outcome.Should().Be(JobOutcome.Succeeded);
        whileInstalling.Should().ContainSingle().Which.Should().BeEquivalentTo(
            new ReplacementMarker(record, JobKind.Update, JobOrigin.User, Project));
        jobs.MarkerFiles.Should().BeEmpty();
    }

    [Fact]
    public async Task A_replacement_put_back_after_a_failure_leaves_no_marker()
    {
        await using var jobs = await JobsHarness.CreateAsync();
        var record = jobs.Installed();
        jobs.PublishReleaseTwo();
        jobs.Engine.NextInstalls.Enqueue((_, _, _) => throw new InstallFailedException(rolledBack: true, new IOException("Locked meanwhile.")));

        var result = await jobs.RunAsync(new UpdateJobRequest(record));

        result.RestoredPrevious.Should().BeTrue();
        jobs.MarkerFiles.Should().BeEmpty();
    }

    [Fact]
    public async Task An_exit_that_does_not_wait_for_the_jobs_leaves_putting_the_release_back_to_the_next_start()
    {
        await using var jobs = await JobsHarness.CreateAsync();
        var record = jobs.Installed();
        jobs.PublishReleaseTwo();
        var installing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        jobs.Engine.NextInstalls.Enqueue(async (_, _, token) =>
        {
            installing.SetResult();
            await Task.Delay(Timeout.Infinite, token);
            throw new InvalidOperationException("Unreachable.");
        });
        var handle = jobs.Runner.Start(new UpdateJobRequest(record));
        await installing.Task.WaitAsync(TimeSpan.FromSeconds(10), Token);

        jobs.Runner.Shutdown(cancelPlayerJobs: true);
        var result = await handle.Completion.WaitAsync(TimeSpan.FromSeconds(10), Token);

        result.LostPrevious.Should().BeTrue();
        (await jobs.Markers.LoadAllAsync(Token)).Should().ContainSingle().Which.Previous.Id.Should().Be(record.Id);
    }

    [Fact]
    public async Task The_start_after_a_stop_puts_back_the_install_a_replacement_removed_before_any_other_job()
    {
        await using var jobs = await JobsHarness.CreateAsync(recover: false);
        var record = Record(source: Source(ReleaseOne, FileOne, 1));
        await jobs.Markers.SaveAsync(new ReplacementMarker(record, JobKind.Update, JobOrigin.Automatic, Project), Token);
        jobs.Downloader.Cached[FileOne] = true;
        var putBack = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        jobs.Engine.NextInstalls.Enqueue(async (request, _, _) =>
        {
            await putBack.Task;
            return await jobs.Engine.InstallNowAsync(request);
        });

        await jobs.Runner.RecoverAsync(Token);
        var refused = await jobs.RunAsync(JobsHarness.Install());
        putBack.SetResult();
        await Eventually.HoldsAsync(() => !jobs.Results.IsEmpty, "the restore ends");

        refused.Failure!.Reason.Should().Be(JobFailureReason.AlreadyRunning);
        var restore = jobs.Results.Single();
        restore.Outcome.Should().Be(JobOutcome.Succeeded);
        restore.RestoredPrevious.Should().BeTrue();
        restore.RemovedRecord.Should().Be(record.Id);
        restore.Installed!.Source!.ReleaseFileId.Should().Be(FileOne.ToString("D"));
        restore.Job.Should().BeEquivalentTo(new { Kind = JobKind.Update, Origin = JobOrigin.Automatic, InstallId = record.Id },
            options => options.ExcludingMissingMembers());
        jobs.Engine.Installs.Single().PackagePath.Should().Be(jobs.Downloader.PathOf(FileOne));
        jobs.Downloader.Downloads.Should().BeEmpty("the previous package was kept for exactly this");
        jobs.Stages(restore.Job.Id).Should().NotContain(JobStage.Waiting).And.EndWith(JobStage.RestoringPrevious);
        jobs.MarkerFiles.Should().BeEmpty();
    }

    [Theory]
    [InlineData("the uninstall was undone")]
    [InlineData("the new install committed")]
    [InlineData("another project replaced it")]
    public async Task The_start_only_drops_the_marker_of_a_replacement_that_ended(string ending)
    {
        await using var jobs = await JobsHarness.CreateAsync(recover: false);
        var record = Record(source: Source(ReleaseOne, FileOne, 1));
        await jobs.Markers.SaveAsync(new ReplacementMarker(record, JobKind.Install, JobOrigin.User,
            ending == "another project replaced it" ? OtherProject : Project), Token);
        jobs.Engine.Add(ending switch
        {
            "the uninstall was undone" => record,
            "the new install committed" => Record(source: Source(ReleaseTwo, FileTwo, 2)),
            _ => Record(source: Source(ReleaseTwo, FileTwo, 1, project: OtherProject)),
        });

        await jobs.Runner.RecoverAsync(Token);

        jobs.Runner.Active.Should().BeEmpty();
        jobs.Engine.Installs.Should().BeEmpty();
        jobs.MarkerFiles.Should().BeEmpty();
    }

    [Fact]
    public async Task A_release_the_start_cannot_put_back_stays_lost_and_its_marker_goes()
    {
        await using var jobs = await JobsHarness.CreateAsync(recover: false);
        var record = Record(source: Source(ReleaseOne, FileOne, 1));
        await jobs.Markers.SaveAsync(new ReplacementMarker(record, JobKind.Repair, JobOrigin.User, Project), Token);
        jobs.Downloader.Failure = new CatalogueException(CatalogueErrorReason.Offline, "No connection.");

        await jobs.Runner.RecoverAsync(Token);
        await Eventually.HoldsAsync(() => !jobs.Results.IsEmpty, "the restore ends");

        var result = jobs.Results.Single();
        result.Outcome.Should().Be(JobOutcome.Cancelled);
        result.LostPrevious.Should().BeTrue();
        result.Job.Kind.Should().Be(JobKind.Repair);
        jobs.MarkerFiles.Should().BeEmpty();
    }

    [Fact]
    public async Task The_start_leaves_alone_the_marker_of_a_record_only_a_newer_app_can_read()
    {
        await using var jobs = await JobsHarness.CreateAsync(recover: false);
        var record = Record(source: Source(ReleaseOne, FileOne, 1)) with { FormatVersion = InstallRecord.CurrentFormatVersion + 1 };
        await jobs.Markers.SaveAsync(new ReplacementMarker(record, JobKind.Update, JobOrigin.User, Project), Token);
        jobs.Downloader.Cached[FileOne] = true;

        await jobs.Runner.RecoverAsync(Token);

        (await jobs.Markers.LoadAllAsync(Token)).Should().BeEmpty();
        jobs.Runner.Active.Should().BeEmpty();
        jobs.Engine.Installs.Should().BeEmpty();
        jobs.MarkerFiles.Should().ContainSingle("the newer app puts its install back itself");
    }

    [Fact]
    public async Task A_marker_keeps_the_replaced_record_whole()
    {
        await using var jobs = await JobsHarness.CreateAsync(recover: false);
        var record = Record(source: Source(ReleaseOne, FileOne, 1)) with
        {
            GameBuildId = "41",
            Package = new InstalledPackage(PackageKind.Manifest, "package.examplemod", Guid.NewGuid(), "Example", "1.2"),
        };
        var marker = new ReplacementMarker(record, JobKind.Install, JobOrigin.User, OtherProject);

        await jobs.Markers.SaveAsync(marker, Token);

        (await jobs.Markers.LoadAllAsync(Token)).Should().ContainSingle().Which.Should().BeEquivalentTo(marker);
    }
}
