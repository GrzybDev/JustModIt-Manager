using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.IO;
using JustModIt.Manager.Core.Jobs;
using JustModIt.Manager.Core.Library;
using JustModIt.Manager.Core.Library.Steam;
using JustModIt.Manager.Core.Platform;
using JustModIt.Manager.Core.Tests.TestSupport;
using Microsoft.Extensions.Time.Testing;

namespace JustModIt.Manager.Core.Tests.Library.Steam;

public sealed class SteamLibraryProviderTests
{
    private static readonly GameKey Quest = new(StoreId.Steam, "1001");

    private readonly SteamTree _steam = new();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_installed_game_is_reported_with_its_details()
    {
        var written = new DateTimeOffset(2026, 4, 2, 8, 30, 0, TimeSpan.Zero);
        _steam.AddApp(1001, "Example Quest", writtenAt: written, buildId: "4242",
            extra: "\"UserConfig\" { \"language\" \"polish\" } \"InstalledDepots\" { \"1002\" { \"manifest\" \"77\" } }");

        var scan = await _steam.ScanAsync();

        scan.IsPresent.Should().BeTrue();
        scan.IsComplete.Should().BeTrue();
        scan.Libraries.Should().Equal(new LibraryLocation(@"c:\steam", SteamLibraryProvider.LibrarySource, @"c:\steam", LibraryState.Online));
        scan.Games.Should().Equal(new InstalledGame
        {
            Key = Quest,
            Name = "Example Quest",
            InstallDirectory = @"c:\steam\steamapps\common\Example Quest",
            LibraryId = @"c:\steam",
            BuildId = "4242",
            State = GameInstallState.Installed,
            Reason = GameStateReason.None,
            StoreActivityAt = written,
            Platform = OperatingSystemKind.Windows,
            Languages = ["pl"],
            Content = ["1002@77"],
            Launch = LaunchLink.TryCreate("steam://rungameid/1001"),
        });
    }

    [Fact]
    public async Task Queued_and_uninstalled_apps_are_hidden_even_with_a_leftover_folder()
    {
        _steam.AddApp(1001, "Queued", stateFlags: 2);
        _steam.AddApp(1002, "Uninstalled", stateFlags: 1);

        (await _steam.ScanAsync()).Games.Should().BeEmpty();
    }

    [Fact]
    public async Task An_installed_app_without_its_folder_is_broken()
    {
        _steam.AddApp(1001, "Example Quest", createFolder: false);

        var game = (await _steam.ScanAsync()).Games.Single();

        game.State.Should().Be(GameInstallState.Broken);
        game.Reason.Should().Be(GameStateReason.InstallDirectoryMissing);
        game.InstallDirectory.Should().BeEquivalentTo(_steam.GameFolder("Example Quest"));
    }

    [Fact]
    public async Task Soundtracks_and_tools_are_not_games()
    {
        _steam.AddApp(1001, "Example Soundtrack", createFolder: false);
        _steam.Files.AddDirectory(Path.Join(_steam.Root, "steamapps", "music", "Example Soundtrack"));
        _steam.AddApp(228980, "Steamworks Common Redistributables");
        _steam.AddApp(1002, "Proton 11.0");

        (await _steam.ScanAsync()).Games.Should().BeEmpty();
    }

    [Fact]
    public async Task A_damaged_manifest_is_reported_without_hiding_the_others()
    {
        _steam.AddApp(1001, "Example Quest");
        var written = new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero);
        _steam.Files.AddFile(_steam.ManifestPath(1002), "\"AppState\" { \"appid\" \"1002\"", written);
        _steam.Files.AddFile(_steam.ManifestPath(1003), "\"AppState\" { \"appid\" \"1004\" \"installdir\" \"Copy\" }",
            SteamTree.WrittenAt);

        var scan = await _steam.ScanAsync();

        scan.Games.Select(game => game.Key).Should().Equal(Quest);
        scan.UnreadableGames.Should().Equal(
            new UnreadableGame(new GameKey(StoreId.Steam, "1002"), @"c:\steam", written),
            new UnreadableGame(new GameKey(StoreId.Steam, "1003"), @"c:\steam", SteamTree.WrittenAt));
        scan.Libraries.Single().UnreadableEntries.Should().Be(2);
    }

    [Fact]
    public async Task Only_files_named_exactly_like_manifests_are_read()
    {
        _steam.AddApp(1001, "Example Quest");
        foreach (var name in (string[])["appmanifest_1002.acf.tmp", "appmanifest_1003.acf.bak", "appmanifest_x.acf", "old_appmanifest_1004.acf"])
        {
            _steam.Files.AddFile(Path.Join(_steam.Root, "steamapps", name), "broken");
        }

        var scan = await _steam.ScanAsync();

        scan.Games.Select(game => game.Key).Should().Equal(Quest);
        scan.UnreadableGames.Should().BeEmpty();
    }

    [Fact]
    public async Task Downloads_and_the_registry_show_work_the_manifest_does_not_and_left_over_data_only_warns()
    {
        var touched = _steam.Time.GetUtcNow() - TimeSpan.FromSeconds(20);
        _steam.AddApp(1001, "Left Over");
        _steam.AddDownload(1001);
        _steam.AddApp(1002, "Starting");
        _steam.AddDownload(1002, changedAt: touched);
        _steam.AddApp(1003, "First Install", stateFlags: 2);
        _steam.Files.AddFile(Path.Join(_steam.Root, "steamapps", "downloading", "state_1003_1004.patch"), "");
        _steam.AddApp(1004, "Updating");
        _steam.SetUpdating(1004);

        var scan = await _steam.ScanAsync();

        var games = scan.Games.ToDictionary(game => game.Name);
        games["Left Over"].Should().Match<InstalledGame>(game => game.IsReady && game.Reason == GameStateReason.UnfinishedDownload);
        games["Starting"].Should().Match<InstalledGame>(game =>
            game.State == GameInstallState.Updating && game.Reason == GameStateReason.DownloadInProgress);
        games["First Install"].State.Should().Be(GameInstallState.Installing);
        games["Updating"].State.Should().Be(GameInstallState.Updating);
        scan.RecheckAt.Should().Be(touched + SteamDownloads.ActiveWindow,
            "the starting game is read again once its data counts as left over");
    }

    [Fact]
    public async Task A_folder_shared_by_several_apps_is_claimed_by_the_installed_one()
    {
        _steam.AddApp(1001, "Example Quest", installDir: "Shared", sizeOnDisk: 10);
        _steam.AddApp(1002, "Example Quest Playtest", installDir: "Shared", sizeOnDisk: 50, createFolder: false, stateFlags: 0x24);

        var game = (await _steam.ScanAsync()).Games.Should().ContainSingle().Subject;

        game.Key.Should().Be(Quest);
        game.State.Should().Be(GameInstallState.Installed);
    }

    [Fact]
    public async Task A_download_dated_far_ahead_by_a_wrong_clock_is_left_over_and_not_waited_for()
    {
        var now = _steam.Time.GetUtcNow();
        _steam.AddApp(1001, "Slightly Ahead");
        _steam.AddDownload(1001, changedAt: now + SafetyGate.ClockSkewTolerance);
        _steam.AddApp(1002, "Far Ahead");
        _steam.AddDownload(1002, changedAt: now + TimeSpan.FromDays(2));

        var scan = await _steam.ScanAsync();

        var games = scan.Games.ToDictionary(game => game.Name);
        games["Slightly Ahead"].State.Should().Be(GameInstallState.Updating);
        games["Far Ahead"].Should().Match<InstalledGame>(game => game.IsReady && game.Reason == GameStateReason.UnfinishedDownload);
        scan.RecheckAt.Should().Be(now + SafetyGate.ClockSkewTolerance + SteamDownloads.ActiveWindow);
    }

    [Fact]
    public async Task A_shared_folder_is_busy_while_another_app_in_it_updates()
    {
        var later = SteamTree.WrittenAt.AddMinutes(5);
        _steam.AddApp(1001, "Example Quest", installDir: "Shared", sizeOnDisk: 50);
        _steam.AddApp(1002, "Example Quest Episode", installDir: "Shared", stateFlags: 1030, writtenAt: later);

        var game = (await _steam.ScanAsync()).Games.Should().ContainSingle().Subject;

        game.Key.Should().Be(Quest);
        game.State.Should().Be(GameInstallState.Updating);
        game.Reason.Should().Be(GameStateReason.SharedFolderBusy);
        game.StoreActivityAt.Should().Be(later);
    }

    [Fact]
    public async Task Games_of_every_library_are_found_and_a_library_listed_twice_is_scanned_once()
    {
        var library = _steam.AddLibrary(@"D:\SteamLibrary");
        _steam.AddLibrary(@"D:\SteamLibrary\");
        _steam.AddApp(1001, "Example Quest", library: library);
        _steam.AddApp(1002, "Root Game");

        var scan = await _steam.ScanAsync();

        scan.Libraries.Select(location => location.Id).Should().Equal(@"c:\steam", @"D:\SteamLibrary");
        scan.Games.Single(game => game.Key == Quest).LibraryId.Should().Be(@"D:\SteamLibrary");
        scan.Games.Should().HaveCount(2);
    }

    [Fact]
    public async Task A_library_reached_through_a_link_is_scanned_once()
    {
        var library = _steam.AddLibrary(@"D:\SteamLibrary");
        _steam.Files.AddDirectoryLink(@"E:\Linked", @"D:\SteamLibrary");
        _steam.AddLibrary(@"E:\Linked", create: false);
        _steam.AddApp(1001, "Example Quest", library: library);

        var scan = await _steam.ScanAsync();

        scan.Libraries.Select(location => location.Id).Should().Equal(@"c:\steam", @"D:\SteamLibrary");
        scan.Games.Should().ContainSingle();
    }

    [Fact]
    public async Task Unavailable_libraries_are_offline_but_a_root_is_always_online()
    {
        _steam.AddLibrary(@"D:\Missing", create: false);
        _steam.AddLibrary(@"E:\Unmounted", mounted: false);
        _steam.Files.AddDirectory(@"F:\NoSteamApps");
        _steam.AddLibrary(@"F:\NoSteamApps", create: false);
        _steam.AddLibrary(_steam.Root, mounted: false);

        var scan = await _steam.ScanAsync();

        scan.Libraries.Select(location => (location.Id, location.State)).Should().Equal(
            (@"c:\steam", LibraryState.Online),
            (@"D:\Missing", LibraryState.Offline),
            (@"E:\Unmounted", LibraryState.Offline),
            (@"F:\NoSteamApps", LibraryState.Offline));
    }

    [Fact]
    public async Task The_old_list_format_and_the_config_copy_are_both_read()
    {
        _steam.Files.AddDirectory(@"D:\Old\steamapps");
        _steam.Files.AddDirectory(@"E:\New\steamapps");
        _steam.Files.AddFile(Path.Join(_steam.Root, "steamapps", "libraryfolders.vdf"),
            "\"LibraryFolders\" { \"ContentStatsID\" \"1\" \"1\" \"D:\\\\Old\" }");
        _steam.Files.AddFile(Path.Join(_steam.Root, "config", "libraryfolders.vdf"),
            "\"libraryfolders\" { \"0\" { \"path\" \"E:\\\\New\" } }");

        var scan = await _steam.ScanAsync();

        scan.Libraries.Select(location => location.Id).Should().Equal(@"c:\steam", @"D:\Old", @"E:\New");
        scan.IsComplete.Should().BeTrue();
    }

    [Fact]
    public async Task An_unreadable_library_list_keeps_the_root_and_marks_the_scan_incomplete()
    {
        _steam.AddApp(1001, "Example Quest");
        _steam.Files.AddFile(Path.Join(_steam.Root, "steamapps", "libraryfolders.vdf"), "\"libraryfolders\" {");
        _steam.Files.AddFile(Path.Join(_steam.Root, "config", "libraryfolders.vdf"), "\"libraryfolders\" { \"0\" ");

        var scan = await _steam.ScanAsync();

        scan.IsComplete.Should().BeFalse();
        scan.Libraries.Select(location => location.Id).Should().Equal(@"c:\steam");
        scan.Games.Should().ContainSingle();
    }

    [Fact]
    public async Task One_readable_copy_of_the_library_list_is_enough()
    {
        _steam.AddLibrary(@"D:\SteamLibrary");
        _steam.Files.AddFile(Path.Join(_steam.Root, "steamapps", "libraryfolders.vdf"), "\"libraryfolders\" {");

        var scan = await _steam.ScanAsync();

        scan.IsComplete.Should().BeTrue();
        scan.Libraries.Select(location => location.Id).Should().Contain(@"D:\SteamLibrary");
    }

    [Fact]
    public async Task Relative_library_paths_are_ignored()
    {
        _steam.Files.AddFile(Path.Join(_steam.Root, "config", "libraryfolders.vdf"),
            "\"libraryfolders\" { \"1\" { \"path\" \"SteamLibrary\" } }");

        (await _steam.ScanAsync()).Libraries.Should().ContainSingle();
    }

    [Fact]
    public async Task Roots_come_from_the_user_registry_then_the_machine_registry_then_program_files()
    {
        var steam = new SteamTree(@"C:\Program Files (x86)\Steam", registered: false);
        steam.Files.AddDirectory(@"D:\Machine32\steamapps");
        steam.Files.AddDirectory(@"E:\Machine64\steamapps");
        steam.Files.AddDirectory(@"F:\User\steamapps");
        steam.Registry.SetText(RegistryHive.LocalMachine, RegistryView.Registry32, @"SOFTWARE\Valve\Steam", "InstallPath", @"D:\Machine32");
        steam.Registry.SetText(RegistryHive.LocalMachine, RegistryView.Registry64, @"SOFTWARE\Valve\Steam", "InstallPath", @"E:\Machine64");
        steam.Registry.SetText(RegistryHive.CurrentUser, RegistryView.Default, SteamTree.SteamKey, "SteamPath", "f:/user");
        steam.Registry.SetText(RegistryHive.CurrentUser, RegistryView.Default, @"Software\Valve\Other", "SteamPath", @"G:\Ignored");

        var scan = await steam.ScanAsync();

        scan.Libraries.Select(location => location.Id).Should().Equal(@"f:\user", @"D:\Machine32", @"E:\Machine64",
            @"C:\Program Files (x86)\Steam");
    }

    [Fact]
    public async Task Candidates_without_steamapps_are_not_roots()
    {
        var steam = new SteamTree(@"D:\Steam", registered: false);
        steam.Files.AddDirectory(@"C:\Program Files (x86)");
        steam.Registry.SetText(RegistryHive.CurrentUser, RegistryView.Default, SteamTree.SteamKey, "SteamPath", "c:/nothing");

        var scan = await steam.ScanAsync();

        scan.IsPresent.Should().BeFalse();
        scan.Libraries.Should().BeEmpty();
    }

    [Fact]
    public async Task A_steam_whose_own_drive_is_gone_is_not_taken_for_uninstalled()
    {
        var steam = new SteamTree(@"E:\Steam");
        steam.Files.AddDirectory(@"C:\Program Files (x86)");
        var library = steam.AddLibrary(@"D:\Library");
        steam.AddApp(1001, "Example Quest", library: library);
        steam.AddApp(1002, "Root Game");
        var games = (await steam.ScanAsync()).Games;

        steam.Files.Remove(@"E:\");
        var unplugged = await steam.ScanAsync();
        steam.Files.AddDirectory(@"E:\");
        var uninstalled = await steam.ScanAsync();

        games.Should().HaveCount(2);
        unplugged.IsPresent.Should().BeTrue();
        unplugged.IsComplete.Should().BeFalse();
        games.Should().OnlyContain(game => unplugged.Describe(game) != null, "a game kept from an incomplete scan is not gone");
        uninstalled.IsPresent.Should().BeFalse();
        games.Should().OnlyContain(game => uninstalled.Describe(game) == null);
    }

    [Fact]
    public async Task Linux_roots_are_searched_natively_then_in_flatpak_and_snap()
    {
        var home = @"C:\home\player";
        var files = new SteamTree(@"C:\unused", registered: false);
        string[] roots =
        [
            Path.Join(home, "data", "Steam"),
            Path.Join(home, ".steam", "debian-installation"),
            Path.Join(home, ".var", "app", "com.valvesoftware.Steam", ".local", "share", "Steam"),
            Path.Join(home, "snapdata", "steam", "common", ".steam", "root"),
        ];
        foreach (var root in roots)
        {
            files.Files.AddDirectory(Path.Join(root, "steamapps"));
        }

        files.Files.AddDirectoryLink(Path.Join(home, ".steam", "steam"), roots[0]);
        var linux = new LibraryEnvironment
        {
            Platform = AppPlatform.Linux,
            HomeDirectory = home,
            XdgDataHome = Path.Join(home, "data"),
            SnapUserData = Path.Join(home, "snapdata"),
        };

        var scan = await files.CreateProvider(linux).ScanAsync(LibraryScanContext.Empty, Token);

        scan.Libraries.Select(location => location.Id).Should().Equal(roots.Select(PathComparison.Normalize));
        scan.Watch.RegistryKeys.Should().BeEmpty();
    }

    [Fact]
    public async Task The_mac_root_is_under_application_support()
    {
        var steam = new SteamTree(@"C:\unused", registered: false);
        var root = steam.Files.AddDirectory(@"C:\Users\Player\Library\Application Support\Steam\steamapps");
        var mac = SteamTree.Windows with { Platform = AppPlatform.MacOS };

        var scan = await steam.CreateProvider(mac).ScanAsync(LibraryScanContext.Empty, Token);

        scan.Libraries.Select(location => location.Id).Should().Equal(Path.GetDirectoryName(root));
    }

    [Fact]
    public async Task The_watch_plan_follows_the_libraries_and_the_registry()
    {
        _steam.AddLibrary(@"D:\SteamLibrary");
        _steam.AddLibrary(@"E:\Unplugged", create: false);
        _steam.Files.AddDirectory(@"E:\");
        _steam.AddDownload(1001);

        var watch = (await _steam.ScanAsync()).Watch;

        string[] entries = ["appmanifest_*.acf", "downloading", "libraryfolders.vdf"];
        watch.Folders.Should().Equal(
            new WatchedFolder(@"c:\steam\steamapps", entries),
            new WatchedFolder(@"c:\steam\steamapps\downloading", []),
            new WatchedFolder(@"D:\SteamLibrary\steamapps", entries),
            new WatchedFolder(@"E:\", ["Unplugged"]),
            new WatchedFolder(@"c:\steam\config", ["libraryfolders.vdf"]));
        watch.RegistryKeys.Should().Equal(
            new WatchedRegistryKey(RegistryHive.CurrentUser, RegistryView.Default, SteamTree.SteamKey, IncludeSubkeys: false),
            new WatchedRegistryKey(RegistryHive.CurrentUser, RegistryView.Default, $@"{SteamTree.SteamKey}\Apps", IncludeSubkeys: true));
    }

    [Fact]
    public async Task Without_steam_the_places_it_may_be_installed_to_are_watched()
    {
        var steam = new SteamTree(@"D:\Unused", registered: false);
        steam.Files.AddDirectory(@"C:\Program Files (x86)");

        var scan = await steam.ScanAsync();

        scan.IsPresent.Should().BeFalse();
        scan.Watch.Folders.Should().Equal(new WatchedFolder(@"C:\Program Files (x86)", ["Steam"]));
    }

    [Fact]
    public async Task A_folder_the_player_added_is_scanned_as_a_library()
    {
        var steam = new SteamTree(@"D:\Unused", registered: false);
        steam.Files.AddDirectory(@"E:\Games\steamapps");
        steam.AddApp(1001, "Example Quest", library: @"E:\Games");

        var scan = await steam.ScanAsync(@"E:\Games", @"F:\Unplugged");

        scan.IsPresent.Should().BeTrue();
        scan.Libraries.Should().Equal(
            new LibraryLocation(@"E:\Games", SteamLibraryProvider.LibrarySource, @"E:\Games", LibraryState.Online, IsManual: true),
            new LibraryLocation(@"F:\Unplugged", SteamLibraryProvider.LibrarySource, @"F:\Unplugged", LibraryState.Offline, IsManual: true));
        scan.Games.Single().Key.Should().Be(Quest);
    }

    [Fact]
    public async Task A_library_whose_drive_does_not_answer_is_offline_while_the_others_are_read()
    {
        var slow = _steam.AddLibrary(@"D:\Slow");
        var other = _steam.AddLibrary(@"E:\Other");
        _steam.AddApp(1001, "Example Quest", library: slow);
        _steam.AddApp(1002, "Other Quest", library: other);
        using var files = new HangingFileSystem(_steam.Files);
        files.Hang(slow);

        var scan = await ScanRunningOutOfTimeAsync(files);

        scan.Libraries.Select(location => (location.Id, location.State)).Should().Equal(
            (@"c:\steam", LibraryState.Online),
            (@"D:\Slow", LibraryState.Offline),
            (@"E:\Other", LibraryState.Online));
        scan.Games.Select(game => game.Key.StoreGameId).Should().Equal("1002");
        scan.IsComplete.Should().BeFalse("a listed path that did not answer may stand for a library known under another one");
        scan.Watch.Folders.Should().NotContain(folder => PathComparison.IsWithin(slow, folder.Directory));
    }

    [Fact]
    public async Task A_root_whose_drive_does_not_answer_leaves_steam_present_and_the_scan_incomplete()
    {
        _steam.Files.AddDirectory(@"E:\Games\steamapps");
        _steam.AddApp(1001, "Example Quest", library: @"E:\Games");
        using var files = new HangingFileSystem(_steam.Files);
        files.Hang(_steam.Root);

        var scan = await ScanRunningOutOfTimeAsync(files, @"E:\Games");

        scan.IsPresent.Should().BeTrue();
        scan.IsComplete.Should().BeFalse();
        scan.Libraries.Select(location => location.Id).Should().Equal(@"E:\Games");
        scan.Games.Should().ContainSingle();
    }

    [Fact]
    public async Task A_probe_of_a_game_whose_drive_does_not_answer_is_offline()
    {
        var library = _steam.AddLibrary(@"D:\Slow");
        _steam.AddApp(1001, "Example Quest", library: library);
        var known = (await _steam.ScanAsync()).Games.Single();
        using var files = new HangingFileSystem(_steam.Files);
        files.Hang(library);
        var time = new FakeTimeProvider();

        var probing = _steam.CreateProvider(files: files).ProbeAsync(known, LibraryScanContext.Empty with { Access = new LibraryAccess(time) }, Token);
        await files.RunOutOfTimeAsync(time);
        var probed = await probing;

        probed!.State.Should().Be(GameInstallState.Offline);
        probed.Reason.Should().Be(GameStateReason.LibraryOffline);
        probed.LibraryId.Should().Be(known.LibraryId);
    }

    [Fact]
    public async Task A_probe_reads_the_game_again()
    {
        _steam.AddApp(1001, "Example Quest");
        var provider = _steam.CreateProvider();
        var known = (await provider.ScanAsync(LibraryScanContext.Empty, Token)).Games.Single();
        _steam.SetUpdating(1001);

        var probed = await provider.ProbeAsync(known, LibraryScanContext.Empty, Token);

        probed!.State.Should().Be(GameInstallState.Updating);
    }

    [Fact]
    public async Task A_probe_tells_a_download_steam_just_started_from_one_it_left_behind()
    {
        _steam.AddApp(1001, "Example Quest");
        var provider = _steam.CreateProvider();
        var known = (await provider.ScanAsync(LibraryScanContext.Empty, Token)).Games.Single();
        _steam.AddDownload(1001, changedAt: _steam.Time.GetUtcNow());

        var starting = await provider.ProbeAsync(known, LibraryScanContext.Empty, Token);
        _steam.Time.Advance(SteamDownloads.ActiveWindow);
        var leftOver = await provider.ProbeAsync(known, LibraryScanContext.Empty, Token);

        starting!.State.Should().Be(GameInstallState.Updating);
        leftOver!.IsReady.Should().BeTrue();
        leftOver.Reason.Should().Be(GameStateReason.UnfinishedDownload);
    }

    [Fact]
    public async Task A_probe_blocks_writing_when_the_game_cannot_be_read_or_its_library_is_gone()
    {
        _steam.AddApp(1001, "Example Quest");
        var provider = _steam.CreateProvider();
        var known = (await provider.ScanAsync(LibraryScanContext.Empty, Token)).Games.Single();

        _steam.Files.AddFile(_steam.ManifestPath(1001), "\"AppState\" {");
        var unreadable = await provider.ProbeAsync(known, LibraryScanContext.Empty, Token);
        _steam.Files.Remove(Path.Join(_steam.Root, "steamapps"));
        var offline = await provider.ProbeAsync(known, LibraryScanContext.Empty, Token);

        unreadable!.State.Should().Be(GameInstallState.Unknown);
        unreadable.Reason.Should().Be(GameStateReason.UnreadableStoreData);
        offline!.State.Should().Be(GameInstallState.Offline);
        offline.Reason.Should().Be(GameStateReason.LibraryOffline);
    }

    [Fact]
    public async Task A_probe_finds_a_game_moved_to_another_library_and_misses_an_uninstalled_one()
    {
        _steam.AddApp(1001, "Example Quest");
        _steam.AddApp(1002, "Uninstalled Later");
        var provider = _steam.CreateProvider();
        var games = (await provider.ScanAsync(LibraryScanContext.Empty, Token)).Games;
        var library = _steam.AddLibrary(@"D:\SteamLibrary");
        _steam.Files.Remove(_steam.ManifestPath(1001));
        _steam.AddApp(1001, "Example Quest", library: library);
        _steam.Files.Remove(_steam.ManifestPath(1002));

        var moved = await provider.ProbeAsync(games.Single(game => game.Key == Quest), LibraryScanContext.Empty, Token);
        var gone = await provider.ProbeAsync(games.Single(game => game.Key != Quest), LibraryScanContext.Empty, Token);

        moved!.LibraryId.Should().Be(library);
        gone.Should().BeNull();
    }

    [Theory]
    [InlineData(@"D:\SteamLibrary")]
    [InlineData(@"D:\SteamLibrary\steamapps")]
    [InlineData(@"D:\SteamLibrary\steamapps\common")]
    [InlineData(@"D:\SteamLibrary\steamapps\common\Example Quest")]
    public async Task A_library_with_manifests_or_a_folder_inside_it_is_evidence_of_that_library(string picked)
    {
        _steam.Files.AddDirectory(@"D:\SteamLibrary\steamapps");
        _steam.AddApp(1001, "Example Quest", library: @"D:\SteamLibrary");

        var evidence = await _steam.CreateProvider().InspectFolderAsync(picked, Token);

        evidence.Should().Be(LibraryFolderEvidence.Accepted(@"D:\SteamLibrary"));
    }

    [Fact]
    public async Task A_folder_without_manifests_is_no_evidence_and_a_game_outside_a_library_is_named()
    {
        _steam.Files.AddDirectory(@"D:\Empty\steamapps");
        _steam.Files.AddFile(@"D:\Copied\Game\steam_api64.dll", "");

        var provider = _steam.CreateProvider();

        (await provider.InspectFolderAsync(@"D:\Empty", Token)).Should().Be(LibraryFolderEvidence.None);
        (await provider.InspectFolderAsync(@"D:\Copied\Game", Token))
            .Should().Be(LibraryFolderEvidence.Rejected(LibraryFolderRejection.GameOutsideLibrary));
    }

    private async Task<StoreScan> ScanRunningOutOfTimeAsync(HangingFileSystem files, params string[] manualFolders)
    {
        var time = new FakeTimeProvider();
        var scanning = _steam.CreateProvider(files: files)
            .ScanAsync(new LibraryScanContext([.. manualFolders]) { Access = new LibraryAccess(time) }, Token);
        await files.RunOutOfTimeAsync(time);
        return await scanning;
    }
}
