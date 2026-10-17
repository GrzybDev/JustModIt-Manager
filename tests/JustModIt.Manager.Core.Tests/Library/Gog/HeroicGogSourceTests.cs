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
    private const string Heroic = @"C:\Users\Player\AppData\Roaming\heroic";
    private const string Store = Heroic + @"\gog_store";
    private const string Installed = Store + @"\installed.json";
    private const string Queue = Heroic + @"\store\download-manager.json";

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
        _files.AddDirectory(@"C:\Users\Player\AppData\Roaming");

        var missing = await Scan();
        _files.AddDirectory(Store);
        var closer = await Scan();

        missing.Libraries.Should().BeEmpty();
        missing.Watch.Folders.Should().Equal(new WatchedFolder(@"C:\Users\Player\AppData\Roaming", ["heroic"]));
        closer.Watch.Folders.Should().Equal(new WatchedFolder(Store, ["installed.json"]), new WatchedFolder(Heroic, ["store"]));
    }

    [Theory]
    [InlineData(Installed)]
    [InlineData(Queue)]
    public async Task A_damaged_file_marks_the_library_unreadable(string damaged)
    {
        _files.AddFile(Installed, """{ "installed": [ { "appName": "1001", "install_path": "D:\\Heroic\\Example" } ] }""");
        _files.AddFile(damaged, """{ "installed": [ { "appName": """);

        var scan = await Scan();

        scan.Libraries.Single().State.Should().Be(LibraryState.Unreadable);
        scan.Records.Should().BeEmpty();
    }

    [Fact]
    public void Heroic_is_looked_for_where_each_platform_keeps_its_configuration()
    {
        var home = @"C:\home\player";
        LibraryEnvironment Environment(AppPlatform platform, string? xdgConfig = null) => new()
        {
            Platform = platform,
            HomeDirectory = home,
            ApplicationData = @"C:\Users\Player\AppData\Roaming",
            XdgConfigHome = xdgConfig,
        };

        HeroicLauncher.ConfigDirectories(Environment(AppPlatform.Windows)).Should().Equal(Heroic);
        HeroicLauncher.ConfigDirectories(Environment(AppPlatform.MacOS))
            .Should().Equal(Path.Join(home, "Library", "Application Support", "heroic"));
        HeroicLauncher.ConfigDirectories(Environment(AppPlatform.Linux)).Should().Equal(
            Path.Join(home, ".config", "heroic"),
            Path.Join(home, ".var", "app", "com.heroicgameslauncher.hgl", "config", "heroic"));
        HeroicLauncher.ConfigDirectories(Environment(AppPlatform.Linux, xdgConfig: @"C:\xdg"))[0]
            .Should().Be(Path.Join(@"C:\xdg", "heroic"));
    }

    [Fact]
    public async Task A_game_known_to_the_registry_and_heroic_is_reported_once_and_heroic_dlc_is_hidden()
    {
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
        GogLibraryProvider.Create(_files, registry, new InMemorySqlite(), new LibraryEnvironment
        {
            Platform = AppPlatform.Windows,
            HomeDirectory = @"C:\Users\Player",
            ApplicationData = @"C:\Users\Player\AppData\Roaming",
        }, NullLogger<GogLibraryProvider>.Instance);
}
