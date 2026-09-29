using System.Collections.Immutable;
using JustModIt.Manager.Core.Catalog;
using JustModIt.Manager.Core.Collections;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.Installation;
using JustModIt.Manager.Core.Jobs;
using JustModIt.Manager.Core.Library;
using JustModIt.Manager.Core.Settings;
using JustModIt.Manager.Core.State;
using JustModIt.Manager.Core.Tests.Jobs;
using JustModIt.Manager.Core.Tests.TestSupport;
using static JustModIt.Manager.Core.Tests.TestSupport.TestProjects;

namespace JustModIt.Manager.Core.Tests.State;

public sealed class StateCoordinatorTests
{
    private static readonly GameKey Unlisted = new(StoreId.Steam, "4000");

    private static readonly VerificationResult Modified = new([new FileVerification("data/strings.pak", FileChange.Replaced, FileIntegrity.Modified)]);

    [Fact]
    public async Task Publishes_the_first_state_from_local_data_before_any_network_or_hashing()
    {
        await using var app = new CoordinatorHarness();
        app.Library.Publish([Installed()], cached: true);
        app.Catalogue.Cached = new CatalogueSnapshot
        {
            State = CatalogueState.Cached,
            Index = new InstallableIndex(Now, [Entry(release: Release(ReleaseOne, 1, File(FileOne)))]),
            FetchedAt = Now,
        };
        app.Engine.Add(Record());
        app.Engine.VerifyGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var state = await app.StartAsync();

        var game = state.Games.Should().ContainSingle().Which;
        game.Status.State.Should().Be(MyGameState.Installed);
        state.IsLibraryCached.Should().BeTrue();
        state.CatalogueState.Should().Be(CatalogueState.Cached);
        app.Catalogue.PageRequests.Should().BeEmpty();
        app.Engine.Calls.Should().StartWith(["recover", "load records"]);
        app.Engine.VerifyGate.SetResult();
    }

    [Fact]
    public async Task A_job_shows_at_once_and_its_outcome_arrives_together_with_the_install()
    {
        await using var app = new CoordinatorHarness().WithGameAndProject();
        await app.StartAsync();
        app.Catalogue.PageGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        app.Jobs.Start(JobsHarness.Install());
        var busy = await app.GameAsync(game => game.Status.State == MyGameState.Busy, "the game is busy");

        busy.Jobs.Should().ContainSingle();
        app.Catalogue.PageGate.SetResult();
        var done = await app.GameAsync(game => game.Job is null, "the job finished");
        var installed = done.Find(Game)!;
        installed.Status.State.Should().Be(MyGameState.Installed);
        installed.Installed!.Source!.ReleaseId.Should().Be(ReleaseOne.ToString("D"));
        done.Jobs.Should().BeEmpty();
    }

    [Fact]
    public async Task Verifies_an_install_on_first_sight_and_shows_that_it_needs_repair()
    {
        await using var app = new CoordinatorHarness().WithGameAndProject();
        app.Engine.Add(Record());
        app.Engine.OnVerify = _ => Modified;

        await app.StartAsync();

        await app.GameAsync(game => game.Status.Repair == RepairReason.FilesModified, "the install needs repair");
        app.Engine.Verifications.Should().Be(1);
    }

    [Fact]
    public async Task Verifies_again_after_a_new_build_once_the_store_is_done_daily_and_when_asked()
    {
        await using var app = new CoordinatorHarness().WithGameAndProject();
        app.Engine.Add(Record());
        await app.StartAsync();
        await Eventually.HoldsAsync(() => app.Engine.Verifications == 1, "the first check");

        app.Library.Publish(Installed());
        await Eventually.SettleAsync();
        app.Engine.Verifications.Should().Be(1, "nothing changed");

        app.Library.Publish(Installed(build: "101"));
        await Eventually.HoldsAsync(() => app.Engine.Verifications == 2, "a new build is checked");

        app.Library.Publish(Installed(state: GameInstallState.Validating, build: "101"));
        await Eventually.SettleAsync();
        app.Engine.Verifications.Should().Be(2, "nothing is checked while the store works on the game");
        app.Library.Publish(Installed(build: "101"));
        await Eventually.HoldsAsync(() => app.Engine.Verifications == 3, "the game is checked once the store is done");

        await Eventually.SettleAsync();
        app.Time.Advance(VerificationSchedule.Interval - TimeSpan.FromHours(1));
        await Eventually.SettleAsync();
        app.Engine.Verifications.Should().Be(3, "a day has not passed yet");
        await Eventually.HoldsAsync(() => app.Engine.Verifications >= 4, "a day later", () => app.Time.Advance(TimeSpan.FromMinutes(10)));

        await Eventually.SettleAsync();
        var checks = app.Engine.Verifications;
        app.Coordinator.RequestVerification(Game);
        await Eventually.HoldsAsync(() => app.Engine.Verifications == checks + 1, "the player opened the game");
    }

    [Fact]
    public async Task Waits_for_the_store_to_leave_the_game_alone_before_checking_it()
    {
        await using var app = new CoordinatorHarness().WithGameAndProject();
        app.Library.Publish(Installed() with { StoreActivityAt = Now.AddSeconds(-5) });
        app.Engine.Add(Record());
        await app.StartAsync();

        await Eventually.SettleAsync();
        app.Engine.Verifications.Should().Be(0);
        app.Time.Advance(SafetyGate.QuietPeriod);
        await Eventually.HoldsAsync(() => app.Engine.Verifications == 1, "the store left the game alone");
    }

    [Fact]
    public async Task Never_checks_a_game_a_job_works_on()
    {
        await using var app = new CoordinatorHarness().WithGameAndProject();
        var record = app.Engine.Add(Record());
        await app.StartAsync();
        await Eventually.HoldsAsync(() => app.Engine.Verifications == 1, "the first check");
        app.Catalogue.PageGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var repair = app.Jobs.Start(new RepairJobRequest(record));
        await app.GameAsync(game => game.Status.State == MyGameState.Busy, "the repair runs");

        app.Coordinator.RequestVerification(Game);
        await Eventually.SettleAsync();

        app.Engine.Verifications.Should().Be(1);
        app.Catalogue.PageGate.SetResult();
        await repair.Completion;
        await Eventually.HoldsAsync(() => app.Engine.Verifications == 2, "the repaired install is checked");
    }

    [Fact]
    public async Task Follows_a_game_the_store_moved_once_when_the_install_is_found_there()
    {
        await using var app = new CoordinatorHarness().WithGameAndProject();
        var record = app.Engine.Add(Record());
        var moved = Installed(folder: Path.Join("Library B", "Example"));
        app.Library.Publish(moved);

        await app.StartAsync();

        var state = await app.GameAsync(game => game.Installed?.InstallDirectory == moved.InstallDirectory && game.Job is null,
            "the install follows the game");
        state.Find(Game)!.Status.State.Should().Be(MyGameState.Installed);
        state.Find(Game)!.Installed!.Id.Should().Be(record.Id);
        app.Engine.Calls.Count(call => call == "adopt").Should().Be(1);
    }

    [Fact]
    public async Task Follows_a_moved_game_the_store_is_busy_with_only_once_the_store_changed_something()
    {
        await using var app = new CoordinatorHarness().WithGameAndProject();
        app.Engine.Add(Record());
        var moved = Installed(folder: Path.Join("Library B", "Example"));
        app.Library.Publish(moved);
        app.Library.Describe(moved with { State = GameInstallState.Updating, Reason = GameStateReason.StoreBusy });

        await app.StartAsync();
        await Eventually.HoldsAsync(() => app.Library.ProbeCount > 0 && !app.Jobs.IsBusy, "the move waits for the store");
        await Eventually.SettleAsync();
        app.Library.ProbeCount.Should().Be(1, "a busy store is not asked again before it changed something");

        app.Library.Probes.Clear();
        app.Library.Publish(moved);

        await app.GameAsync(game => game.Installed?.InstallDirectory == moved.InstallDirectory && game.Job is null,
            "the install follows the game");
        app.Engine.Calls.Count(call => call == "adopt").Should().Be(1);
    }

    [Fact]
    public async Task Leaves_a_moved_game_orphaned_when_the_install_is_not_there_and_does_not_try_again()
    {
        await using var app = new CoordinatorHarness().WithGameAndProject();
        app.Engine.Add(Record());
        app.Engine.OnAdopt = (_, _) => null;
        app.Library.Publish(Installed(folder: Path.Join("Library B", "Example")));

        await app.StartAsync();
        await Eventually.HoldsAsync(() => app.Engine.Calls.Contains("adopt"), "the install is looked for");
        app.Library.Publish(Installed(folder: Path.Join("Library B", "Example")) with { BuildId = "101" });

        var state = await app.GameAsync(game => game.Job is null && game.Status.Orphan == OrphanReason.GameMoved, "the game stays orphaned");
        state.Find(Game)!.NeedsAttention.Should().BeTrue();
        await Eventually.SettleAsync();
        app.Engine.Calls.Count(call => call == "adopt").Should().Be(1);
    }

    [Fact]
    public async Task An_install_whose_originals_are_gone_is_reported_until_the_player_finds_them_back()
    {
        using var drive = new TempDirectory();
        await using var app = new CoordinatorHarness().WithGameAndProject();
        var record = app.Engine.Add(Record(backup: new BackupFolder(BackupLocation.AppData, drive.Combine("backups"))));
        app.Engine.Backups[record.Id] = true;

        await app.StartAsync();

        var state = await app.GameAsync(game => game.Status.State == MyGameState.BackupsUnavailable, "the originals are missing");
        state.Find(Game)!.Status.BackupDirectory.Should().Be(record.Backup.Directory);
        Directory.CreateDirectory(record.Backup.Directory);
        app.Coordinator.RequestVerification(Game);
        await app.GameAsync(game => game.Status.State == MyGameState.Installed, "the originals are back");
    }

    [Fact]
    public async Task A_failed_job_needs_attention_across_restarts_until_a_job_succeeds()
    {
        await using var app = new CoordinatorHarness().WithGameAndProject();
        await app.StartAsync();
        app.Downloader.Failure = new CatalogueException(CatalogueErrorReason.Offline, "No connection.");

        await app.RunJobAsync(JobsHarness.Install());

        var failed = await app.GameAsync(game => game.LastFailure is not null, "the failure shows");
        var failure = failed.Find(Game)!.LastFailure!;
        failure.Failure!.Reason.Should().Be(JobFailureReason.Offline);
        failure.PackageFileId.Should().Be(FileOne);
        failed.Find(Game)!.NeedsAttention.Should().BeTrue();
        await Eventually.HoldsAsync(() => app.Failures.Stored?.Games.ContainsKey(Game) == true, "the failure is saved");

        app.Downloader.Failure = null;
        await app.RunJobAsync(JobsHarness.Install());

        await app.GameAsync(game => game.LastFailure is null && game.Installed is not null, "the failure is cleared");
        await Eventually.HoldsAsync(() => app.Failures.Stored?.Games.Count == 0, "the cleared failure is saved");
    }

    [Fact]
    public async Task A_saved_failure_shows_after_a_start_until_the_player_dismisses_it()
    {
        await using var app = new CoordinatorHarness().WithGameAndProject();
        var failure = new FailedAttempt(JobKind.Update, JobOrigin.Automatic, Now.AddHours(-1), null) { LostPrevious = true };
        await app.Failures.SaveAsync(new FailedAttempts(new ValueDictionary<GameKey, FailedAttempt>(
            ImmutableDictionary<GameKey, FailedAttempt>.Empty.Add(Game, failure))), CoordinatorHarness.Token);

        var state = await app.StartAsync();

        state.Find(Game)!.LastFailure.Should().Be(failure);
        app.Coordinator.DismissFailure(Game);
        await app.GameAsync(game => game.LastFailure is null, "the failure is dismissed");
    }

    [Fact]
    public async Task A_game_an_automatic_job_found_running_shows_as_running_until_it_exits()
    {
        await using var app = new CoordinatorHarness().WithGameAndProject();
        await app.StartAsync();
        app.Library.Runs(Game, "ExampleGame");

        (await app.RunJobAsync(JobsHarness.Install(origin: JobOrigin.Automatic))).Outcome.Should().Be(JobOutcome.Deferred);

        var running = await app.GameAsync(game => game.Status.GameState == GameInstallState.Running, "the game runs");
        running.Find(Game)!.RunningProcesses.Should().Equal("ExampleGame");
        app.RunningGames.ExitAll();
        await app.GameAsync(game => game.Status.State == MyGameState.NotInstalled, "the game exited");
    }

    [Fact]
    public async Task Indexes_games_found_after_the_catalogue_refresh_from_the_saved_catalogue()
    {
        await using var app = new CoordinatorHarness().WithGameAndProject();
        app.Catalogue.Publish(CatalogueState.Fresh, Entry(release: Release(ReleaseOne, 1, File(FileOne))),
            Entry(OtherProject, Release(ReleaseTwo, 1, File(FileTwo)), OtherGame));
        await app.StartAsync();
        app.Library.Publish(Installed(), Installed(OtherGame));
        await app.StateAsync(state => state.Games.Count == 2, "both games are seen");
        app.Coordinator.IndexRequestForRefresh().Games.Should().BeEquivalentTo([Game, OtherGame]);
        app.Catalogue.Rebuilds.Should().BeEmpty("the catalogue's next refresh indexes them");

        app.Library.Publish(Installed(), Installed(OtherGame), Installed(Unlisted));

        await Eventually.HoldsAsync(() => !app.Catalogue.Rebuilds.IsEmpty, "the new game is indexed");
        app.Catalogue.Rebuilds.Single().Games.Should().BeEquivalentTo([Game, OtherGame, Unlisted]);
    }

    [Fact]
    public async Task Removes_old_downloads_after_a_job_but_keeps_those_of_installs_and_of_failed_jobs()
    {
        await using var app = new CoordinatorHarness().WithGameAndProject();
        app.Library.Publish(Installed(), Installed(OtherGame));
        app.Catalogue.Publish(CatalogueState.Fresh, Entry(release: Release(ReleaseOne, 1, File(FileOne))),
            Entry(OtherProject, Release(ReleaseTwo, 1, File(FileTwo)), OtherGame));
        app.Catalogue.Add(Page(OtherProject, Release(ReleaseTwo, 1, File(FileTwo)), OtherGame));
        var stale = Guid.NewGuid();
        app.Downloader.Cached[stale] = true;
        await app.StartAsync();
        await app.RunJobAsync(JobsHarness.Install());
        app.Engine.NextInstalls.Enqueue((_, _, _) => throw new InstallBlockedException([new MissingFileIssue("data/strings.pak")]));

        await app.RunJobAsync(new InstallJobRequest(OtherGame, "Other Game", Locator(OtherProject), null));

        await Eventually.HoldsAsync(() => !app.Downloader.Cached.ContainsKey(stale), "the old download is removed");
        app.Downloader.Cached.Keys.Should().BeEquivalentTo([FileOne, FileTwo]);
    }

    [Fact]
    public async Task Lists_leftover_backups_only_of_games_the_store_confirms_gone()
    {
        await using var app = new CoordinatorHarness().WithGameAndProject();
        var gone = app.Engine.Add(Record(OtherGame));
        var unplugged = app.Engine.Add(Record(new GameKey(StoreId.Gog, "1207658924")));
        app.Engine.Leftovers = [new LeftoverBackup(gone.Id, OtherGame, "Other Game", "backups", 2048),
            new LeftoverBackup(unplugged.Id, unplugged.Game, "Offline Game", "backups", 1024)];
        app.Library.Publish([Installed()], cached: false,
            new StoreStatus(StoreId.Steam, true, false, true, [new LibraryLocation("library-1", "steam-library", "library", LibraryState.Online)]),
            new StoreStatus(StoreId.Gog, true, false, true, [new LibraryLocation("gog", "gog-registry", null, LibraryState.Offline)]));
        await app.StartAsync();

        var leftovers = await app.Coordinator.FindLeftoverBackupsAsync(CoordinatorHarness.Token);

        leftovers.Should().ContainSingle().Which.RecordId.Should().Be(gone.Id);
    }

    [Fact]
    public async Task Moves_the_backups_of_every_install_when_the_player_picks_another_place()
    {
        await using var app = new CoordinatorHarness().WithGameAndProject();
        app.Library.Publish(Installed(), Installed(OtherGame));
        app.Engine.Add(Record());
        app.Engine.Add(Record(OtherGame));
        await app.StartAsync();
        await app.StateAsync(state => state.Games.Count(game => game.Installed is not null) == 2, "both installs are listed");

        var jobs = app.Coordinator.MoveBackups(new BackupChoice(BackupLocation.AppData));

        jobs.Should().HaveCount(2);
        foreach (var job in jobs)
        {
            (await job.Completion).Installed!.Backup.Location.Should().Be(BackupLocation.AppData);
        }
    }

    [Fact]
    public async Task A_slow_reader_gets_the_latest_state_without_the_ones_in_between()
    {
        await using var app = new CoordinatorHarness().WithGameAndProject();
        var first = await app.StartAsync();

        app.Library.Publish(Installed(build: "101"));
        app.Library.Publish(Installed(build: "102"));
        var shown = await app.StateAsync(state => state.Find(Game)?.Game?.BuildId == "102", "the last build shows");

        var latest = app.Coordinator.WaitForChangeAsync(first.Version, CoordinatorHarness.Token);

        latest.IsCompletedSuccessfully.Should().BeTrue();
        (await latest).Version.Should().BeGreaterThanOrEqualTo(shown.Version);
    }
}
