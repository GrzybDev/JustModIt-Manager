using System.Collections.Concurrent;
using System.Collections.Immutable;
using JustModIt.Manager.Core.Collections;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.Library;
using JustModIt.Manager.Core.Library.Steam;
using JustModIt.Manager.Core.Library.Watching;
using JustModIt.Manager.Core.Safety;
using JustModIt.Manager.Core.Settings;
using JustModIt.Manager.Core.Tests.Library.Steam;
using JustModIt.Manager.Core.Tests.Library.Watching;
using JustModIt.Manager.Core.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace JustModIt.Manager.Core.Tests.Library;

public sealed class LibraryServiceTests : IAsyncDisposable
{
    private const string LibraryPath = @"D:\Library";

    private static readonly LibraryLocation SteamLibrary = new(LibraryPath, "steam-library", LibraryPath, LibraryState.Online);
    private static readonly WatchedFolder SteamFolder = new(@"D:\Library\steamapps", ["appmanifest_*.acf"]);
    private static readonly WatchedFolder GogFolder = new(@"E:\GOG\Example", ["goggame-*.info"]);

    private readonly CountingTimeProvider _time = new();
    private readonly FakeLibraryProvider _steam = new(StoreId.Steam);
    private readonly FakeLibraryProvider _gog = new(StoreId.Gog);
    private readonly FakeWatcher _watcher = new();
    private readonly FakeSystemEvents _events = new();
    private readonly InMemoryDocumentStore<LibrarySnapshot> _snapshots = new();
    private readonly FakeRunningGames _running = new();
    private readonly InMemoryFileSystem _files = new();
    private readonly ConcurrentQueue<LibrarySnapshot> _published = new();
    private readonly CancellationTokenSource _stop = new();
    private SettingsService? _settings;
    private LibraryService? _service;
    private Task? _run;

    public LibraryServiceTests()
    {
        _steam.Result = _steam.Result with { Libraries = [SteamLibrary] };
        _files.AddDirectory(LibraryPath);
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private LibraryService Service => _service!;

    [Fact]
    public async Task The_saved_snapshot_is_shown_until_the_first_scan_replaces_it()
    {
        var saved = new LibrarySnapshot { Games = Games(Game("1001")), Stores = [new StoreStatus(StoreId.Steam, true, false, true, [SteamLibrary])] };
        await _snapshots.SaveAsync(saved, Token);
        _steam.Finds(Game("1002"));

        await StartAsync();

        _published.Select(snapshot => (snapshot.IsCached, snapshot.Version, Keys: string.Join(",", snapshot.Games.Keys))).Should().Equal(
            (true, 1L, "steam:1001"),
            (false, 2L, "steam:1002"));
        Service.Snapshot.ScannedAt.Should().Be(_time.GetUtcNow());
        await Eventually.HoldsAsync(() => _snapshots.Saves.Count == 2, "the scanned snapshot is saved");
        _snapshots.Saves[^1].Should().BeSameAs(Service.Snapshot);
        (_steam.Scans, _gog.Scans).Should().Be((1, 1));
    }

    [Fact]
    public async Task A_new_watch_is_followed_by_one_more_scan_of_its_store()
    {
        _steam.Result = _steam.Result with { Watch = new WatchPlan([SteamFolder], []) };

        await StartAsync();
        _watcher.Active.Should().Equal(SteamFolder);
        await _time.AdvanceAndWaitAsync(LibraryService.Debounce);

        (_steam.Scans, _gog.Scans).Should().Be((2, 1));
        _watcher.Started.Should().Be(1);
    }

    [Fact]
    public async Task Only_the_changed_store_is_rescanned_once_its_changes_settle()
    {
        await StartWatchedAsync();

        await _time.WakeAsync(() => _watcher.Report(WatchSignal.Changed, SteamFolder), "the change is noticed");
        _time.Advance(TimeSpan.FromSeconds(2));
        _watcher.Report(WatchSignal.Changed, SteamFolder);
        await _time.AdvanceAndWaitAsync(TimeSpan.FromSeconds(1));
        _steam.Scans.Should().Be(2, "changes kept coming");

        await _time.AdvanceAndWaitAsync(TimeSpan.FromSeconds(2));

        (_steam.Scans, _gog.Scans).Should().Be((3, 2));
    }

    [Fact]
    public async Task Each_store_settles_on_its_own()
    {
        await StartWatchedAsync();

        await _time.WakeAsync(() => _watcher.Report(WatchSignal.Changed, SteamFolder), "the Steam change is noticed");
        _time.Advance(TimeSpan.FromSeconds(2));
        await _time.WakeAsync(() => _watcher.Report(WatchSignal.Changed, GogFolder), "the GOG change is noticed");
        await _time.AdvanceAndWaitAsync(TimeSpan.FromSeconds(1));
        (_steam.Scans, _gog.Scans).Should().Be((3, 2));

        await _time.AdvanceAndWaitAsync(TimeSpan.FromSeconds(2));

        (_steam.Scans, _gog.Scans).Should().Be((3, 3));
    }

    [Fact]
    public async Task A_store_that_keeps_changing_is_rescanned_after_the_longest_delay()
    {
        await StartWatchedAsync();
        await _time.WakeAsync(() => _watcher.Report(WatchSignal.Changed, SteamFolder), "the change is noticed");
        _time.Advance(TimeSpan.FromSeconds(2));
        _watcher.Report(WatchSignal.Changed, SteamFolder);

        // From here on every step passes the moment the loop expected the changes to settle, so it wakes and sleeps again.
        for (var elapsed = TimeSpan.FromSeconds(4); elapsed < LibraryService.MaxDebounce; elapsed += TimeSpan.FromSeconds(2))
        {
            await _time.AdvanceAndWaitAsync(TimeSpan.FromSeconds(2));
            _watcher.Report(WatchSignal.Changed, SteamFolder);
        }

        // Delays shorter than a millisecond end at once, so the last step before the deadline is a millisecond.
        await _time.AdvanceAndWaitAsync(TimeSpan.FromSeconds(2) - TimeSpan.FromMilliseconds(1));
        _steam.Scans.Should().Be(2, "changes kept coming");
        await _time.AdvanceAndWaitAsync(TimeSpan.FromMilliseconds(1));
        _steam.Scans.Should().Be(3, "the busy store is rescanned once the longest delay is up");
    }

    [Fact]
    public async Task A_lost_watch_recreates_every_watch_and_scans_every_store()
    {
        await StartWatchedAsync();
        var started = _watcher.Started;

        await _time.WakeAsync(() => _watcher.Report(WatchSignal.Lost, SteamFolder), "the loss is noticed");
        await _time.AdvanceAndWaitAsync(LibraryService.Debounce);

        (_steam.Scans, _gog.Scans).Should().Be((3, 3));
        _watcher.Started.Should().Be(started + 2);
        _watcher.Active.Should().BeEquivalentTo(new object[] { SteamFolder, GogFolder });
    }

    [Fact]
    public async Task Stores_are_read_again_only_on_the_six_hour_safety_net_while_nothing_changes()
    {
        await StartAsync();

        _time.Advance(LibraryService.RescanInterval - TimeSpan.FromTicks(1));
        await Eventually.SettleAsync();
        (_steam.Scans, _gog.Scans).Should().Be((1, 1));
        await _time.AdvanceAndWaitAsync(TimeSpan.FromTicks(1));

        (_steam.Scans, _gog.Scans).Should().Be((2, 2));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Resuming_from_sleep_or_a_new_volume_rescans_every_store(bool resumed)
    {
        await StartAsync();

        await _time.WakeAsync(() => _events.Raise(resumed), "the event is noticed");
        await _time.AdvanceAndWaitAsync(LibraryService.Debounce);

        (_steam.Scans, _gog.Scans).Should().Be((2, 2));
    }

    [Fact]
    public async Task Checking_now_scans_every_store_and_completes_afterwards()
    {
        await StartAsync();
        _steam.Finds(Game("1001"));

        await Service.CheckNowAsync(Token);

        (_steam.Scans, _gog.Scans).Should().Be((2, 2));
        Service.Snapshot.Find(Key("1001")).Should().NotBeNull();
    }

    [Fact]
    public async Task A_store_whose_scan_fails_keeps_its_games_as_stale_and_the_others_go_on()
    {
        _steam.Finds(Game("1001"));
        _gog.Finds(Game("2001", store: StoreId.Gog));
        await StartAsync();
        _steam.Failure = new InvalidOperationException("The store's data is unreadable.");
        _gog.Finds(Game("2001", store: StoreId.Gog), Game("2002", store: StoreId.Gog));

        await Service.CheckNowAsync(Token);

        var snapshot = Service.Snapshot;
        snapshot.FindStore(StoreId.Steam)!.ScanFailed.Should().BeTrue();
        snapshot.Find(Key("1001"))!.IsStale.Should().BeTrue();
        snapshot.Find(Key("1001"))!.IsReady.Should().BeFalse();
        snapshot.Games.Keys.Where(key => key.Store == StoreId.Gog).Should().HaveCount(2);

        _steam.Failure = null;
        await Service.CheckNowAsync(Token);

        Service.Snapshot.Find(Key("1001"))!.IsReady.Should().BeTrue();
        Service.Snapshot.FindStore(StoreId.Steam)!.ScanFailed.Should().BeFalse();
    }

    [Fact]
    public async Task Games_of_an_offline_library_are_kept_as_offline_until_it_returns()
    {
        _steam.Finds(Game("1001"));
        await StartAsync();

        // A drive being unplugged is often caught half-read first.
        _steam.Result = _steam.Result with { Games = [], Libraries = [SteamLibrary with { State = LibraryState.Unreadable }], IsComplete = false };
        await Service.CheckNowAsync(Token);
        _steam.Result = _steam.Result with { Libraries = [SteamLibrary with { State = LibraryState.Offline }], IsComplete = true };
        await Service.CheckNowAsync(Token);
        var offline = Service.Snapshot.Find(Key("1001"));
        await Service.CheckNowAsync(Token);
        var stillOffline = Service.Snapshot.Find(Key("1001"));
        _steam.Result = _steam.Result with { Games = [Game("1001")], Libraries = [SteamLibrary] };
        await Service.CheckNowAsync(Token);

        offline!.State.Should().Be(GameInstallState.Offline);
        offline.Reason.Should().Be(GameStateReason.LibraryOffline);
        offline.IsStale.Should().BeFalse();
        stillOffline.Should().Be(offline);
        Service.Snapshot.Find(Key("1001"))!.IsReady.Should().BeTrue();
        Service.Snapshot.ConfirmsGone(Key("1001"), Game("1001").InstallDirectory).Should().BeFalse();
    }

    [Fact]
    public async Task An_unreadable_library_or_list_keeps_the_last_good_games_as_stale()
    {
        var other = new LibraryLocation(@"E:\Other", "steam-library", @"E:\Other", LibraryState.Online);
        _steam.Result = _steam.Result with { Libraries = [SteamLibrary, other], Games = [Game("1001"), Game("1002", library: other.Id)] };
        await StartAsync();

        _steam.Result = _steam.Result with { Libraries = [SteamLibrary with { State = LibraryState.Unreadable }], Games = [], IsComplete = false };
        await Service.CheckNowAsync(Token);

        var snapshot = Service.Snapshot;
        snapshot.Games.Values.Should().HaveCount(2).And.OnlyContain(game => game.IsStale && game.State == GameInstallState.Installed);
        snapshot.FindStore(StoreId.Steam)!.Libraries.Should().Equal(
            SteamLibrary with { State = LibraryState.Unreadable },
            other with { State = LibraryState.Unreadable });
    }

    [Fact]
    public async Task A_record_caught_mid_write_keeps_the_game_as_stale_and_restarts_its_quiet_period()
    {
        var written = _time.GetUtcNow().AddMinutes(1);
        _steam.Finds(Game("1001"));
        await StartAsync();

        _steam.Result = _steam.Result with { Games = [], UnreadableGames = [new UnreadableGame(Key("1001"), LibraryPath, written), new UnreadableGame(Key("1009"), LibraryPath, null)] };
        await Service.CheckNowAsync(Token);

        var game = Service.Snapshot.Find(Key("1001"))!;
        game.IsStale.Should().BeTrue();
        game.StoreActivityAt.Should().Be(written);
        Service.Snapshot.Find(Key("1009")).Should().BeNull();
    }

    [Fact]
    public async Task A_game_found_again_elsewhere_wins_over_its_offline_copy()
    {
        var other = new LibraryLocation(@"E:\Other", "steam-library", @"E:\Other", LibraryState.Online);
        _steam.Finds(Game("1001"));
        await StartAsync();

        _steam.Result = _steam.Result with { Libraries = [SteamLibrary with { State = LibraryState.Offline }, other], Games = [Game("1001", library: other.Id)] };
        await Service.CheckNowAsync(Token);

        Service.Snapshot.Find(Key("1001"))!.LibraryId.Should().Be(other.Id);
    }

    [Fact]
    public async Task An_unchanged_scan_is_neither_announced_nor_saved()
    {
        _steam.Finds(Game("1001"));
        await StartAsync();
        var version = Service.Snapshot.Version;
        await Eventually.HoldsAsync(() => _snapshots.Saves.Count == 1, "the first snapshot is saved");

        _time.Advance(TimeSpan.FromMinutes(1));
        await Service.CheckNowAsync(Token);
        await Eventually.SettleAsync();

        _published.Should().ContainSingle();
        _snapshots.Saves.Should().ContainSingle();
        Service.Snapshot.Version.Should().Be(version);
        Service.Snapshot.ScannedAt.Should().Be(_time.GetUtcNow());
    }

    [Fact]
    public async Task A_failed_save_or_a_failing_listener_does_not_stop_the_loop()
    {
        await StartAsync();
        await Eventually.HoldsAsync(() => _snapshots.Saves.Count == 1, "the first snapshot is saved");
        _snapshots.FailNextSaves(1);
        Service.Changed += (_, _) => throw new InvalidOperationException("A listener broke.");
        _steam.Finds(Game("1001"));

        await Service.CheckNowAsync(Token);
        await Eventually.HoldsAsync(() => !_snapshots.FailsNextSave, "the save of the change fails");
        _steam.Finds(Game("1001"), Game("1002"));
        await Service.CheckNowAsync(Token);

        Service.Snapshot.Games.Should().HaveCount(2);
        await Eventually.HoldsAsync(() => _snapshots.Saves is [.., { Games.Count: 2 }], "the later snapshot is saved");
    }

    [Fact]
    public async Task Folders_the_player_adds_are_scanned_with_their_store()
    {
        await StartAsync();

        await _time.WakeAsync(() => _settings!.Update(settings => settings.WithLibraryFolder(new LibraryFolder(StoreId.Gog, @"E:\GOG"))),
            "the settings change is noticed");
        await _time.AdvanceAndWaitAsync(LibraryService.Debounce);

        (_steam.Scans, _gog.Scans).Should().Be((1, 2));
        _gog.Contexts.Last().ManualFolders.Should().Equal(@"E:\GOG");
    }

    [Fact]
    public async Task A_download_that_stops_changing_is_read_again_and_counts_as_left_over()
    {
        var steam = new SteamTree();
        steam.AddApp(1001, "Example Quest");
        steam.AddDownload(1001, changedAt: _time.GetUtcNow());
        await StartAsync(steam.CreateProvider(time: _time));
        await _time.AdvanceAndWaitAsync(LibraryService.Debounce);
        Service.Snapshot.Find(Key("1001"))!.State.Should().Be(GameInstallState.Updating);

        await _time.AdvanceAndWaitAsync(SteamDownloads.ActiveWindow);

        Service.Snapshot.Find(Key("1001")).Should().Match<InstalledGame>(game =>
            game.IsReady && game.Reason == GameStateReason.UnfinishedDownload);
    }

    [Fact]
    public async Task A_probe_reads_the_game_again_and_looks_for_running_processes()
    {
        _steam.Finds(Game("1001"));
        await StartAsync();

        var idle = await Service.ProbeAsync(Key("1001"), Token);
        _running.Processes = [new RunningProcess(42, "Example.exe", @"D:\Library\steamapps\common\1001\Example.exe")];
        var running = await Service.ProbeAsync(Key("1001"), Token);

        (idle.AllowsChange(forUninstall: false) && idle.RunningProcesses.Count == 0).Should().BeTrue();
        running.Game!.State.Should().Be(GameInstallState.Running);
        running.RunningProcesses.Should().ContainSingle();
        _running.Directories.Should().Equal(Game("1001").InstallDirectory, Game("1001").InstallDirectory);
    }

    [Fact]
    public async Task A_probe_that_finds_a_change_asks_for_a_rescan_of_the_store()
    {
        _steam.Finds(Game("1001"));
        await StartAsync();
        _steam.Probe = game => game with { State = GameInstallState.Updating, Reason = GameStateReason.StoreBusy };

        var timers = _time.TimersCreated;
        var probe = await Service.ProbeAsync(Key("1001"), Token);
        await Eventually.HoldsAsync(() => _time.TimersCreated > timers, "the rescan is scheduled");
        await _time.AdvanceAndWaitAsync(LibraryService.Debounce);

        probe.Game!.State.Should().Be(GameInstallState.Updating);
        probe.RunningProcesses.Should().BeEmpty();
        _running.Directories.Should().BeEmpty();
        _steam.Scans.Should().Be(2);
    }

    [Fact]
    public async Task A_probe_of_a_game_waiting_for_a_store_update_names_what_runs_and_keeps_the_state()
    {
        _steam.Finds(Game("1001") with { State = GameInstallState.UpdatePending, Reason = GameStateReason.UpdateRequired });
        await StartAsync();
        _running.Processes = [new RunningProcess(42, "Example.exe", @"D:\Library\steamapps\common\1001\Example.exe")];

        var probe = await Service.ProbeAsync(Key("1001"), Token);

        probe.Game!.State.Should().Be(GameInstallState.UpdatePending);
        probe.RunningProcesses.Should().ContainSingle();
    }

    [Fact]
    public async Task A_game_the_snapshot_does_not_know_yet_is_probed_in_the_whole_store()
    {
        _steam.Finds(Game("1001"));
        await StartAsync();
        _steam.Finds(Game("1001"), Game("1002"));

        var probe = await Service.ProbeAsync(Key("1002"), Token);

        probe.Game.Should().Be(Game("1002"));
        (probe.AllowsChange(forUninstall: false) && probe.RunningProcesses.Count == 0).Should().BeTrue();
        _steam.Scans.Should().Be(2);
        _running.Directories.Should().Equal(Game("1002").InstallDirectory);
    }

    [Fact]
    public async Task A_probe_of_an_unknown_or_uninstalled_game_finds_nothing()
    {
        _steam.Finds(Game("1001"));
        await StartAsync();
        _steam.Probe = _ => null;

        (await Service.ProbeAsync(Key("9999"), Token)).Game.Should().BeNull();
        (await Service.ProbeAsync(Key("1001"), Token)).Game.Should().BeNull();
    }

    [Fact]
    public async Task A_folder_with_a_stores_records_is_added_to_the_settings()
    {
        _files.AddDirectory(@"E:\Games");
        _gog.Evidence = LibraryFolderEvidence.Accepted(@"E:\Games");
        await StartAsync();

        var result = await Service.AddLibraryFolderAsync(@"E:\Games", Token);

        result.Should().Be(new LibraryFolderResult(new LibraryFolder(StoreId.Gog, @"E:\Games"), null));
        _settings!.Current.LibraryFolders.Should().Equal(new LibraryFolder(StoreId.Gog, @"E:\Games"));
        (await Service.AddLibraryFolderAsync(@"E:\Games", Token)).Rejection.Should().Be(LibraryFolderRejection.AlreadyAdded);
        await Eventually.HoldsAsync(() => _gog.Scans > 1, "the added folder is scanned");

        Service.RemoveLibraryFolder(new LibraryFolder(StoreId.Gog, @"E:\Games"));

        _settings.Current.LibraryFolders.Should().BeEmpty();
    }

    [Fact]
    public async Task A_folder_is_refused_with_the_most_specific_reason()
    {
        _files.AddDirectory(@"E:\Expansion");
        await StartAsync();

        var noEvidence = await Service.AddLibraryFolderAsync(@"E:\Expansion", Token);
        _gog.Evidence = LibraryFolderEvidence.Rejected(LibraryFolderRejection.DlcOnly);
        var dlcOnly = await Service.AddLibraryFolderAsync(@"E:\Expansion", Token);

        _steam.Evidence = LibraryFolderEvidence.Accepted(LibraryPath);
        var known = await Service.AddLibraryFolderAsync(LibraryPath, Token);

        noEvidence.Rejection.Should().Be(LibraryFolderRejection.NoEvidence);
        dlcOnly.Rejection.Should().Be(LibraryFolderRejection.DlcOnly);
        known.Rejection.Should().Be(LibraryFolderRejection.AlreadyDetected);
        (await Service.AddLibraryFolderAsync(@"E:\Missing", Token)).Rejection.Should().Be(LibraryFolderRejection.Unreachable);
        (await Service.AddLibraryFolderAsync(@"Relative\Folder", Token)).Rejection.Should().Be(LibraryFolderRejection.Unreachable);
        _settings!.Current.LibraryFolders.Should().BeEmpty();
    }

    [Fact]
    public async Task A_folder_that_does_not_answer_in_time_is_unreachable()
    {
        _files.AddDirectory(@"\\server\share");
        _steam.InspectionHangs = true;
        await StartAsync();

        var adding = Service.AddLibraryFolderAsync(@"\\server\share", Token);
        await Eventually.HoldsAsync(() => _steam.Inspections == 1, "the folder is looked into");
        _time.Advance(LibraryAccess.TimeLimit);

        (await adding.WaitAsync(TimeSpan.FromSeconds(30), Token)).Rejection.Should().Be(LibraryFolderRejection.Unreachable);
    }

    [Fact]
    public async Task A_library_that_does_not_answer_stays_offline_while_the_other_libraries_and_stores_go_on()
    {
        var steam = new SteamTree();
        var slow = steam.AddLibrary(@"D:\Slow");
        steam.AddApp(1001, "Slow Quest", library: slow);
        steam.AddApp(1002, "Root Quest");
        using var files = new HangingFileSystem(steam.Files);
        _gog.Finds(Game("2001", store: StoreId.Gog));
        await StartWithSteamAsync(steam, files);
        files.Hang(slow);

        await CheckRunningOutOfTimeAsync(files);
        await Service.CheckNowAsync(Token);

        var snapshot = Service.Snapshot;
        snapshot.Find(Key("1001")).Should().Match<InstalledGame>(game =>
            game.State == GameInstallState.Offline && game.Reason == GameStateReason.LibraryOffline && game.Name == "Slow Quest");
        snapshot.Find(Key("1002"))!.IsReady.Should().BeTrue();
        snapshot.Find(Key("2001", StoreId.Gog))!.IsReady.Should().BeTrue();
        snapshot.ConfirmsGone(Key("1003"), Game("1003").InstallDirectory).Should().BeFalse();
        files.HungCalls.Should().Be(1, "a drive whose read still hangs is not read again");
    }

    [Fact]
    public async Task A_library_that_answers_late_is_read_again()
    {
        var steam = new SteamTree();
        var slow = steam.AddLibrary(@"D:\Slow");
        steam.AddApp(1001, "Slow Quest", library: slow);
        using var files = new HangingFileSystem(steam.Files);
        await StartWithSteamAsync(steam, files);
        files.Hang(slow);
        await CheckRunningOutOfTimeAsync(files);

        await _time.WakeAsync(() => files.Answer(slow), "the late answer asks for another read");
        await _time.AdvanceAndWaitAsync(LibraryService.Debounce);

        await Eventually.HoldsAsync(() => Service.Snapshot.Find(Key("1001"))!.IsReady, "the library is read again");
    }

    [Fact]
    public async Task A_watch_that_hangs_while_starting_does_not_hold_up_the_loop()
    {
        _steam.Result = _steam.Result with { Watch = new WatchPlan([SteamFolder], []) };
        _watcher.Hang(SteamFolder);
        await CreateAsync();
        _run = Service.RunAsync(_stop.Token);

        await Eventually.HoldsAsync(() => _watcher.Blocked == 1, "starting the watch hangs");
        _published.Should().ContainSingle("what the scan found does not wait for the watches");
        await Eventually.SettleAsync();
        await _time.WakeAsync(() => _time.Advance(LibraryAccess.TimeLimit), "the first scan is done without the watch");
        _published.Should().ContainSingle();
        _watcher.Active.Should().BeEmpty();

        await _time.WakeAsync(_watcher.Answer, "the watch that started late asks for another read");
        await _time.AdvanceAndWaitAsync(LibraryService.Debounce);

        _watcher.Active.Should().Equal(SteamFolder);
        (_steam.Scans, _gog.Scans).Should().Be((2, 1));
    }

    [Fact]
    public async Task A_rewatch_that_hangs_does_not_hold_up_the_full_scan_and_reads_every_store_again_once_done()
    {
        await StartWatchedAsync();
        _watcher.Hang(SteamFolder);
        await _time.WakeAsync(() => _watcher.Report(WatchSignal.Lost, SteamFolder), "the loss is noticed");

        _time.Advance(LibraryService.Debounce);
        await Eventually.HoldsAsync(() => _watcher.Blocked == 1, "restarting the watch hangs");
        await Eventually.SettleAsync();
        await _time.WakeAsync(() => _time.Advance(LibraryAccess.TimeLimit), "the full scan is done without the watch");
        (_steam.Scans, _gog.Scans).Should().Be((3, 3));

        await _time.WakeAsync(_watcher.Answer, "the rewatch that ended late asks for another read");
        await _time.AdvanceAndWaitAsync(LibraryService.Debounce);

        (_steam.Scans, _gog.Scans).Should().Be((4, 4));
        _watcher.Active.Should().BeEquivalentTo(new object[] { SteamFolder, GogFolder });
    }

    [Theory]
    [InlineData("2001")]
    [InlineData("2002")]
    public async Task A_game_whose_folder_does_not_answer_keeps_its_last_data_as_offline(string recordedId)
    {
        var game = Game("2001", store: StoreId.Gog) with { BuildId = "42" };
        _gog.Finds(game);
        await StartAsync();

        // A record may name a pack whose folder holds the game under another id.
        _gog.Result = _gog.Result with { Games = [], UnansweredGames = [new UnansweredGame(Key(recordedId, StoreId.Gog), game.InstallDirectory)] };
        await Service.CheckNowAsync(Token);

        Service.Snapshot.Find(game.Key).Should().Be(game with { State = GameInstallState.Offline, Reason = GameStateReason.LibraryOffline });
        (await Service.ProbeAsync(game.Key, Token)).AllowsChange(forUninstall: true).Should().BeFalse();
    }

    [Fact]
    public async Task The_report_counts_games_per_store_and_library()
    {
        var offline = new LibraryLocation(@"E:\Unplugged", "steam-library", @"E:\Unplugged", LibraryState.Offline);
        _steam.Result = _steam.Result with { Libraries = [SteamLibrary, offline with { UnreadableEntries = 1 }], Games = [Game("1001"), Game("1002")] };
        _gog.Result = _gog.Result with { IsPresent = false };
        await StartAsync();

        var report = DetectionReport.From(Service.Snapshot);

        report.ScannedAt.Should().Be(Service.Snapshot.ScannedAt);
        report.Stores.Should().HaveCount(2);
        var steam = report.Stores[0];
        (steam.IsFound, steam.GameCount, steam.ScanFailed, steam.IsComplete).Should().Be((true, 2, false, true));
        steam.Libraries.Select(library => (library.Library.State, library.Library.IsFullyRead, library.GameCount))
            .Should().Equal((LibraryState.Online, true, 2), (LibraryState.Offline, false, 0));
        var gog = report.Stores[1];
        (gog.IsFound, gog.GameCount, gog.ScanFailed, gog.IsComplete, gog.Libraries.Count).Should().Be((false, 0, false, true, 0));
    }

    [Fact]
    public async Task A_game_is_confirmed_gone_only_by_a_scan_that_read_everything()
    {
        await StartAsync();

        Service.Snapshot.ConfirmsGone(Key("1001"), Game("1001").InstallDirectory).Should().BeTrue();
        _steam.Result = _steam.Result with
        {
            Libraries = [SteamLibrary with { UnreadableEntries = 1 }],
            UnreadableGames = [new UnreadableGame(Key("1001"), LibraryPath, StoreActivityAt: null)],
        };
        await Service.CheckNowAsync(Token);
        Service.Snapshot.ConfirmsGone(Key("1001"), Game("1001").InstallDirectory).Should().BeFalse("its record may be caught while the store rewrites it");
        _steam.Result = _steam.Result with { Libraries = [SteamLibrary], UnreadableGames = [], IsComplete = false };
        await Service.CheckNowAsync(Token);
        Service.Snapshot.ConfirmsGone(Key("1001"), Game("1001").InstallDirectory).Should().BeFalse();
        LibrarySnapshot.Empty.ConfirmsGone(Key("1001"), Game("1001").InstallDirectory).Should().BeFalse();
        (Service.Snapshot with { IsCached = true }).ConfirmsGone(Key("1001"), Game("1001").InstallDirectory).Should().BeFalse();

        var moved = Game("1001") with { InstallDirectory = Path.Join(LibraryPath, "steamapps", "common", "1001-moved") };
        _steam.Result = _steam.Result with { IsComplete = true, Games = [moved] };
        await Service.CheckNowAsync(Token);
        Service.Snapshot.ConfirmsGone(Key("1001"), Game("1001").InstallDirectory).Should().BeTrue("the store moved it");
        Service.Snapshot.ConfirmsGone(Key("1001"), moved.InstallDirectory).Should().BeFalse();
    }

    [Fact]
    public async Task Stopping_the_loop_stops_every_watch()
    {
        await StartWatchedAsync();

        await _stop.CancelAsync();

        await FluentActions.Awaiting(() => _run!).Should().ThrowAsync<OperationCanceledException>();
        _watcher.Active.Should().BeEmpty();
        Service.ActiveWatchCount.Should().Be(0);
    }

    [Fact]
    public async Task Checks_that_the_stopped_loop_will_never_run_are_cancelled()
    {
        await CreateAsync();
        var pending = Service.CheckNowAsync(Token);
        await _stop.CancelAsync();

        await FluentActions.Awaiting(() => Service.RunAsync(_stop.Token)).Should().ThrowAsync<OperationCanceledException>();

        await FluentActions.Awaiting(() => pending).Should().ThrowAsync<OperationCanceledException>();
        await FluentActions.Awaiting(() => Service.CheckNowAsync(Token)).Should().ThrowAsync<OperationCanceledException>();
        _steam.Scans.Should().Be(0);
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        if (_run is not null)
        {
            await FluentActions.Awaiting(() => _run).Should().ThrowAsync<OperationCanceledException>();
        }

        if (_service is not null)
        {
            await _service.DisposeAsync();
        }

        _watcher.Dispose();
        _steam.Dispose();
        _gog.Dispose();
        if (_settings is not null)
        {
            await _settings.DisposeAsync();
        }

        _stop.Dispose();
    }

    private static GameKey Key(string id, StoreId? store = null) => new(store ?? StoreId.Steam, id);

    private static InstalledGame Game(string id, string library = LibraryPath, StoreId? store = null) => new()
    {
        Key = Key(id, store),
        Name = $"Game {id}",
        InstallDirectory = Path.Join(library, "steamapps", "common", id),
        LibraryId = library,
        State = GameInstallState.Installed,
    };

    private static ValueDictionary<GameKey, InstalledGame> Games(params InstalledGame[] games) =>
        new(games.ToDictionary(game => game.Key).ToImmutableDictionary());

    private async Task CreateAsync(ILibraryProvider? steam = null)
    {
        _settings = await SettingsService.LoadAsync(new InMemoryDocumentStore<AppSettings>(), new FakeTimeProvider(),
            NullLogger<SettingsService>.Instance, Token);
        _service = new LibraryService([steam ?? _steam, _gog], _settings, _running, _files, _watcher, _watcher, _events, _snapshots, _time,
            NullLogger<LibraryService>.Instance);
        _service.Changed += (_, snapshot) => _published.Enqueue(snapshot);
    }

    private async Task StartAsync(ILibraryProvider? steam = null)
    {
        await CreateAsync(steam);
        await _time.WakeAsync(() => _run = Service.RunAsync(_stop.Token), "the first scan is done");
    }

    // Starts with a real Steam store, after the second scan that its new watches cause.
    private async Task StartWithSteamAsync(SteamTree steam, HangingFileSystem files)
    {
        await StartAsync(steam.CreateProvider(files: files));
        await _time.AdvanceAndWaitAsync(LibraryService.Debounce);
    }

    // Checks now while a drive does not answer, and waits until the loop sleeps again.
    private async Task CheckRunningOutOfTimeAsync(HangingFileSystem files)
    {
        var timers = _time.TimersCreated;
        var checking = Service.CheckNowAsync(Token);
        await files.RunOutOfTimeAsync(_time);
        await checking;
        await Eventually.HoldsAsync(() => _time.TimersCreated > timers, "the loop sleeps after the check");
    }

    // Starts with a folder watched per store, after the second scan that new watches cause.
    private async Task StartWatchedAsync()
    {
        _steam.Result = _steam.Result with { Watch = new WatchPlan([SteamFolder], []) };
        _gog.Result = _gog.Result with { Watch = new WatchPlan([GogFolder], []) };
        await StartAsync();
        await _time.AdvanceAndWaitAsync(LibraryService.Debounce);
        (_steam.Scans, _gog.Scans).Should().Be((2, 2));
    }

    private sealed class FakeSystemEvents : ISystemEvents
    {
        public event EventHandler? Resumed;

        public event EventHandler? VolumeArrived;

        public void Raise(bool resumed) => (resumed ? Resumed : VolumeArrived)?.Invoke(this, EventArgs.Empty);
    }

    private sealed class FakeRunningGames : IRunningGameDetector
    {
        public ValueList<RunningProcess> Processes { get; set; }

        public ConcurrentQueue<string> Directories { get; } = new();

        public ValueList<RunningProcess> FindProcessesIn(string directory, CancellationToken cancellationToken)
        {
            Directories.Enqueue(directory);
            return Processes;
        }

        public Task WaitForExitAsync(IEnumerable<RunningProcess> processes, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
