using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.Library;
using JustModIt.Manager.Core.Library.Gog;
using JustModIt.Manager.Core.Platform;
using JustModIt.Manager.Core.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;

namespace JustModIt.Manager.Core.Tests.Library.Gog;

public sealed class HeroicGogSourceTests
{
    private const string Store = @"C:\Users\Player\AppData\Roaming\heroic\gog_store";
    private const string Installed = Store + @"\installed.json";

    private readonly InMemoryFileSystem _files = new();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Installed_games_become_records()
    {
        _files.AddFile(Installed, """
            {
              "installed": [
                { "platform": "windows", "executable": "", "install_path": "D:\\Heroic\\Example", "install_size": "43.12 GiB",
                  "is_dlc": false, "version": "4.04a", "appName": "1495134320", "language": "pl-PL", "buildId": "58136404580296017" },
                { "platform": "osx", "install_path": "/Applications/Example.app", "is_dlc": false, "appName": 1207658924 },
                { "platform": "linux", "install_path": "D:\\Heroic\\Expansion", "is_dlc": true, "appName": "2001" },
                { "platform": "someday-os", "install_path": "D:\\Heroic\\Future", "appName": "1003" },
                { "platform": "windows", "install_path": "D:\\Heroic\\No Id", "appName": "not-a-number" },
                { "platform": "windows", "install_path": " ", "appName": "2002" },
              ]
            }
            """);

        var scan = await Scan();

        scan.Libraries.Should().Equal(new LibraryLocation(Installed, HeroicGogSource.LibrarySource, Installed, LibraryState.Online));
        scan.Records.Should().Equal(
            new GogInstallRecord("1495134320", @"D:\Heroic\Example", Installed)
            {
                BuildId = "58136404580296017",
                Language = "pl-PL",
                Platform = OperatingSystemKind.Windows,
                Launch = HeroicLink("1495134320"),
            },
            new GogInstallRecord("1207658924", "/Applications/Example.app", Installed)
            {
                Platform = OperatingSystemKind.MacOS,
                Launch = HeroicLink("1207658924"),
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
        closer.Watch.Folders.Should().Equal(new WatchedFolder(Store, ["installed.json"]));
    }

    [Fact]
    public async Task A_damaged_file_marks_the_library_unreadable()
    {
        _files.AddFile(Installed, """{ "installed": [ { "appName": """);

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

        HeroicGogSource.StoreDirectories(Environment(AppPlatform.Windows)).Should().Equal(Store);
        HeroicGogSource.StoreDirectories(Environment(AppPlatform.MacOS))
            .Should().Equal(Path.Join(home, "Library", "Application Support", "heroic", "gog_store"));
        HeroicGogSource.StoreDirectories(Environment(AppPlatform.Linux)).Should().Equal(
            Path.Join(home, ".config", "heroic", "gog_store"),
            Path.Join(home, ".var", "app", "com.heroicgameslauncher.hgl", "config", "heroic", "gog_store"));
        HeroicGogSource.StoreDirectories(Environment(AppPlatform.Linux, xdgConfig: @"C:\xdg"))[0]
            .Should().Be(Path.Join(@"C:\xdg", "heroic", "gog_store"));
    }

    [Fact]
    public async Task A_game_known_to_the_registry_and_heroic_is_reported_once_and_heroic_dlc_is_hidden()
    {
        var registry = new InMemoryRegistry()
            .SetText(RegistryHive.LocalMachine, RegistryView.Registry32, @"SOFTWARE\GOG.com\Games\1207658924", "path", @"D:\Games\Example");
        _files.AddInfo(@"D:\Games\Example", "1207658924", "Example");
        _files.AddInfo(@"D:\Heroic\Other", "1001", "Other");
        _files.AddFile(Installed, """
            { "installed": [
                { "appName": "1207658924", "install_path": "D:\\Games\\Example", "is_dlc": false },
                { "appName": "1001", "install_path": "D:\\Heroic\\Other", "is_dlc": false, "platform": "linux" },
                { "appName": "1002", "install_path": "D:\\Heroic\\Other", "is_dlc": true }
            ] }
            """);

        var scan = await Provider(registry).ScanAsync(LibraryScanContext.Empty, Token);

        scan.Games.Select(game => (game.Key.StoreGameId, game.Platform)).Should().BeEquivalentTo([
            ("1207658924", OperatingSystemKind.Windows),
            ("1001", OperatingSystemKind.Linux),
        ]);
        scan.Libraries.Select(library => library.Source).Should().Equal(GogRegistrySource.LibrarySource, HeroicGogSource.LibrarySource);
    }

    [Fact]
    public async Task A_damaged_heroic_file_does_not_hide_the_registry_games()
    {
        var registry = new InMemoryRegistry()
            .SetText(RegistryHive.LocalMachine, RegistryView.Registry32, @"SOFTWARE\GOG.com\Games\1207658924", "path", @"D:\Games\Example");
        _files.AddInfo(@"D:\Games\Example", "1207658924", "Example");
        _files.AddFile(Installed, "{");

        var scan = await Provider(registry).ScanAsync(LibraryScanContext.Empty, Token);

        scan.IsComplete.Should().BeTrue();
        scan.Games.Select(game => game.Key).Should().Equal(new GameKey(StoreId.Gog, "1207658924"));
        scan.Libraries.Single(library => library.Source == HeroicGogSource.LibrarySource).State.Should().Be(LibraryState.Unreadable);
    }

    private static LaunchLink? HeroicLink(string gameId) => LaunchLink.TryCreate($"heroic://launch?appName={gameId}&runner=gog");

    private Task<GogSourceScan> Scan() => new HeroicGogSource(_files, Store).ScanAsync(LibraryScanContext.Empty, Token);

    private GogLibraryProvider Provider(InMemoryRegistry registry) =>
        GogLibraryProvider.Create(_files, registry, new LibraryEnvironment
        {
            Platform = AppPlatform.Windows,
            HomeDirectory = @"C:\Users\Player",
            ApplicationData = @"C:\Users\Player\AppData\Roaming",
        }, NullLogger<GogLibraryProvider>.Instance);
}
