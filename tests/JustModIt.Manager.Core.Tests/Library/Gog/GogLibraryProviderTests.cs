using JustModIt.Manager.Core.Collections;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.Library;
using JustModIt.Manager.Core.Library.Gog;
using JustModIt.Manager.Core.Platform;
using JustModIt.Manager.Core.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;

namespace JustModIt.Manager.Core.Tests.Library.Gog;

public sealed class GogLibraryProviderTests
{
    private const string GamesKey = @"SOFTWARE\GOG.com\Games";
    private const string Folder = @"D:\GOG Games\Example";

    private static readonly GameKey Example = new(StoreId.Gog, "1207658924");

    private readonly InMemoryFileSystem _files = new();
    private readonly InMemoryRegistry _registry = new();

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
        });
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
    public async Task A_cancelled_scan_stops()
    {
        AddRecord("1207658924", Folder);

        var scanning = CreateProvider().ScanAsync(LibraryScanContext.Empty, new CancellationToken(canceled: true));

        await FluentActions.Awaiting(() => scanning).Should().ThrowAsync<OperationCanceledException>();
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

    private GogLibraryProvider CreateProvider(params IGogInstallSource[] extra) =>
        new(_files, [new GogRegistrySource(_registry), .. extra], AppPlatform.Windows, NullLogger<GogLibraryProvider>.Instance);

    private Task<StoreScan> Scan() => CreateProvider().ScanAsync(LibraryScanContext.Empty, Token);

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

    private sealed class ThrowingSource : IGogInstallSource
    {
        public Task<GogSourceScan> ScanAsync(LibraryScanContext context, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The launcher's data is unreadable.");
    }
}
