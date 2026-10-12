using JustModIt.Manager.Core.Collections;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.Library;
using JustModIt.Manager.Core.Library.Gog;
using JustModIt.Manager.Core.Library.Heroic;
using JustModIt.Manager.Core.Platform;
using JustModIt.Manager.Core.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace JustModIt.Manager.Core.Tests.Library.Gog;

public sealed class GogLibraryProviderTests
{
    private const string GamesKey = @"SOFTWARE\GOG.com\Games";
    private const string Folder = @"D:\GOG Games\Example";
    private const string GalaxyStorage = @"C:\ProgramData\GOG.com\Galaxy\storage";
    private const string GalaxyLibrary = GalaxyStorage + @"\galaxy-2.0.db";
    private const string GalaxyJobs = GalaxyStorage + @"\jobs.db";

    private static readonly GameKey Example = new(StoreId.Gog, "1207658924");

    private readonly InMemoryFileSystem _files = new();
    private readonly InMemoryRegistry _registry = new();
    private readonly InMemorySqlite _sqlite = new();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_info_file_names_and_dates_the_game_and_beats_the_registry()
    {
        AddRecord("1207658924", Folder, name: "Registry Name", buildId: "900", language: "german");
        _files.AddInfo(Folder, "1207658924", "Proper Name", buildId: "900", languages: "\"pl-PL\"");

        var scan = await Scan();

        scan.IsPresent.Should().BeTrue();
        scan.IsComplete.Should().BeTrue();
        scan.Games.Should().Equal(new InstalledGame
        {
            Key = Example,
            Name = "Proper Name",
            InstallDirectory = Folder,
            LibraryId = GogRegistrySource.LibrarySource,
            BuildId = "900",
            State = GameInstallState.Installed,
            StoreActivityAt = GogFiles.WrittenAt,
            Platform = OperatingSystemKind.Windows,
            Languages = ["pl-PL", "de"],
            Launch = LaunchProgram.TryCreate("game.exe", null, null),
        });
    }

    [Fact]
    public async Task Each_game_starts_through_what_installed_it()
    {
        InstallGalaxy();
        _sqlite.Add(GalaxyLibrary, ["1207658924", Folder], ["1003", @"D:\GOG Galaxy\Moved Since"]);
        _files.AddInfo(Folder, "1207658924", "Through Galaxy");
        AddRecord("1207658924", Folder);
        _files.AddInfo(@"D:\GOG Games\Offline", "1003", "Through The Offline Installer");
        AddRecord("1003", @"D:\GOG Games\Offline");
        _files.AddInfo(@"D:\Heroic\Another", "1001", "Through Heroic");
        _files.AddFile(@"R:\Roaming\heroic\gog_store\installed.json",
            """{ "installed": [ { "appName": "1001", "install_path": "D:\\Heroic\\Another", "platform": "windows" } ] }""");
        _files.AddInfo(@"E:\Offline Installs\Third", "1002", "From Its Folder");

        var scan = await CreateProvider(new HeroicGogSource(_files, @"R:\Roaming\heroic"))
            .ScanAsync(Manual(@"E:\Offline Installs"), Token);

        scan.Games.ToDictionary(game => game.Name, game => game.Launch).Should().BeEquivalentTo(new Dictionary<string, GameLaunch?>
        {
            ["Through Galaxy"] = LaunchLink.TryCreate("goggalaxy://openGameView/1207658924"),
            ["Through The Offline Installer"] = LaunchProgram.TryCreate("game.exe", null, null),
            ["Through Heroic"] = LaunchLink.TryCreate("heroic://launch?appName=1001&runner=gog"),
            ["From Its Folder"] = LaunchProgram.TryCreate("game.exe", null, null),
        });
        scan.Libraries.Select(library => library.Source).Should().Contain([GogRegistrySource.GalaxySource, HeroicLauncher.LibrarySource],
            "the libraries name the clients there are to open");
    }

    [Theory]
    [InlineData("install", GameInstallState.Installing)]
    [InlineData("update", GameInstallState.Updating)]
    [InlineData("verify", GameInstallState.Validating)]
    public async Task A_game_galaxy_works_on_is_busy_until_its_job_is_gone(string job, GameInstallState state)
    {
        InstallGalaxy();
        _sqlite.Add(GalaxyLibrary, ["1207658924", Folder]).Add(GalaxyJobs, ["1207658924", job]);
        _files.AddInfo(Folder, "1207658924", "Example");
        AddRecord("1207658924", Folder);
        var provider = CreateProvider();

        var scan = await provider.ScanAsync(LibraryScanContext.Empty, Token);
        _sqlite.Add(GalaxyJobs);
        var probed = await provider.ProbeAsync(scan.Games.Single(), LibraryScanContext.Empty, Token);

        scan.Games.Single().Should().Match<InstalledGame>(game => game.State == state && game.Reason == GameStateReason.StoreBusy);
        probed!.IsReady.Should().BeTrue();
        scan.Watch.Folders.Should().Contain(new WatchedFolder(GalaxyStorage,
            ["galaxy-2.0.db", "galaxy-2.0.db-wal", "galaxy-2.0.db-journal", "jobs.db", "jobs.db-wal", "jobs.db-journal"]));
    }

    [Fact]
    public async Task A_busy_galaxy_database_leaves_the_registry_games_as_they_were_until_it_can_be_read()
    {
        InstallGalaxy();
        _sqlite.Lock(GalaxyLibrary);
        _files.AddInfo(Folder, "1207658924", "Example");
        AddRecord("1207658924", Folder);

        var scan = await Scan();

        scan.Games.Should().BeEmpty();
        scan.Libraries.Should().Equal(new LibraryLocation(GogRegistrySource.LibrarySource, GogRegistrySource.GalaxySource, null,
            LibraryState.Unreadable));
    }

    [Fact]
    public async Task A_stale_record_of_a_deleted_folder_does_not_hide_the_real_install()
    {
        AddRecord("1207658924", @"D:\Old Location", view: RegistryView.Registry32);
        AddRecord("1207658924", Folder, view: RegistryView.Registry64);
        _files.AddInfo(Folder, "1207658924", "Example");

        var game = (await Scan()).Games.Single();

        game.InstallDirectory.Should().Be(Folder);
        game.IsReady.Should().BeTrue();
    }

    [Fact]
    public async Task A_missing_folder_is_broken_and_a_missing_drive_is_offline()
    {
        _files.AddDirectory(@"D:\");
        AddRecord("1001", @"D:\Removed", name: "Registry Only");
        AddRecord("1002", @"Q:\Unplugged\Game", name: "On Another Drive");

        var games = (await Scan()).Games.ToDictionary(game => game.Name);

        games["Registry Only"].Should().Match<InstalledGame>(game =>
            game.State == GameInstallState.Broken && game.Reason == GameStateReason.InstallDirectoryMissing);
        games["On Another Drive"].Should().Match<InstalledGame>(game =>
            game.State == GameInstallState.Offline && game.Reason == GameStateReason.LibraryOffline);
    }

    [Fact]
    public async Task A_build_mismatch_means_an_update_even_next_to_an_uninstall_entry()
    {
        AddRecord("1207658924", Folder, buildId: "901");
        AddUninstall("1207658924", Folder);
        _files.AddInfo(Folder, "1207658924", "Example", buildId: "900");

        var game = (await Scan()).Games.Single();

        game.State.Should().Be(GameInstallState.Updating);
        game.Reason.Should().Be(GameStateReason.BuildMismatch);
        game.BuildId.Should().Be("900");
    }

    [Fact]
    public async Task A_folder_without_an_info_file_is_installed_with_a_doubt()
    {
        _files.AddDirectory(Folder);
        AddRecord("1207658924", Folder, name: "Very Old Installer");

        var game = (await Scan()).Games.Single();

        game.State.Should().Be(GameInstallState.Installed);
        game.Reason.Should().Be(GameStateReason.InfoFileMissing);
        game.IsReady.Should().BeTrue();
        game.Name.Should().Be("Very Old Installer");
    }

    [Fact]
    public async Task Dlc_is_hidden_however_it_is_recorded()
    {
        _files.AddInfo(Folder, "1207658924", "Example");
        _files.AddInfo(Folder, "2001", "Example: Expansion", rootGameId: "1207658924", playTasks: false);
        AddRecord("1207658924", Folder);
        AddRecord("2001", Folder);
        AddRecord("2002", Folder, dependsOn: "1207658924");
        AddRecord("2003", Folder);

        var games = (await Scan()).Games;

        games.Select(game => game.Key).Should().Equal(Example);
    }

    [Fact]
    public async Task A_record_under_a_pack_id_still_reports_the_game_in_its_folder()
    {
        _files.AddInfo(Folder, "1207658924", "Example");
        AddRecord("1400000000", Folder, name: "Example Complete Edition", buildId: "999");

        var game = (await Scan()).Games.Single();

        game.Key.Should().Be(Example);
        game.Name.Should().Be("Example");
        game.State.Should().Be(GameInstallState.Installed);
    }

    [Theory]
    [InlineData(".gogdl-resume", true, GameInstallState.Updating)]
    [InlineData(".gogdl-download-cache", false, GameInstallState.Installing)]
    public async Task Download_markers_mean_the_launcher_is_writing(string marker, bool hasInfo, GameInstallState state)
    {
        _files.AddFile(Path.Join(Folder, marker), "");
        if (hasInfo)
        {
            _files.AddInfo(Folder, "1207658924", "Example");
        }

        AddRecord("1207658924", Folder);

        var game = (await Scan()).Games.Single();

        game.State.Should().Be(state);
        game.Reason.Should().Be(GameStateReason.DownloadInProgress);
    }

    [Fact]
    public async Task A_failing_source_leaves_the_others_and_marks_the_scan_incomplete()
    {
        _files.AddInfo(Folder, "1207658924", "Example");
        AddRecord("1207658924", Folder);

        var scan = await CreateProvider(new ThrowingSource()).ScanAsync(LibraryScanContext.Empty, Token);

        scan.IsComplete.Should().BeFalse();
        scan.Games.Select(game => game.Key).Should().Equal(Example);
    }

    [Fact]
    public async Task The_registry_and_every_game_folder_are_watched()
    {
        _files.AddInfo(Folder, "1207658924", "Example");
        AddRecord("1207658924", Folder);
        AddRecord("1001", @"D:\Removed");

        var watch = (await Scan()).Watch;

        watch.RegistryKeys.Should().HaveCount(3).And.OnlyContain(key => key.Path == GamesKey && key.IncludeSubkeys);
        watch.Folders.Should().Equal(new WatchedFolder(Folder, ["goggame-*.info", ".gogdl-resume", ".gogdl-download-cache"]));
    }

    [Fact]
    public async Task Without_any_source_gog_is_absent()
    {
        var scan = await Scan();

        scan.IsPresent.Should().BeFalse();
        scan.Games.Should().BeEmpty();
    }

    [Fact]
    public async Task Folders_the_player_added_hold_a_game_or_a_folder_of_games()
    {
        _files.AddInfo(@"E:\Offline Installs\Example", "1207658924", "Example");
        _files.AddInfo(@"E:\Offline Installs\Another", "1001", "Another");
        _files.AddDirectory(@"E:\Offline Installs\Not A Game");
        _files.AddInfo(@"F:\Single", "1002", "Single");

        var scan = await CreateProvider().ScanAsync(Manual(@"E:\Offline Installs", @"F:\Single", @"Q:\Unplugged"), Token);

        scan.IsPresent.Should().BeTrue();
        scan.Libraries.Select(library => (library.Id, library.State, library.IsManual)).Should().Equal(
            (@"E:\Offline Installs", LibraryState.Online, true),
            (@"F:\Single", LibraryState.Online, true),
            (@"Q:\Unplugged", LibraryState.Offline, true));
        scan.Games.Select(game => (game.Key.StoreGameId, game.LibraryId, game.InstallDirectory)).Should().BeEquivalentTo([
            ("1001", @"E:\Offline Installs", @"E:\Offline Installs\Another"),
            ("1207658924", @"E:\Offline Installs", @"E:\Offline Installs\Example"),
            ("1002", @"F:\Single", @"F:\Single"),
        ]);
    }

    [Fact]
    public async Task Games_whose_drive_does_not_answer_are_reported_while_the_others_are_read()
    {
        _files.AddInfo(Folder, "1207658924", "Example");
        AddRecord("1207658924", Folder);
        _files.AddInfo(@"Q:\Slow\Game", "1001", "Slow Game");
        AddRecord("1001", @"Q:\Slow\Game");
        _files.AddInfo(@"Q:\Other", "1002", "Other Game");
        AddRecord("1002", @"Q:\Other");
        using var files = new HangingFileSystem(_files);
        files.Hang(@"Q:\");

        var scan = await ScanRunningOutOfTimeAsync(files, LibraryScanContext.Empty);

        scan.Games.Select(game => game.Key).Should().Equal(Example);
        scan.UnansweredGames.Should().BeEquivalentTo([
            new UnansweredGame(new GameKey(StoreId.Gog, "1001"), @"Q:\Slow\Game"),
            new UnansweredGame(new GameKey(StoreId.Gog, "1002"), @"Q:\Other"),
        ]);
        files.HungCalls.Should().Be(1, "a drive that does not answer holds one thread");
        scan.Libraries.Single().UnreadableEntries.Should().Be(2, "a game on a drive that did not answer is not gone");
        scan.IsComplete.Should().BeTrue();
        scan.Watch.Folders.Should().Equal(new WatchedFolder(Folder, ["goggame-*.info", ".gogdl-resume", ".gogdl-download-cache"]));
    }

    [Fact]
    public async Task A_folder_the_player_added_that_does_not_answer_is_offline()
    {
        _files.AddInfo(@"E:\Offline Installs\Example", "1207658924", "Example");
        _files.AddInfo(@"Q:\Slow\Another", "1001", "Another");
        using var files = new HangingFileSystem(_files);
        files.Hang(@"Q:\Slow");

        var scan = await ScanRunningOutOfTimeAsync(files, Manual(@"E:\Offline Installs", @"Q:\Slow"));

        scan.Libraries.Select(library => (library.Id, library.State, library.IsManual)).Should().Equal(
            (@"E:\Offline Installs", LibraryState.Online, true),
            (@"Q:\Slow", LibraryState.Offline, true));
        scan.Games.Select(game => game.Key).Should().Equal(Example);
    }

    [Fact]
    public async Task A_launcher_list_that_does_not_answer_leaves_the_scan_incomplete()
    {
        _files.AddInfo(Folder, "1207658924", "Example");
        AddRecord("1207658924", Folder);
        using var files = new HangingFileSystem(_files);
        files.Hang(@"R:\Roaming");

        var scan = await ScanRunningOutOfTimeAsync(files, LibraryScanContext.Empty, new HeroicGogSource(files, @"R:\Roaming\heroic"));

        scan.IsComplete.Should().BeFalse();
        scan.Games.Select(game => game.Key).Should().Equal(Example);
    }

    [Fact]
    public async Task A_probe_of_a_game_whose_folder_does_not_answer_is_offline()
    {
        _files.AddInfo(Folder, "1207658924", "Example");
        AddRecord("1207658924", Folder);
        var known = (await Scan()).Games.Single();
        using var files = new HangingFileSystem(_files);
        files.Hang(Folder);
        var time = new FakeTimeProvider();

        var probing = CreateProvider(files).ProbeAsync(known, LibraryScanContext.Empty with { Access = new LibraryAccess(time) }, Token);
        await files.RunOutOfTimeAsync(time);

        (await probing).Should().Be(known with { State = GameInstallState.Offline, Reason = GameStateReason.LibraryOffline });
    }

    [Fact]
    public async Task A_probe_reads_the_records_again()
    {
        _files.AddInfo(Folder, "1207658924", "Example", buildId: "900");
        AddRecord("1207658924", Folder, buildId: "900");
        var provider = CreateProvider();
        var known = (await provider.ScanAsync(LibraryScanContext.Empty, Token)).Games.Single();
        _files.AddFile(Path.Join(Folder, ".gogdl-resume"), "");

        var probed = await provider.ProbeAsync(known, LibraryScanContext.Empty, Token);
        var gone = await provider.ProbeAsync(known with { Key = new GameKey(StoreId.Gog, "1") }, LibraryScanContext.Empty, Token);

        probed!.State.Should().Be(GameInstallState.Updating);
        gone.Should().BeNull();
    }

    [Fact]
    public async Task A_folder_with_a_game_or_games_is_evidence_and_one_with_only_dlc_is_not()
    {
        _files.AddInfo(@"E:\Offline Installs\Example", "1207658924", "Example");
        _files.AddInfo(@"F:\Expansion", "2001", "Example: Expansion", rootGameId: "1207658924", playTasks: false);
        _files.AddDirectory(@"G:\Empty");
        var provider = CreateProvider();

        (await provider.InspectFolderAsync(@"E:\Offline Installs\Example", Token)).Should().Be(
            LibraryFolderEvidence.Accepted(@"E:\Offline Installs\Example"));
        (await provider.InspectFolderAsync(@"E:\Offline Installs", Token)).Should().Be(LibraryFolderEvidence.Accepted(@"E:\Offline Installs"));
        (await provider.InspectFolderAsync(@"F:\Expansion", Token)).Should().Be(LibraryFolderEvidence.Rejected(LibraryFolderRejection.DlcOnly));
        (await provider.InspectFolderAsync(@"G:\Empty", Token)).Should().Be(LibraryFolderEvidence.None);
    }

    private static LibraryScanContext Manual(params string[] folders) => new(new ValueList<string>([.. folders]));

    private GogLibraryProvider CreateProvider(params IInstallSource<GogInstallRecord>[] extra) => CreateProvider(_files, extra);

    private GogLibraryProvider CreateProvider(IFileSystem files, params IInstallSource<GogInstallRecord>[] extra) =>
        new(files, [new GogRegistrySource(_registry, new GalaxyDatabase(files, _sqlite, GalaxyStorage)), .. extra], AppPlatform.Windows,
            NullLogger<GogLibraryProvider>.Instance);

    private void InstallGalaxy()
    {
        _registry.SetText(RegistryHive.LocalMachine, RegistryView.Registry32, @"SOFTWARE\GOG.com\GalaxyClient", "clientExecutable",
            "GalaxyClient.exe");
        _files.AddDirectory(GalaxyStorage);
    }

    private Task<StoreScan> Scan() => CreateProvider().ScanAsync(LibraryScanContext.Empty, Token);

    private async Task<StoreScan> ScanRunningOutOfTimeAsync(HangingFileSystem files, LibraryScanContext context,
        params IInstallSource<GogInstallRecord>[] extra)
    {
        var time = new FakeTimeProvider();
        var scanning = CreateProvider(files, extra).ScanAsync(context with { Access = new LibraryAccess(time) }, Token);
        await files.RunOutOfTimeAsync(time);
        return await scanning;
    }

    private void AddRecord(string id, string path, string? name = null, string? buildId = null, string? language = null,
        string? dependsOn = null, RegistryView view = RegistryView.Registry32)
    {
        var key = $@"{GamesKey}\{id}";
        _registry.SetText(RegistryHive.LocalMachine, view, key, "path", path);
        foreach (var (valueName, value) in (ReadOnlySpan<(string, string?)>)[("gameName", name), ("buildId", buildId), ("language", language), ("dependsOn", dependsOn)])
        {
            if (value is not null)
            {
                _registry.SetText(RegistryHive.LocalMachine, view, key, valueName, value);
            }
        }
    }

    private void AddUninstall(string id, string path) =>
        _registry
            .SetText(RegistryHive.LocalMachine, RegistryView.Registry32,
                $@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{id}_is1", "Publisher", "GOG.com")
            .SetText(RegistryHive.LocalMachine, RegistryView.Registry32,
                $@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{id}_is1", "InstallLocation", path);

    private sealed class ThrowingSource : IInstallSource<GogInstallRecord>
    {
        public Task<SourceScan<GogInstallRecord>> ScanAsync(LibraryScanContext context, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The launcher's data is unreadable.");
    }
}
