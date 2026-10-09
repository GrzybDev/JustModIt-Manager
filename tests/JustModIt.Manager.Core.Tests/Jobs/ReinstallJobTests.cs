using JustModIt.Manager.Core.Catalog;
using JustModIt.Manager.Core.Installation;
using JustModIt.Manager.Core.Jobs;
using JustModIt.Manager.Core.Tests.TestSupport;
using static JustModIt.Manager.Core.Tests.TestSupport.TestProjects;

namespace JustModIt.Manager.Core.Tests.Jobs;

public sealed class ReinstallJobTests
{
    [Fact]
    public async Task An_update_downloads_first_checks_the_fit_then_replaces_the_installed_release()
    {
        await using var jobs = await JobsHarness.CreateAsync();
        var record = jobs.Installed();
        jobs.PublishReleaseTwo();

        var result = await jobs.RunAsync(new UpdateJobRequest(record));

        result.Outcome.Should().Be(JobOutcome.Succeeded);
        result.RemovedRecord.Should().Be(record.Id);
        result.Installed!.Source!.ReleaseId.Should().Be(ReleaseTwo.ToString("D"));
        result.Installed.Source.ReleaseLabel.Should().Be("v2");
        jobs.Downloader.Downloads.Should().Equal(FileTwo);
        jobs.Engine.Calls.Should().Equal("recover", "predict", "uninstall", "install " + FileTwo.ToString("D") + ".zip");
        jobs.Stages(result.Job.Id).Should().ContainInOrder(JobStage.Preparing, JobStage.Downloading, JobStage.Checking,
            JobStage.Uninstalling, JobStage.Installing);
    }

    [Fact]
    public async Task An_update_asks_for_the_project_where_the_site_last_showed_it()
    {
        await using var jobs = await JobsHarness.CreateAsync();
        var record = jobs.Installed();
        var release = Release(ReleaseTwo, 2, File(FileTwo));
        jobs.Catalogue.Add(Page(release: release, teamSlug: "renamed-team"));
        jobs.Catalogue.Publish(CatalogueState.Fresh, Entry(release: release, teamSlug: "renamed-team"));

        var result = await jobs.RunAsync(new UpdateJobRequest(record));

        jobs.Catalogue.PageRequests.Should().Equal(Locator(teamSlug: "renamed-team"));
        result.Installed!.Source!.TeamSlug.Should().Be("renamed-team");
    }

    [Fact]
    public async Task A_previous_release_downloaded_again_comes_from_where_the_site_last_showed_the_project()
    {
        await using var jobs = await JobsHarness.CreateAsync();
        var record = jobs.Installed();
        jobs.Downloader.Cached.Clear();
        var release = Release(ReleaseTwo, 2, File(FileTwo));
        jobs.Catalogue.Add(Page(release: release, teamSlug: "renamed-team"));
        jobs.Catalogue.Publish(CatalogueState.Fresh, Entry(release: release, teamSlug: "renamed-team"));
        jobs.Engine.NextInstalls.Enqueue((_, _, _) => throw new InstallFailedException(rolledBack: true, new IOException("Locked meanwhile.")));

        var result = await jobs.RunAsync(new UpdateJobRequest(record));

        result.RestoredPrevious.Should().BeTrue();
        jobs.Downloader.DownloadedFrom.Select(locator => (locator.TeamSlug, locator.FileUuid))
            .Should().Equal(("renamed-team", FileTwo), ("renamed-team", FileOne));
    }

    [Fact]
    public async Task A_repair_installs_the_current_release_again_from_the_kept_package()
    {
        await using var jobs = await JobsHarness.CreateAsync();
        var record = jobs.Installed();

        var result = await jobs.RunAsync(new RepairJobRequest(record));

        result.Outcome.Should().Be(JobOutcome.Succeeded);
        result.Installed!.Source!.ReleaseFileId.Should().Be(FileOne.ToString("D"));
        jobs.Downloader.Downloads.Should().BeEmpty("the installed release's package is kept");
    }

    [Fact]
    public async Task The_player_may_take_the_recommended_variant_when_the_installed_one_is_gone()
    {
        await using var jobs = await JobsHarness.CreateAsync();
        var record = jobs.Installed("Text and voice");
        jobs.PublishReleaseTwo(File(FileTwo));

        var result = await jobs.RunAsync(new UpdateJobRequest(record));

        result.Installed!.Source!.VariantName.Should().Be("Text only");
    }

    [Fact]
    public async Task An_automatic_update_never_swaps_the_installed_variant()
    {
        await using var jobs = await JobsHarness.CreateAsync();
        var record = jobs.Installed("Text and voice");
        jobs.PublishReleaseTwo(File(FileTwo));

        var result = await jobs.RunAsync(new UpdateJobRequest(record, JobOrigin.Automatic));

        result.Failure!.Reason.Should().Be(JobFailureReason.NoPackage);
        jobs.Engine.Calls.Should().NotContain("uninstall");
        jobs.Engine.Records.Should().ContainKey(record.Id);
    }

    [Fact]
    public async Task A_new_release_made_for_another_game_version_removes_nothing()
    {
        await using var jobs = await JobsHarness.CreateAsync();
        var record = jobs.Installed();
        jobs.PublishReleaseTwo();
        jobs.Engine.Prediction = new PreflightReport([new FileMismatchIssue("data/strings.pak", AlreadyPatched: false)], []);

        var result = await jobs.RunAsync(new UpdateJobRequest(record));

        result.Failure.Should().BeEquivalentTo(new JobFailure(JobFailureReason.NewReleaseIncompatible) { Files = ["data/strings.pak"] });
        result.RemovedPrevious.Should().BeFalse();
        result.RestoredPrevious.Should().BeFalse();
        jobs.Engine.Calls.Should().NotContain("uninstall");
    }

    [Fact]
    public async Task A_repair_that_no_longer_fits_the_game_leaves_the_install_as_it_is()
    {
        await using var jobs = await JobsHarness.CreateAsync();
        var record = jobs.Installed();
        jobs.Engine.Prediction = new PreflightReport([new MissingFileIssue("data/strings.pak")], []);

        var result = await jobs.RunAsync(new RepairJobRequest(record));

        result.Failure!.Reason.Should().Be(JobFailureReason.UnsupportedGameVersion);
        jobs.Engine.Records.Should().ContainKey(record.Id);
    }

    [Theory]
    [InlineData(JobKind.Update)]
    [InlineData(JobKind.Repair)]
    public async Task A_failure_after_the_uninstall_puts_the_previous_release_back(JobKind kind)
    {
        await using var jobs = await JobsHarness.CreateAsync();
        var record = jobs.Installed();
        jobs.PublishReleaseTwo();
        jobs.Engine.NextInstalls.Enqueue((_, _, _) => throw new InstallFailedException(rolledBack: true, new IOException("Locked meanwhile.")));

        var result = await jobs.RunAsync(kind == JobKind.Update ? new UpdateJobRequest(record) : new RepairJobRequest(record));

        result.Outcome.Should().Be(JobOutcome.Failed);
        result.Failure!.Reason.Should().Be(JobFailureReason.DiskError);
        result.RemovedPrevious.Should().BeTrue();
        result.RestoredPrevious.Should().BeTrue();
        result.RemovedRecord.Should().Be(record.Id);
        result.Installed!.Source!.ReleaseFileId.Should().Be(FileOne.ToString("D"));
        jobs.Engine.Installs.Last().PackagePath.Should().Be(jobs.Downloader.PathOf(FileOne));
        jobs.Downloader.Downloads.Should().NotContain(FileOne, "the previous package was kept for exactly this");
        jobs.Stages(result.Job.Id).Should().EndWith(JobStage.RestoringPrevious);
    }

    [Fact]
    public async Task A_cancel_after_the_uninstall_still_puts_the_previous_release_back()
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
        await installing.Task.WaitAsync(TimeSpan.FromSeconds(10), JobsHarness.Token);

        jobs.Runner.Cancel(handle.Job.Id);
        var result = await handle.Completion.WaitAsync(TimeSpan.FromSeconds(10), JobsHarness.Token);

        result.Outcome.Should().Be(JobOutcome.Cancelled);
        result.CancelledByPlayer.Should().BeTrue();
        result.RestoredPrevious.Should().BeTrue();
        result.Installed!.Source!.ReleaseId.Should().Be(ReleaseOne.ToString("D"));
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task An_exit_puts_back_what_a_cancelled_update_removed_only_while_it_waits_for_the_jobs(bool cancelPlayerJobs,
        bool restored)
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
        var handle = jobs.Runner.Start(new UpdateJobRequest(record, JobOrigin.Automatic));
        await installing.Task.WaitAsync(TimeSpan.FromSeconds(10), JobsHarness.Token);

        jobs.Runner.Shutdown(cancelPlayerJobs);
        var result = await handle.Completion.WaitAsync(TimeSpan.FromSeconds(10), JobsHarness.Token);

        result.Outcome.Should().Be(JobOutcome.Cancelled);
        result.CancelledByPlayer.Should().BeFalse();
        result.RestoredPrevious.Should().Be(restored);
    }

    [Fact]
    public async Task Tells_when_the_previous_release_could_not_be_put_back()
    {
        await using var jobs = await JobsHarness.CreateAsync();
        var record = jobs.Installed();
        jobs.PublishReleaseTwo();
        jobs.Engine.NextInstalls.Enqueue((_, _, _) => throw new InstallFailedException(rolledBack: true, new InvalidOperationException("Odd.")));
        jobs.Engine.NextInstalls.Enqueue((_, _, _) => throw new InstallBlockedException([new FileLockedIssue("data/strings.pak", ["Viewer"])]));

        var result = await jobs.RunAsync(new UpdateJobRequest(record));

        result.Failure!.Reason.Should().Be(JobFailureReason.InstallFailed);
        result.LostPrevious.Should().BeTrue();
        result.Installed.Should().BeNull();
        result.RemovedRecord.Should().Be(record.Id);
    }

    [Fact]
    public async Task An_install_without_a_known_project_cannot_be_updated()
    {
        await using var jobs = await JobsHarness.CreateAsync();
        var record = jobs.Engine.Add(Record(source: new InstallSource { ProjectName = "Hand-made" }));

        var result = await jobs.RunAsync(new UpdateJobRequest(record));

        result.Failure!.Reason.Should().Be(JobFailureReason.ProjectNotFound);
        jobs.Catalogue.PageRequests.Should().BeEmpty();
    }
}
