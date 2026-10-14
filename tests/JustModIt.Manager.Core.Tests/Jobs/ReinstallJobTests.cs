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
    public async Task An_update_hands_the_files_the_software_changes_only_to_the_packages_its_prediction_keeps()
    {
        await using var jobs = await JobsHarness.CreateAsync();
        var record = jobs.Installed();
        jobs.PublishReleaseTwo();
        jobs.Engine.Prediction = new PreflightReport([], [])
        {
            LeftOut = [new LeftOutPart(0, new InstallPart(jobs.Downloader.PathOf(FileTwo)), PartLeftOutReason.NewerApp)],
        };

        var result = await jobs.RunAsync(new UpdateJobRequest(record));

        result.Outcome.Should().Be(JobOutcome.Succeeded);
        jobs.Engine.ReplacedBy.Should().ContainSingle().Which.Should().BeEmpty("a package left out takes nothing over");
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
    public async Task The_player_may_take_the_releases_base_when_the_installed_variant_is_gone()
    {
        await using var jobs = await JobsHarness.CreateAsync();
        var record = jobs.Installed("Text and voice");
        jobs.PublishReleaseTwo(File(FileTwo));

        var result = await jobs.RunAsync(new UpdateJobRequest(record));

        result.Installed!.Source.Should().Match<InstallSource>(source =>
            source.ReleaseFileId == FileTwo.ToString("D") && source.VariantName == null && source.HadVariantChoice == false);
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

    [Theory]
    [InlineData(JobKind.Update)]
    [InlineData(JobKind.Install)]
    public async Task A_new_release_made_for_another_game_version_removes_nothing(JobKind kind)
    {
        await using var jobs = await JobsHarness.CreateAsync(policy: kind == JobKind.Install ? new OneProjectPolicy() : null);
        var record = jobs.Installed();
        var request = Replacement(jobs, record, kind);
        jobs.Engine.Prediction = new PreflightReport([new FileMismatchIssue("data/strings.pak", AlreadyPatched: false)], []);

        var result = await jobs.RunAsync(request);

        result.Failure.Should().BeEquivalentTo(new JobFailure(JobFailureReason.NewReleaseIncompatible) { Files = ["data/strings.pak"] });
        result.RemovedPrevious.Should().BeFalse();
        result.RestoredPrevious.Should().BeFalse();
        jobs.Engine.Calls.Should().NotContain("uninstall");
        jobs.Engine.Records.Should().ContainKey(record.Id);
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
    [InlineData(true, JobFailureReason.KeptFilesBlockReplacement)]
    [InlineData(false, JobFailureReason.KeptFilesPending)]
    public async Task An_update_that_writes_a_kept_file_says_whether_only_uninstalling_first_gets_past_it(bool keptByItsUninstall,
        JobFailureReason reason)
    {
        await using var jobs = await JobsHarness.CreateAsync();
        var record = jobs.Installed();
        jobs.PublishReleaseTwo();
        jobs.Engine.Prediction = new PreflightReport([new KeptFileIssue("tool/list.dat", keptByItsUninstall ? record.Id : Guid.NewGuid())], []);

        var result = await jobs.RunAsync(new UpdateJobRequest(record));

        result.Failure.Should().BeEquivalentTo(new JobFailure(reason) { Files = ["tool/list.dat"] });
        jobs.Engine.Calls.Should().NotContain("uninstall");
    }

    [Theory]
    [InlineData(JobKind.Update)]
    [InlineData(JobKind.Repair)]
    [InlineData(JobKind.Install)]
    public async Task A_failure_after_the_uninstall_puts_the_previous_release_back(JobKind kind)
    {
        await using var jobs = await JobsHarness.CreateAsync(policy: kind == JobKind.Install ? new OneProjectPolicy() : null);
        var record = jobs.Installed();
        var request = Replacement(jobs, record, kind);
        jobs.Engine.NextInstalls.Enqueue((_, _, _) => throw new InstallFailedException(rolledBack: true, new IOException("Locked meanwhile.")));

        var result = await jobs.RunAsync(request);

        result.Outcome.Should().Be(JobOutcome.Failed);
        result.Failure!.Reason.Should().Be(JobFailureReason.DiskError);
        result.RemovedPrevious.Should().BeTrue();
        result.RestoredPrevious.Should().BeTrue();
        result.RemovedRecord.Should().Be(record.Id);
        result.Installed!.Source!.ReleaseFileId.Should().Be(FileOne.ToString("D"));
        jobs.Engine.Installs.Last().Parts.Single().PackagePath.Should().Be(jobs.Downloader.PathOf(FileOne));
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

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_failed_update_tells_the_files_its_uninstall_left_only_when_the_previous_release_did_not_come_back(bool restores)
    {
        await using var jobs = await JobsHarness.CreateAsync();
        var record = jobs.Installed();
        jobs.PublishReleaseTwo();
        jobs.Engine.OnUninstall = (id, token) =>
        {
            jobs.Engine.Records.TryRemove(id, out _);
            return Task.FromResult(new UninstallResult([new KeptFile("tool/list.dat", KeptReason.GameUpdatedSinceInstall)]));
        };
        jobs.Engine.NextInstalls.Enqueue((_, _, _) => throw new InstallFailedException(rolledBack: true, new InvalidOperationException("Odd.")));
        if (!restores)
        {
            jobs.Engine.NextInstalls.Enqueue((_, _, _) => throw new InstallBlockedException([new FileLockedIssue("data/strings.pak", ["Viewer"])]));
        }

        var result = await jobs.RunAsync(new UpdateJobRequest(record));

        result.RestoredPrevious.Should().Be(restores);
        result.KeptFiles.Should().Equal(restores ? [] : [new KeptFile("tool/list.dat", KeptReason.GameUpdatedSinceInstall)],
            "the release put back takes over what its uninstall left");
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

    // An update, a repair, or another project taking the installed one's place in an app that keeps one project per game.
    private static JobRequest Replacement(JobsHarness jobs, InstallRecord record, JobKind kind)
    {
        if (kind == JobKind.Install)
        {
            jobs.Catalogue.Add(Page(OtherProject, Release(ReleaseTwo, 1, File(FileTwo)), teamSlug: "paper-team"));
            return new InstallJobRequest(Game, "Example Game", Locator(OtherProject, "paper-team"));
        }

        jobs.PublishReleaseTwo();
        return kind == JobKind.Update ? new UpdateJobRequest(record) : new RepairJobRequest(record);
    }
}
