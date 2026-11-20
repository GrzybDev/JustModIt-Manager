using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.Library;
using JustModIt.Manager.Core.Library.Gog;
using JustModIt.Manager.Core.Library.Heroic;
using JustModIt.Manager.Core.Platform;
using JustModIt.Manager.Core.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;

namespace JustModIt.Manager.Core.Tests.Library.Gog;

public sealed class HeroicGogSourceTests
{
    private static readonly string Heroic = HeroicLauncher.ConfigDirectories(TestEnvironments.Host)[0];
    private static readonly string Store = Path.Join(Heroic, "gog_store");
    private static readonly string Installed = Path.Join(Store, "installed.json");
    private static readonly string Queue = Path.Join(Heroic, "store", "download-manager.json");

    private readonly InMemoryFileSystem _files = new();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Installed_games_become_records()
    {
        _files.AddFile(Installed, """
            {
              "installed": [
                { "platform": "windows", "executable": "", "install_path": "D:\\Heroic\\Example", "install_size": "12.34 GiB",
                  "is_dlc": false, "version": "1.2a", "appName": "1100000003", "language": "pl-PL", "buildId": "53210987654321097" },
                { "platform": "osx", "install_path": "/Applications/Example.app", "is_dlc": false, "appName": 1100000001 },
                { "platform": "linux", "install_path": "D:\\Heroic\\Expansion", "is_dlc": true, "appName": "2001" },
                { "platform": "someday-os", "install_path": "D:\\Heroic\\Future", "appName": "1003" },
                { "platform": "windows", "install_path": "D:\\Heroic\\No Id", "appName": "not-a-number" },
                { "platform": "windows", "install_path": " ", "appName": "2002" },
              ]
            }
            """);
        _files.AddFile(Queue, """
            {
              "finished": [ { "params": { "appName": "1003", "runner": "gog" }, "type": "update", "status": "done" } ],
              "queue": [
                { "params": { "appName": "1100000003", "runner": "gog" }, "type": "update" },
                { "params": { "appName": "1100000001", "runner": "legendary" }, "type": "install" }
              ]
            }
            """);

        var scan = await Scan();

        scan.Libraries.Should().Equal(new LibraryLocation(Installed, HeroicLauncher.LibrarySource, Installed, LibraryState.Online));
        scan.Records.Should().Equal(
            new GogInstallRecord("1100000003", @"D:\Heroic\Example", Installed)
            {
                BuildId = "53210987654321097",
                Language = "pl-PL",
                Platform = OperatingSystemKind.Windows,
                Launch = HeroicLink("1100000003"),
                Activity = GameInstallState.Updating,
            },
            new GogInstallRecord("1100000001", "/Applications/Example.app", Installed)
            {
                Platform = OperatingSystemKind.MacOS,
                Launch = HeroicLink("1100000001"),
            },
            new GogInstallRecord("2001", @"D:\Heroic\Expansion", Installed)
            {
                IsDlc = true,
                Platform = OperatingSystemKind.Linux,
                Launch = HeroicLink("2001"),
            },
            new GogInstallRecord("1003", @"D:\Heroic\Future", Installed) { Platform = OperatingSystemKind.Unknown, Launch = HeroicLink("1003") });
    }

    [Fact]
    public async Task Without_installed_json_heroic_is_absent_and_watched_step_by_step()
    {
        var config = _files.AddDirectory(Path.GetDirectoryName(Heroic)!);

        var missing = await Scan();
        _files.AddDirectory(Store);
        var closer = await Scan();

        missing.Libraries.Should().BeEmpty();
        missing.Watch.Folders.Should().Equal(new WatchedFolder(config, ["heroic"]));
        closer.Watch.Folders.Should().Equal(new WatchedFolder(Store, ["installed.json"]), new WatchedFolder(Heroic, ["store"]));
    }

    [Theory]
    [InlineData("gog_store", "installed.json")]
    [InlineData("store", "download-manager.json")]
    public async Task A_damaged_file_marks_the_library_unreadable(string folder, string file)
    {
        _files.AddFile(Installed, """{ "installed": [ { "appName": "1001", "install_path": "D:\\Heroic\\Example" } ] }""");
        _files.AddFile(Path.Join(Heroic, folder, file), """{ "installed": [ { "appName": """);

        var scan = await Scan();

        scan.Libraries.Single().State.Should().Be(LibraryState.Unreadable);
        scan.Records.Should().BeEmpty();
    }

    [Fact]
    public void Heroic_is_looked_for_where_each_platform_keeps_its_configuration()
    {
        var windows = TestEnvironments.Windows;
        var linux = TestEnvironments.Linux;
        var xdg = TestPaths.Rooted('H', "xdg");

        HeroicLauncher.ConfigDirectories(windows).Should().Equal(Path.Join(windows.ApplicationData, "heroic"));
        HeroicLauncher.ConfigDirectories(windows with { Platform = AppPlatform.MacOS })
            .Should().Equal(Path.Join(windows.HomeDirectory, "Library", "Application Support", "heroic"));
        HeroicLauncher.ConfigDirectories(linux).Should().Equal(
            Path.Join(linux.HomeDirectory, ".config", "heroic"),
            Path.Join(linux.HomeDirectory, ".var", "app", "com.heroicgameslauncher.hgl", "config", "heroic"));
        HeroicLauncher.ConfigDirectories(linux with { XdgConfigHome = xdg })[0].Should().Be(Path.Join(xdg, "heroic"));
    }

    [Fact]
    public async Task The_games_of_heroic_from_flatpak_are_found_on_linux()
    {
        var flatpak = HeroicLauncher.ConfigDirectories(TestEnvironments.Linux)[1];
        var folder = TestPaths.Rooted('H', "player", "Games", "Heroic", "Example");
        _files.AddInfo(folder, "1001", "Example");
        _files.AddFile(Path.Join(flatpak, "gog_store", "installed.json"), $$"""
            { "installed": [ { "appName": "1001", "install_path": "{{TestPaths.Escaped(folder)}}", "platform": "windows" } ] }
            """);

        var scan = await GogLibraryProvider.Create(_files, new InMemoryRegistry(), new InMemorySqlite(), TestEnvironments.Linux,
            NullLogger<GogLibraryProvider>.Instance).ScanAsync(LibraryScanContext.Empty, Token);

        scan.Games.Select(game => (game.Key.StoreGameId, game.InstallDirectory, game.Platform, game.Launch))
            .Should().Equal(("1001", folder, OperatingSystemKind.Windows, HeroicLink("1001")));
    }

    [Fact]
    public async Task A_game_known_to_the_registry_and_heroic_is_reported_once_and_heroic_dlc_is_hidden()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Only Windows has GOG's registry records.");
        var registry = new InMemoryRegistry()
            .SetText(RegistryHive.LocalMachine, RegistryView.Registry32, @"SOFTWARE\GOG.com\Games\1100000001", "path", @"D:\Games\Example");
        _files.AddInfo(@"D:\Games\Example", "1100000001", "Example");
        _files.AddInfo(@"D:\Heroic\Other", "1001", "Other");
        _files.AddFile(Installed, """
            { "installed": [
                { "appName": "1100000001", "install_path": "D:\\Games\\Example", "is_dlc": false },
                { "appName": "1001", "install_path": "D:\\Heroic\\Other", "is_dlc": false, "platform": "linux" },
                { "appName": "1002", "install_path": "D:\\Heroic\\Other", "is_dlc": true }
            ] }
            """);

        var scan = await Provider(registry).ScanAsync(LibraryScanContext.Empty, Token);

        scan.Games.Select(game => (game.Key.StoreGameId, game.Platform)).Should().BeEquivalentTo([
            ("1100000001", OperatingSystemKind.Windows),
            ("1001", OperatingSystemKind.Linux),
        ]);
        scan.Libraries.Select(library => library.Source).Should().Equal(GogRegistrySource.LibrarySource, HeroicLauncher.LibrarySource);
    }

    [Fact]
    public async Task A_damaged_heroic_file_does_not_hide_the_registry_games()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Only Windows has GOG's registry records.");
        var registry = new InMemoryRegistry()
            .SetText(RegistryHive.LocalMachine, RegistryView.Registry32, @"SOFTWARE\GOG.com\Games\1100000001", "path", @"D:\Games\Example");
        _files.AddInfo(@"D:\Games\Example", "1100000001", "Example");
        _files.AddFile(Installed, "{");

        var scan = await Provider(registry).ScanAsync(LibraryScanContext.Empty, Token);

        scan.IsComplete.Should().BeTrue();
        scan.Games.Select(game => game.Key).Should().Equal(new GameKey(StoreId.Gog, "1100000001"));
        scan.Libraries.Single(library => library.Source == HeroicLauncher.LibrarySource).State.Should().Be(LibraryState.Unreadable);
    }

    private static LaunchLink? HeroicLink(string gameId) => LaunchLink.TryCreate($"heroic://launch?appName={gameId}&runner=gog");

    private Task<SourceScan<GogInstallRecord>> Scan() => new HeroicGogSource(_files, Heroic).ScanAsync(LibraryScanContext.Empty, Token);

    private GogLibraryProvider Provider(InMemoryRegistry registry) =>
        GogLibraryProvider.Create(_files, registry, new InMemorySqlite(), TestEnvironments.Windows, NullLogger<GogLibraryProvider>.Instance);
}
