using JustModIt.Manager.Core.Catalog;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.Installation;
using JustModIt.Manager.Core.Jobs;
using JustModIt.Manager.Core.Settings;
using JustModIt.Manager.Core.Tests.TestSupport;
using static JustModIt.Manager.Core.Tests.TestSupport.TestProjects;

namespace JustModIt.Manager.Core.Tests.Jobs;

public sealed class JobRunnerTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Installs_the_recommended_package_of_the_current_release_and_keeps_where_it_came_from()
    {
        await using var jobs = await JobsHarness.CreateAsync();

        var result = await jobs.RunAsync(JobsHarness.Install());

        result.Outcome.Should().Be(JobOutcome.Succeeded);
        jobs.Downloader.Downloads.Should().Equal(FileOne);
        jobs.Catalogue.PageRequests.Should().ContainSingle();
        var source = result.Installed!.Source!;
        source.ProjectId.Should().Be(Project.ToString("D"));
        source.ReleaseId.Should().Be(ReleaseOne.ToString("D"));
        source.ReleaseFileId.Should().Be(FileOne.ToString("D"));
        source.ReleaseVersion.Should().Be(1);
        source.ReleaseLabel.Should().Be("v1");
        source.VariantName.Should().Be("Text only");
        source.TeamName.Should().Be("Lantern Team");
        source.CoverUrl.Should().Be(new Uri("https://images.example.com/covers/example-game.jpg"));
        jobs.Engine.Installs.Single().Backup.Location.Should().Be(BackupLocation.GameFolder);
        result.ReleaseId.Should().Be(ReleaseOne);
        result.PackageFileIds.Should().Equal(FileOne);
        jobs.Stages(result.Job.Id).Should().Equal(JobStage.Queued, JobStage.Preparing, JobStage.Downloading, JobStage.Checking,
            JobStage.Installing);
        jobs.Results.Should().ContainSingle().Which.Should().BeSameAs(result);
        jobs.Runner.Find(Game).Should().BeNull();
    }

    [Fact]
    public async Task Installs_the_chosen_variant_with_the_backup_location_the_settings_name()
    {
        await using var jobs = await JobsHarness.CreateAsync();
        jobs.Settings.Update(settings => settings with { BackupLocation = BackupLocation.AppData });
        jobs.Choose(("Text and voice", true));

        var result = await jobs.RunAsync(JobsHarness.Install());

        result.Installed!.Source!.VariantName.Should().Be("Text and voice");
        jobs.Downloader.Downloads.Should().Equal(VoiceFile);
        jobs.Engine.Installs.Single().Backup.Location.Should().Be(BackupLocation.AppData);
    }

    [Fact]
    public async Task Start_returns_at_once_and_the_job_announces_itself_from_the_background()
    {
        await using var jobs = await JobsHarness.CreateAsync();
        jobs.Catalogue.PageGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        // Marks the test's own thread only, so an event raised on the thread pool meanwhile is not taken for one raised by Start.
        using var starting = new ThreadLocal<bool>();
        var announcedWhileStarting = false;
        jobs.Runner.Changed += (_, _) => announcedWhileStarting |= starting.Value;

        starting.Value = true;
        var handle = jobs.Runner.Start(JobsHarness.Install());
        starting.Value = false;

        handle.Completion.IsCompleted.Should().BeFalse();
        handle.Job.Stage.Should().Be(JobStage.Queued);
        jobs.Runner.Find(Game)!.Id.Should().Be(handle.Job.Id);
        jobs.Runner.IsBusy.Should().BeTrue();
        await Eventually.HoldsAsync(() => jobs.LastChange(handle.Job.Id)?.Stage == JobStage.Preparing, "the job prepares");
        announcedWhileStarting.Should().BeFalse("the first snapshot must not be raised on the caller's thread");
        jobs.Catalogue.PageGate.SetResult();
        (await handle.Completion).Outcome.Should().Be(JobOutcome.Succeeded);
        await jobs.Runner.WhenIdleAsync(Token);
    }

    [Fact]
    public async Task A_second_job_for_the_same_game_is_refused_without_any_event()
    {
        await using var jobs = await JobsHarness.CreateAsync();
        jobs.Catalogue.PageGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = jobs.Runner.Start(JobsHarness.Install());

        var second = await jobs.RunAsync(JobsHarness.Install());

        second.Outcome.Should().Be(JobOutcome.Failed);
        second.Failure!.Reason.Should().Be(JobFailureReason.AlreadyRunning);
        jobs.Changes.Should().OnlyContain(change => change.Id == first.Job.Id);
        jobs.Results.Should().BeEmpty();
        jobs.Catalogue.PageGate.SetResult();
        (await first.Completion).Outcome.Should().Be(JobOutcome.Succeeded);
    }

    [Fact]
    public async Task Tells_a_refused_job_from_one_that_ran_however_soon_that_one_ended()
    {
        await using var jobs = await JobsHarness.CreateAsync();
        jobs.Catalogue.PageGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = jobs.Runner.Start(JobsHarness.Install());
        var busy = jobs.Runner.Start(JobsHarness.Install());
        jobs.Catalogue.PageGate.SetResult();
        await first.Completion;
        jobs.Runner.Shutdown(cancelPlayerJobs: false);
        var exiting = jobs.Runner.Start(JobsHarness.Install());

        (first.IsRefused, busy.IsRefused, exiting.IsRefused).Should().Be((false, true, true),
            "a job that ended before its starter looked ran all the same");
    }

    [Fact]
    public async Task Waits_for_recovery_before_touching_anything()
    {
        await using var jobs = await JobsHarness.CreateAsync(recover: false);
        jobs.Engine.Recovery = new TaskCompletionSource<IReadOnlyList<RecoveredOperation>>();
        var recovery = jobs.Runner.RecoverAsync(Token);

        var handle = jobs.Runner.Start(JobsHarness.Install());
        await jobs.WaitsAsync(handle.Job.Id, JobWaitReason.Recovery);

        jobs.Catalogue.PageRequests.Should().BeEmpty();
        jobs.Library.ProbeCount.Should().Be(0);
        jobs.Engine.Recovery.SetResult([new RecoveredOperation(Guid.NewGuid(), RecoveryOutcome.RolledBack, "folder")]);
        (await handle.Completion).Outcome.Should().Be(JobOutcome.Succeeded);
        (await recovery).Should().ContainSingle();
        (await jobs.Runner.Recovery).Should().ContainSingle();
    }

    [Fact]
    public async Task A_failed_recovery_still_lets_the_jobs_run()
    {
        await using var jobs = await JobsHarness.CreateAsync(recover: false);
        jobs.Engine.Recovery = new TaskCompletionSource<IReadOnlyList<RecoveredOperation>>();
        jobs.Engine.Recovery.SetException(new IOException("The disk is gone."));

        (await jobs.Runner.RecoverAsync(Token)).Should().BeEmpty();

        (await jobs.RunAsync(JobsHarness.Install())).Outcome.Should().Be(JobOutcome.Succeeded);
    }

    [Fact]
    public async Task Waits_for_the_game_to_exit_without_polling_then_installs()
    {
        await using var jobs = await JobsHarness.CreateAsync();
        jobs.Library.Runs(Game, "ExampleGame", "examplegame", "Launcher");

        var handle = jobs.Runner.Start(JobsHarness.Install());
        await jobs.WaitsAsync(handle.Job.Id, JobWaitReason.GameRunning);

        jobs.LastChange(handle.Job.Id)!.RunningProcesses.Should().Equal("ExampleGame", "Launcher");
        jobs.Time.Advance(TimeSpan.FromHours(1));
        await Eventually.SettleAsync();
        jobs.Library.ProbeCount.Should().Be(1, "the job waits for the exit instead of looking again and again");
        jobs.Downloader.Downloads.Should().BeEmpty();
        jobs.Library.Probes.Clear();
        jobs.RunningGames.ExitAll();

        (await handle.Completion).Outcome.Should().Be(JobOutcome.Succeeded);
        jobs.Stages(handle.Job.Id).Should().StartWith([JobStage.Queued, JobStage.Waiting, JobStage.Queued, JobStage.Preparing]);
    }

    [Fact]
    public async Task Waits_while_the_store_works_on_the_game_and_looks_again_when_the_library_changes()
    {
        await using var jobs = await JobsHarness.CreateAsync();
        jobs.Library.Describe(Installed(state: GameInstallState.Updating));

        var handle = jobs.Runner.Start(JobsHarness.Install());
        await jobs.WaitsAsync(handle.Job.Id, JobWaitReason.StoreBusy);
        jobs.Library.Probes.Clear();
        jobs.Library.RaiseChanged();

        (await handle.Completion).Outcome.Should().Be(JobOutcome.Succeeded);
        jobs.Library.ProbeCount.Should().BeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task Looks_again_at_a_busy_store_after_a_while_when_no_change_is_reported()
    {
        await using var jobs = await JobsHarness.CreateAsync();
        jobs.Library.Describe(Installed(state: GameInstallState.Validating));
        var timers = jobs.Time.TimersCreated;

        var handle = jobs.Runner.Start(JobsHarness.Install());
        await jobs.WaitsAsync(handle.Job.Id, JobWaitReason.StoreBusy);
        await Eventually.HoldsAsync(() => jobs.Time.TimersCreated > timers, "the job sleeps until it looks again");
        jobs.Library.Probes.Clear();
        var probes = jobs.Library.ProbeCount;

        // The clock moves to the minute's end at once: stepped while the job waits for a thread, it ran past the minute on
        // a busy machine.
        jobs.Time.Advance(JobPipeline.StoreBusyRecheck - TimeSpan.FromSeconds(1));
        await Eventually.SettleAsync();
        jobs.Library.ProbeCount.Should().Be(probes, "nothing changed, so the job sleeps on");
        jobs.Time.Advance(TimeSpan.FromSeconds(1));

        (await handle.Completion.WaitAsync(TimeSpan.FromSeconds(30), Token)).Outcome.Should().Be(JobOutcome.Succeeded,
            "the store is looked at again at least once a minute");
    }

    [Fact]
    public async Task Waits_until_the_store_last_touched_the_game_long_enough_ago()
    {
        await using var jobs = await JobsHarness.CreateAsync();
        jobs.Library.Describe(Installed() with { StoreActivityAt = Now.AddSeconds(-10) });
        var timers = jobs.Time.TimersCreated;

        var handle = jobs.Runner.Start(JobsHarness.Install());
        await jobs.WaitsAsync(handle.Job.Id, JobWaitReason.QuietPeriod);
        await Eventually.HoldsAsync(() => jobs.Time.TimersCreated > timers, "the job sleeps until the quiet period ends");
        jobs.Time.Advance(TimeSpan.FromSeconds(19));
        await Eventually.SettleAsync();
        handle.Completion.IsCompleted.Should().BeFalse();

        jobs.Time.Advance(TimeSpan.FromSeconds(1));

        await Eventually.HoldsAsync(() => handle.Completion.IsCompleted, "the store's last touch is 30 seconds ago");
        (await handle.Completion).Outcome.Should().Be(JobOutcome.Succeeded);
    }

    [Fact]
    public async Task A_game_another_app_manages_fails_before_anything_is_downloaded()
    {
        await using var jobs = await JobsHarness.CreateAsync();
        jobs.Engine.OtherApps = [new GameManagedByOtherAppIssue("OtherApp", @"D:\Games\Example\.other-app")];

        var install = await jobs.RunAsync(JobsHarness.Install());
        var update = await jobs.RunAsync(new UpdateJobRequest(jobs.Installed()));

        install.Failure.Should().BeEquivalentTo(new JobFailure(JobFailureReason.ManagedByOtherApp)
        {
            OtherApp = "OtherApp",
            Location = @"D:\Games\Example\.other-app",
        });
        update.Failure!.Reason.Should().Be(JobFailureReason.ManagedByOtherApp);
        jobs.Downloader.Downloads.Should().BeEmpty();
        jobs.Engine.Calls.Should().Equal("recover");
    }

    [Fact]
    public async Task An_update_whose_originals_were_deleted_fails_before_anything_is_downloaded()
    {
        await using var jobs = await JobsHarness.CreateAsync();
        var installed = jobs.Installed();
        jobs.PublishReleaseTwo();
        jobs.Engine.MissingOriginals[installed.Id] = [new OriginalsMissingIssue(["data/strings.pak"])];

        var update = await jobs.RunAsync(new UpdateJobRequest(installed));

        update.Failure.Should().BeEquivalentTo(new JobFailure(JobFailureReason.OriginalsMissing) { Files = ["data/strings.pak"] });
        jobs.Downloader.Downloads.Should().BeEmpty();
        jobs.Engine.Calls.Should().Equal("recover");
    }

    [Fact]
    public async Task A_pending_store_update_fails_as_not_ready_before_anything_is_downloaded()
    {
        await using var jobs = await JobsHarness.CreateAsync();
        jobs.Library.Describe(Installed(state: GameInstallState.UpdatePending) with { Reason = GameStateReason.UpdateRequired });

        var result = await jobs.RunAsync(JobsHarness.Install());

        result.Failure.Should().BeEquivalentTo(new JobFailure(JobFailureReason.GameNotReady)
        {
            GameState = GameInstallState.UpdatePending,
            StateReason = GameStateReason.UpdateRequired,
        });
        jobs.Downloader.Downloads.Should().BeEmpty();
        jobs.Catalogue.PageRequests.Should().BeEmpty();
    }

    [Fact]
    public async Task Uninstalls_a_game_waiting_for_a_store_update()
    {
        await using var jobs = await JobsHarness.CreateAsync();
        var record = jobs.Installed();
        jobs.Library.Describe(Installed(state: GameInstallState.UpdatePending));

        var result = await jobs.RunAsync(new UninstallJobRequest(record));

        result.Outcome.Should().Be(JobOutcome.Succeeded);
        result.RemovedRecord.Should().Be(record.Id);
        jobs.Engine.Calls.Should().EndWith("uninstall");
    }

    [Fact]
    public async Task An_automatic_job_that_has_its_package_waits_for_the_game_instead_of_deferring()
    {
        await using var jobs = await JobsHarness.CreateAsync();
        var download = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        jobs.Downloader.Gates[FileOne] = download;

        var handle = jobs.Runner.Start(JobsHarness.Install(origin: JobOrigin.Automatic));
        await Eventually.HoldsAsync(() => jobs.Downloader.Downloads.Contains(FileOne), "the package is being downloaded");
        jobs.Library.Runs(Game, "ExampleGame");
        download.SetResult();
        await jobs.WaitsAsync(handle.Job.Id, JobWaitReason.GameRunning);
        await Eventually.HoldsAsync(() => jobs.RunningGames.Waits == 1, "the job waits for the game to exit");
        jobs.Library.Probes.Clear();
        jobs.RunningGames.ExitAll();

        (await handle.Completion.WaitAsync(TimeSpan.FromSeconds(30), Token)).Outcome.Should().Be(JobOutcome.Succeeded);
        jobs.Downloader.Downloads.Should().Equal([FileOne], "a downloaded package is not downloaded, and counted, again");
    }
    [Fact]
    public async Task An_automatic_job_is_deferred_while_the_game_runs()
    {
        await using var jobs = await JobsHarness.CreateAsync();
        jobs.Library.Runs(Game, "ExampleGame");

        var result = await jobs.RunAsync(JobsHarness.Install(origin: JobOrigin.Automatic));

        result.Outcome.Should().Be(JobOutcome.Deferred);
        result.DeferredBy.Should().Be(JobWaitReason.GameRunning);
        result.RunningProcesses.Should().ContainSingle().Which.Name.Should().Be("ExampleGame");
        jobs.Catalogue.PageRequests.Should().BeEmpty();
        jobs.RunningGames.Waits.Should().Be(0);
    }

    [Fact]
    public async Task One_download_runs_at_a_time()
    {
        await using var jobs = await JobsHarness.CreateAsync();
        jobs.Library.Publish(Installed(), Installed(OtherGame));
        jobs.Catalogue.Add(Page(OtherProject, Release(ReleaseTwo, 1, File(FileTwo)), OtherGame));
        jobs.Downloader.Gates[FileOne] = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = jobs.Runner.Start(JobsHarness.Install());
        await Eventually.HoldsAsync(() => jobs.Downloader.Downloads.Contains(FileOne), "the first download starts");

        var second = jobs.Runner.Start(new InstallJobRequest(OtherGame, "Other Game", Locator(OtherProject)));
        await jobs.WaitsAsync(second.Job.Id, JobWaitReason.Download);

        jobs.Downloader.Downloads.Should().Equal(FileOne);
        jobs.Downloader.Gates[FileOne].SetResult();
        (await first.Completion).Outcome.Should().Be(JobOutcome.Succeeded);
        (await second.Completion).Outcome.Should().Be(JobOutcome.Succeeded);
        jobs.Downloader.Downloads.Should().Equal(FileOne, FileTwo);
    }

    [Fact]
    public async Task A_kept_package_is_used_without_waiting_for_another_download()
    {
        await using var jobs = await JobsHarness.CreateAsync();
        jobs.Library.Publish(Installed(), Installed(OtherGame));
        jobs.Catalogue.Add(Page(OtherProject, Release(ReleaseTwo, 1, File(FileTwo)), OtherGame));
        jobs.Downloader.Cached[FileTwo] = true;
        jobs.Downloader.Gates[FileOne] = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = jobs.Runner.Start(JobsHarness.Install());
        await Eventually.HoldsAsync(() => jobs.Downloader.Downloads.Contains(FileOne), "the first download starts");

        var second = await jobs.RunAsync(new InstallJobRequest(OtherGame, "Other Game", Locator(OtherProject)));

        second.Outcome.Should().Be(JobOutcome.Succeeded);
        jobs.Downloader.Gates[FileOne].SetResult();
        await first.Completion;
    }

    [Fact]
    public async Task Cancelling_a_download_leaves_the_game_untouched()
    {
        await using var jobs = await JobsHarness.CreateAsync();
        jobs.Downloader.Gates[FileOne] = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handle = jobs.Runner.Start(JobsHarness.Install());
        await Eventually.HoldsAsync(() => jobs.Downloader.Downloads.Contains(FileOne), "the download starts");

        jobs.Runner.Cancel(handle.Job.Id).Should().BeTrue();
        var result = await handle.Completion;

        result.Outcome.Should().Be(JobOutcome.Cancelled);
        result.CancelledByPlayer.Should().BeTrue();
        jobs.Engine.Installs.Should().BeEmpty();
        jobs.Runner.Cancel(handle.Job.Id).Should().BeFalse();
    }

    [Fact]
    public async Task Shutting_down_cancels_the_automatic_jobs_and_refuses_new_ones_but_lets_the_players_finish()
    {
        await using var jobs = await JobsHarness.CreateAsync();
        jobs.Library.Publish(Installed(), Installed(OtherGame));
        jobs.Catalogue.Add(Page(OtherProject, Release(ReleaseTwo, 1, File(FileTwo)), OtherGame));
        jobs.Catalogue.PageGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var automatic = jobs.Runner.Start(JobsHarness.Install(origin: JobOrigin.Automatic));
        var players = jobs.Runner.Start(new InstallJobRequest(OtherGame, "Other Game", Locator(OtherProject)));
        await Eventually.HoldsAsync(() => jobs.Catalogue.PageRequests.Count == 2, "both jobs prepare");

        jobs.Runner.Shutdown(cancelPlayerJobs: false);

        var cancelled = await automatic.Completion;
        cancelled.Outcome.Should().Be(JobOutcome.Cancelled);
        cancelled.CancelledByPlayer.Should().BeFalse();
        (await jobs.Runner.Start(JobsHarness.Install()).Completion).Outcome.Should().Be(JobOutcome.Cancelled);
        jobs.Catalogue.PageGate.SetResult();
        (await players.Completion).Outcome.Should().Be(JobOutcome.Succeeded);
    }

    [Fact]
    public async Task A_release_with_no_file_for_the_games_store_fails_before_any_download()
    {
        await using var jobs = await JobsHarness.CreateAsync();
        jobs.Catalogue.Add(Page(release: Release(ReleaseOne, 1, File(FileOne, onlyFor: OtherGame))));

        var result = await jobs.RunAsync(JobsHarness.Install());

        result.Failure.Should().BeEquivalentTo(new JobFailure(JobFailureReason.NoPackage)
        {
            Unavailability = SelectionUnavailability.OtherStores,
        });
        jobs.Downloader.Downloads.Should().BeEmpty();
    }

    [Fact]
    public async Task A_release_gone_from_the_fresh_page_fails_as_unavailable_before_any_download()
    {
        await using var jobs = await JobsHarness.CreateAsync();
        jobs.Catalogue.Add(Page(release: Release(ReleaseOne, 1, File(FileOne)) with { IsHidden = true }));

        var result = await jobs.RunAsync(JobsHarness.Install());

        result.Failure!.Reason.Should().Be(JobFailureReason.ReleaseUnavailable);
        jobs.Downloader.Downloads.Should().BeEmpty();
    }

    [Fact]
    public async Task A_page_that_cannot_be_read_fails_as_offline_with_the_catalogue_error()
    {
        await using var jobs = await JobsHarness.CreateAsync();
        jobs.Catalogue.PageFailure = new CatalogueException(CatalogueErrorReason.Timeout, "No answer.");

        var result = await jobs.RunAsync(JobsHarness.Install());

        result.Failure.Should().BeEquivalentTo(new JobFailure(JobFailureReason.Offline) { CatalogueError = CatalogueErrorReason.Timeout });
    }

    [Fact]
    public async Task A_project_that_is_not_completed_is_not_installed_anew_but_its_install_keeps_its_updates()
    {
        await using var jobs = await JobsHarness.CreateAsync();
        jobs.Catalogue.Add(jobs.Catalogue.Pages[Project] with { Status = InProgress });

        var install = await jobs.RunAsync(JobsHarness.Install());

        install.Failure!.Reason.Should().Be(JobFailureReason.ProjectNotCompleted);
        jobs.Downloader.Downloads.Should().BeEmpty();

        var record = jobs.Installed();
        jobs.PublishReleaseTwo();
        jobs.Catalogue.Add(jobs.Catalogue.Pages[Project] with { Status = InProgress });
        (await jobs.RunAsync(new UpdateJobRequest(record))).Outcome.Should().Be(JobOutcome.Succeeded);
    }

    [Fact]
    public async Task A_project_the_site_no_longer_has_fails_as_not_found()
    {
        await using var jobs = await JobsHarness.CreateAsync();
        jobs.Catalogue.Pages.Clear();

        (await jobs.RunAsync(JobsHarness.Install())).Failure!.Reason.Should().Be(JobFailureReason.ProjectNotFound);
    }

    [Fact]
    public async Task Waits_again_when_the_game_starts_between_the_look_and_the_install()
    {
        await using var jobs = await JobsHarness.CreateAsync();
        jobs.Engine.NextInstalls.Enqueue((_, _, _) => throw new InstallBlockedException([new GameRunningIssue("ExampleGame")]));

        var handle = jobs.Runner.Start(JobsHarness.Install());

        await jobs.Time.RunUntilCompletedAsync(handle.Completion, TimeSpan.FromSeconds(1));
        (await handle.Completion).Outcome.Should().Be(JobOutcome.Succeeded);
        jobs.Engine.Installs.Should().HaveCount(2);
    }

    [Fact]
    public async Task Progress_is_throttled_and_every_snapshot_carries_a_growing_sequence()
    {
        await using var jobs = await JobsHarness.CreateAsync();
        jobs.Engine.NextInstalls.Enqueue((request, progress, _) =>
        {
            for (var done = 0; done <= 1000; done++)
            {
                progress!.Report(new InstallProgress(InstallStage.Installing, "data/strings.pak", done / 100, 10, done, 1000));
                if (done == 500)
                {
                    jobs.Time.Advance(JobContext.ProgressInterval);
                }
            }

            return jobs.Engine.InstallNowAsync(request);
        });

        var result = await jobs.RunAsync(JobsHarness.Install());

        var installing = jobs.Progress.Where(snapshot => snapshot.Id == result.Job.Id && snapshot.Stage == JobStage.Installing).ToList();
        installing.Should().HaveCountLessThanOrEqualTo(3, "reports within one interval are dropped");
        installing[^1].Fraction.Should().Be(1);
        jobs.Changes.Concat(jobs.Progress).Where(snapshot => snapshot.Id == result.Job.Id).Select(snapshot => snapshot.Sequence)
            .Should().OnlyHaveUniqueItems();
        result.Job.Sequence.Should().Be(jobs.Changes.Concat(jobs.Progress).Max(snapshot => snapshot.Sequence));
    }

    [Fact]
    public async Task Prunes_downloads_only_while_no_job_runs_and_keeps_what_it_is_asked_to()
    {
        await using var jobs = await JobsHarness.CreateAsync();
        jobs.Downloader.Cached[FileOne] = true;
        jobs.Downloader.Cached[FileTwo] = true;
        jobs.Catalogue.PageGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handle = jobs.Runner.Start(JobsHarness.Install());

        (await jobs.Runner.PruneDownloadsAsync(new HashSet<Guid>(), Token)).Should().Be(0);
        jobs.Downloader.Prunes.Should().BeEmpty();
        jobs.Catalogue.PageGate.SetResult();
        await handle.Completion;

        (await jobs.Runner.PruneDownloadsAsync(new HashSet<Guid> { FileOne }, Token)).Should().Be(1);
        jobs.Downloader.Cached.Keys.Should().Equal(FileOne);
    }

    [Fact]
    public async Task Clears_downloads_for_the_player_only_while_no_job_runs_and_keeps_the_package_of_a_job_just_finished()
    {
        await using var jobs = await JobsHarness.CreateAsync();
        jobs.Downloader.Cached[FileTwo] = true;
        jobs.Catalogue.PageGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handle = jobs.Runner.Start(JobsHarness.Install());

        (await jobs.Runner.ClearDownloadsAsync(new HashSet<Guid>(), Token)).Should().BeFalse("a job may be about to use a package");
        jobs.Downloader.Prunes.Should().BeEmpty();
        jobs.Catalogue.PageGate.SetResult();
        (await handle.Completion).IsSuccess.Should().BeTrue();

        (await jobs.Runner.ClearDownloadsAsync(new HashSet<Guid>(), Token)).Should().BeTrue();
        jobs.Downloader.Cached.Keys.Should().Equal([FileOne], "the install that just finished needs its package, which the list may not know of yet");
        jobs.Downloader.Prunes.Single().KeepPartial.Should().BeFalse("clearing the cache drops partial downloads too");
    }

    [Fact]
    public async Task Keeps_the_package_of_a_job_that_finished_since_the_last_prune_whatever_it_is_asked()
    {
        await using var jobs = await JobsHarness.CreateAsync();
        jobs.Downloader.Failure = new CatalogueException(CatalogueErrorReason.Offline, "Cut off.");
        await jobs.RunAsync(JobsHarness.Install());
        jobs.Downloader.Failure = null;
        jobs.Downloader.Cached[FileOne] = true;

        await jobs.Runner.PruneDownloadsAsync(new HashSet<Guid>(), Token);
        jobs.Downloader.Cached.Keys.Should().Equal(FileOne);

        await jobs.Runner.PruneDownloadsAsync(new HashSet<Guid>(), Token);
        jobs.Downloader.Cached.Should().BeEmpty();
    }

    [Fact]
    public async Task A_test_install_of_a_package_file_is_flagged()
    {
        await using var jobs = await JobsHarness.CreateAsync();

        var result = await jobs.RunAsync(new TestInstallJobRequest(Game, "Example Game", [Path.Join("creator", "build.examplemod")]));

        result.Installed!.IsTest.Should().BeTrue();
        jobs.Engine.Installs.Single().IsTest.Should().BeTrue();
        jobs.Catalogue.PageRequests.Should().BeEmpty();
        jobs.Downloader.Downloads.Should().BeEmpty();
    }

    [Fact]
    public async Task Packages_tried_together_install_in_order_as_one_install_that_needs_each_of_them()
    {
        await using var jobs = await JobsHarness.CreateAsync();
        string[] packages = [Path.Join("creator", "base.zip"), Path.Join("creator", "add-on.examplemod")];

        var result = await jobs.RunAsync(new TestInstallJobRequest(Game, "Example Game", packages));

        result.Outcome.Should().Be(JobOutcome.Succeeded);
        jobs.Engine.Installs.Single().Parts.Should().Equal(new InstallPart(packages[0]), new InstallPart(packages[1]));
    }

    [Fact]
    public async Task Forgets_an_install_whose_game_folder_is_gone_for_good()
    {
        await using var jobs = await JobsHarness.CreateAsync();
        var record = jobs.Installed();
        jobs.Library.Publish();
        jobs.Engine.OnUninstall = (_, _) => throw new InstallBlockedException([new GameFolderMissingIssue(record.InstallDirectory)]);
        jobs.Engine.Forgetting = ForgetOutcome.Forgotten;

        var result = await jobs.RunAsync(new UninstallJobRequest(record));

        result.Outcome.Should().Be(JobOutcome.Succeeded);
        result.Forgotten.Should().BeTrue();
        result.RemovedRecord.Should().Be(record.Id);
    }

    [Fact]
    public async Task Keeps_an_install_whose_game_folder_is_on_an_unplugged_drive()
    {
        await using var jobs = await JobsHarness.CreateAsync();
        var record = jobs.Installed();
        jobs.Library.Publish();
        jobs.Engine.OnUninstall = (_, _) => throw new InstallBlockedException([new GameFolderMissingIssue(record.InstallDirectory)]);

        var result = await jobs.RunAsync(new UninstallJobRequest(record));

        result.Failure!.Reason.Should().Be(JobFailureReason.GameFolderMissing);
        jobs.Engine.Records.Should().ContainKey(record.Id);
    }

    [Fact]
    public async Task A_listener_that_throws_does_not_break_the_job()
    {
        await using var jobs = await JobsHarness.CreateAsync();
        jobs.Runner.Changed += (_, _) => throw new InvalidOperationException("A broken listener.");
        jobs.Runner.Finished += (_, _) => throw new InvalidOperationException("A broken listener.");

        (await jobs.RunAsync(JobsHarness.Install())).Outcome.Should().Be(JobOutcome.Succeeded);
    }
}
