using JustModIt.Manager.Core.Collections;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.Library;
using JustModIt.Manager.Core.Library.Epic;
using JustModIt.Manager.Core.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;

namespace JustModIt.Manager.Core.Tests.Library.Epic;

public sealed class EpicLibraryProviderTests
{
    private const string ProgramData = @"C:\ProgramData";
    private const string Manifests = @"C:\ProgramData\Epic\EpicGamesLauncher\Data\Manifests";
    private const string Pending = Manifests + @"\Pending";
    private const string InstalledDat = @"C:\ProgramData\Epic\UnrealEngineLauncher\LauncherInstalled.dat";
    private const string Heroic = @"C:\Users\Player\AppData\Roaming\heroic";
    private const string HeroicLegendary = Heroic + @"\legendaryConfig\legendary";
    private const string Legendary = @"C:\Users\Player\.config\legendary";
    private const string Folder = @"D:\Epic Games\Example";
    private const string Namespace = "0a1b2c3d4e5f60718293a4b5c6d7e8f9";
    private const string ItemId = "11112222333344445555666677778888";
    private const string Guid = "4FBDA9F39E21FE6564A51AAB980BCE51";

    private static readonly DateTimeOffset WrittenAt = new(2026, 3, 1, 9, 0, 0, TimeSpan.Zero);
    private static readonly GameKey Example = new(StoreId.Epic, Namespace);

    private readonly InMemoryFileSystem _files = new();
    private readonly InMemoryRegistry _registry = new();
    private readonly Dictionary<string, List<string>> _legendaryEntries = [];

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_launchers_games_are_found_and_its_engine_plugins_and_dlc_are_not()
    {
        AddItem(Guid, "Example App", Namespace, Folder, name: "Example Game", version: "1.2.3");
        AddItem("A0000000000000000000000000000002", "ExampleExpansion", Namespace, Folder, categories: "\"games\", \"addons\"");
        AddItem("A0000000000000000000000000000003", "ExampleStandalone", "fedcba98765432100123456789abcdef", @"D:\Epic Games\Standalone",
            categories: "\"games\", \"addons\", \"addons/launchable\"");
        AddItem("A0000000000000000000000000000004", "UE_9.9", "ue", @"C:\Program Files\Epic Games\UE_9.9",
            categories: "\"engines/ue5\", \"engines\"");
        AddItem("A0000000000000000000000000000005", "ExamplePlugin_9.9", "ue", @"C:\Program Files\Epic Games\UE_9.9",
            categories: "\"plugins/engine\", \"plugins\"", compatibleApps: "\"UE_9.9\"", hostGuid: "A0000000000000000000000000000004");
        AddItem("A0000000000000000000000000000006", "HostedTool", "abcdefabcdefabcdefabcdefabcdefab", @"D:\Epic Games\Host",
            hostGuid: "A0000000000000000000000000000004");
        _files.AddDirectory(@"D:\Epic Games\Standalone");

        var scan = await Scan();

        scan.IsPresent.Should().BeTrue();
        scan.Libraries.Should().Equal(new LibraryLocation(Manifests, EpicLauncherSource.LibrarySource, Manifests, LibraryState.Online));
        scan.Games.Select(game => game.Key.StoreGameId).Should().BeEquivalentTo(Namespace, "fedcba98765432100123456789abcdef");
        scan.Games.Single(game => game.Key == Example).Should().Be(new InstalledGame
        {
            Key = Example,
            Name = "Example Game",
            InstallDirectory = Folder,
            LibraryId = Manifests,
            BuildId = "1.2.3",
            State = GameInstallState.Installed,
            StoreActivityAt = WrittenAt,
            Platform = OperatingSystemKind.Windows,
            Launch = LaunchLink.TryCreate(
                $"com.epicgames.launcher://apps/{Namespace}%3A{ItemId}%3AExample%20App?action=launch&silent=true"),
        });
    }

    [Theory]
    [InlineData("pending manifest", GameInstallState.Updating, GameStateReason.StoreBusy)]
    [InlineData("incomplete install", GameInstallState.Installing, GameStateReason.StoreBusy)]
    [InlineData("needs validation", GameInstallState.Validating, GameStateReason.StoreBusy)]
    [InlineData("older complete version", GameInstallState.Updating, GameStateReason.StoreBusy)]
    [InlineData("pending dlc", GameInstallState.Updating, GameStateReason.StoreBusy)]
    [InlineData("staged download", GameInstallState.Updating, GameStateReason.DownloadInProgress)]
    [InlineData("pending download", GameInstallState.Updating, GameStateReason.DownloadInProgress)]
    public async Task What_the_launcher_does_with_a_game_blocks_writing_it(string signal, GameInstallState state, GameStateReason reason)
    {
        var later = WrittenAt.AddHours(1);
        AddItem(Guid, "ExampleApp", Namespace, Folder, version: "2.0", incomplete: signal == "incomplete install",
            needsValidation: signal == "needs validation");
        AddItem("A0000000000000000000000000000002", "ExampleExpansion", Namespace, Folder, categories: "\"addons\"");
        _files.AddFile(InstalledDat, $$"""
            { "InstallationList": [ { "AppName": "ExampleApp", "AppVersion": "{{(signal == "older complete version" ? "1.0" : "2.0")}}" } ] }
            """);
        switch (signal)
        {
            case "pending manifest":
                _files.AddFile(Path.Join(Pending, $"{Guid}.item"), "{}", later);
                break;
            case "pending dlc":
                _files.AddFile(Path.Join(Pending, "A0000000000000000000000000000002.item"), "{}", later);
                break;
            case "staged download":
                _files.AddDirectory(Path.Join(Folder, ".egstore", "bps"));
                break;
            case "pending download":
                _files.AddFile(Path.Join(Folder, ".egstore", "Pending", "Example.pak"), "chunk", later);
                break;
        }

        var game = (await Scan()).Games.Single();

        game.Should().Match<InstalledGame>(found => found.State == state && found.Reason == reason);
        if (signal is "pending manifest" or "pending dlc")
        {
            game.StoreActivityAt.Should().Be(later);
        }
    }

    [Theory]
    [InlineData(Manifests + @"\B0000000000000000000000000000001.item")]
    [InlineData(InstalledDat)]
    public async Task A_launcher_file_caught_mid_write_keeps_the_launchers_games_as_they_were(string damaged)
    {
        AddItem(Guid, "ExampleApp", Namespace, Folder);
        _files.AddFile(damaged, """{ "AppName": """);

        var scan = await Scan();

        scan.Games.Should().BeEmpty();
        scan.Libraries.Single().State.Should().Be(LibraryState.Unreadable);
    }

    [Fact]
    public async Task Legendary_names_the_namespace_in_its_metadata_and_heroic_starts_its_games()
    {
        AddLegendary(HeroicLegendary, "ExampleApp", Namespace, Folder, title: "Example Game", version: "1.33");
        AddLegendary(HeroicLegendary, "ExampleExpansion", Namespace, Folder, isDlc: true);
        AddLegendary(HeroicLegendary, "MacApp", "99999999999999999999999999999999", @"D:\Heroic\Mac App", platform: "Mac");
        AddLegendary(Legendary, "Standalone", "fn", @"E:\Legendary\Standalone", title: "Standalone Game");

        var scan = await Scan();

        var inHeroic = Path.Join(HeroicLegendary, "installed.json");
        var standalone = Path.Join(Legendary, "installed.json");
        scan.Libraries.Should().Equal(
            new LibraryLocation(inHeroic, "heroic", inHeroic, LibraryState.Online),
            new LibraryLocation(standalone, LegendarySource.LibrarySource, standalone, LibraryState.Online));
        scan.Games.Select(game => (game.Key.StoreGameId, game.Name, game.BuildId, game.Platform, game.Launch)).Should().BeEquivalentTo([
            (Namespace, "Example Game", "1.33", OperatingSystemKind.Windows,
                LaunchLink.TryCreate("heroic://launch?appName=ExampleApp&runner=legendary")),
            ("99999999999999999999999999999999", "MacApp", (string?)null, OperatingSystemKind.MacOS,
                LaunchLink.TryCreate("heroic://launch?appName=MacApp&runner=legendary")),
            ("fn", "Standalone Game", null, OperatingSystemKind.Windows, (LaunchLink?)null),
        ]);
    }

    [Theory]
    [InlineData("resume", GameInstallState.Updating)]
    [InlineData("repair", GameInstallState.Validating)]
    [InlineData("needs verification", GameInstallState.Validating)]
    [InlineData("queued update", GameInstallState.Updating)]
    [InlineData("queued dlc install", GameInstallState.Installing)]
    public async Task What_legendary_or_heroic_does_with_a_game_blocks_writing_it(string signal, GameInstallState state)
    {
        AddLegendary(HeroicLegendary, "ExampleApp", Namespace, Folder, needsVerification: signal == "needs verification");
        AddLegendary(HeroicLegendary, "ExampleExpansion", Namespace, Folder, isDlc: true);
        if (signal is "resume" or "repair")
        {
            _files.AddFile(Path.Join(HeroicLegendary, "tmp", $"ExampleApp.{signal}"), "", WrittenAt);
        }

        var queued = signal switch
        {
            "queued update" => """{ "params": { "appName": "ExampleApp", "runner": "legendary" }, "type": "update" }""",
            "queued dlc install" => """{ "params": { "appName": "ExampleExpansion", "runner": "legendary" }, "type": "install" }""",
            _ => """{ "params": { "appName": "ExampleApp", "runner": "gog" }, "type": "update" }""",
        };
        _files.AddFile(Path.Join(Heroic, "store", "download-manager.json"), $$"""{ "queue": [ {{queued}} ] }""");

        var game = (await Scan()).Games.Single();

        game.Should().Match<InstalledGame>(found => found.State == state && found.Reason == GameStateReason.StoreBusy);
    }

    [Theory]
    [InlineData("installed")]
    [InlineData("metadata")]
    [InlineData("no metadata")]
    public async Task Legendary_files_caught_mid_write_keep_its_games_as_they_were(string damaged)
    {
        AddLegendary(HeroicLegendary, "ExampleApp", Namespace, Folder);
        switch (damaged)
        {
            case "installed":
                _files.AddFile(Path.Join(HeroicLegendary, "installed.json"), """{ "ExampleApp": { "app_name": """);
                break;
            case "metadata":
                _files.AddFile(Path.Join(HeroicLegendary, "metadata", "ExampleApp.json"), """{ "metadata": { """);
                break;
            default:
                _files.Remove(Path.Join(HeroicLegendary, "metadata", "ExampleApp.json"));
                break;
        }

        var scan = await Scan();

        scan.Games.Should().BeEmpty();
        scan.Libraries.Single().State.Should().Be(LibraryState.Unreadable);
    }

    [Fact]
    public async Task A_game_both_launchers_list_is_one_game_as_cautious_as_either()
    {
        AddItem(Guid, "ExampleApp", Namespace, Folder);
        AddLegendary(HeroicLegendary, "ExampleApp", Namespace, Folder);
        _files.AddFile(Path.Join(HeroicLegendary, "tmp", "ExampleApp.resume"), "");

        var game = (await Scan()).Games.Single();

        game.Key.Should().Be(Example);
        game.State.Should().Be(GameInstallState.Updating);
    }

    [Fact]
    public async Task A_folder_the_player_added_counts_only_with_a_launchers_manifest_in_it()
    {
        AddManifest(Folder, Namespace, "ExampleApp");
        _files.AddFile(Path.Join(@"D:\Copied", ".egstore", $"{Guid}.mancpn"), """{ "AppName": """);
        _files.AddDirectory(@"D:\Empty");
        var provider = CreateProvider();

        var scan = await provider.ScanAsync(new LibraryScanContext(new ValueList<string>([Folder, @"D:\Copied"])), Token);

        scan.Games.Should().Equal(new InstalledGame
        {
            Key = Example,
            Name = "Example",
            InstallDirectory = Folder,
            LibraryId = Folder,
            State = GameInstallState.Installed,
            StoreActivityAt = WrittenAt,
            Platform = OperatingSystemKind.Windows,
        });
        (await provider.InspectFolderAsync(Folder, Token)).Should().Be(LibraryFolderEvidence.Accepted(Folder));
        (await provider.InspectFolderAsync(@"D:\Copied", Token)).Should().Be(LibraryFolderEvidence.None);
        (await provider.InspectFolderAsync(@"D:\Empty", Token)).Should().Be(LibraryFolderEvidence.None);
    }

    [Fact]
    public async Task A_probe_reads_the_game_folder_again()
    {
        AddItem(Guid, "ExampleApp", Namespace, Folder);
        var provider = CreateProvider();
        var known = (await provider.ScanAsync(LibraryScanContext.Empty, Token)).Games.Single();
        _files.AddDirectory(Path.Join(Folder, ".egstore", "bps"));

        var probed = await provider.ProbeAsync(known, LibraryScanContext.Empty, Token);

        probed!.State.Should().Be(GameInstallState.Updating);
    }

    [Fact]
    public async Task Every_launcher_record_is_watched_but_no_game_folder()
    {
        AddItem(Guid, "ExampleApp", Namespace, Folder);
        _files.AddDirectory(Pending);
        _files.AddDirectory(@"C:\ProgramData\Epic\UnrealEngineLauncher");
        AddLegendary(HeroicLegendary, "ExampleApp", Namespace, Folder);
        _files.AddDirectory(Path.Join(HeroicLegendary, "tmp"));
        _files.AddDirectory(Path.Join(Heroic, "store"));

        var watch = (await Scan()).Watch;

        watch.Folders.Should().BeEquivalentTo([
            new WatchedFolder(Manifests, ["*.item", "Pending"]),
            new WatchedFolder(Pending, ["*.item"]),
            new WatchedFolder(@"C:\ProgramData\Epic\UnrealEngineLauncher", ["LauncherInstalled.dat"]),
            new WatchedFolder(HeroicLegendary, ["installed.json"]),
            new WatchedFolder(Path.Join(HeroicLegendary, "tmp"), ["*.resume", "*.repair"]),
            new WatchedFolder(Path.Join(HeroicLegendary, "metadata"), ["ExampleApp.json"]),
            new WatchedFolder(Path.Join(Heroic, "store"), ["download-manager.json"]),
            new WatchedFolder(@"C:\Users\Player", [".config"]),
        ]);
        watch.RegistryKeys.Should().BeEmpty();
    }

    [Fact]
    public void Legendary_is_looked_for_where_it_looks_for_its_configuration()
    {
        LibraryEnvironment Environment(string? legendaryConfig = null, string? xdgConfig = null) => new()
        {
            Platform = AppPlatform.Linux,
            HomeDirectory = @"C:\home\player",
            LegendaryConfigPath = legendaryConfig,
            XdgConfigHome = xdgConfig,
        };

        LegendarySource.StandaloneDirectory(Environment()).Should().Be(Path.Join(@"C:\home\player", ".config", "legendary"));
        LegendarySource.StandaloneDirectory(Environment(xdgConfig: @"C:\xdg")).Should().Be(Path.Join(@"C:\xdg", "legendary"));
        LegendarySource.StandaloneDirectory(Environment(@"C:\custom", @"C:\xdg")).Should().Be(@"C:\custom");
    }

    private EpicLibraryProvider CreateProvider() =>
        EpicLibraryProvider.Create(_files, _registry, new LibraryEnvironment
        {
            Platform = AppPlatform.Windows,
            HomeDirectory = @"C:\Users\Player",
            ApplicationData = @"C:\Users\Player\AppData\Roaming",
            ProgramData = ProgramData,
        }, NullLogger<EpicLibraryProvider>.Instance);

    private Task<StoreScan> Scan() => CreateProvider().ScanAsync(LibraryScanContext.Empty, Token);

    private void AddItem(string guid, string appName, string catalogNamespace, string folder, string name = "Example",
        string categories = "\"public\", \"games\", \"applications\"", string version = "1.0", bool incomplete = false,
        bool needsValidation = false, string hostGuid = "", string compatibleApps = "")
    {
        _files.AddDirectory(folder);
        _files.AddFile(Path.Join(Manifests, $"{guid}.item"), $$"""
            {
                "FormatVersion": 0,
                "bIsIncompleteInstall": {{(incomplete ? "true" : "false")}},
                "bNeedsValidation": {{(needsValidation ? "true" : "false")}},
                "AppCategories": [ {{categories}} ],
                "CompatibleApps": [ {{compatibleApps}} ],
                "DisplayName": "{{name}}",
                "InstallationGuid": "{{guid}}",
                "InstallLocation": {{Json(folder)}},
                "HostInstallationGuid": "{{hostGuid}}",
                "CatalogNamespace": "{{catalogNamespace}}",
                "CatalogItemId": "{{ItemId}}",
                "AppName": "{{appName}}",
                "AppVersionString": "{{version}}",
                "MainGameAppName": ""
            }
            """, WrittenAt);
    }

    private void AddLegendary(string legendary, string appName, string catalogNamespace, string folder, string? title = null,
        string? version = null, bool isDlc = false, bool needsVerification = false, string platform = "Windows")
    {
        _files.AddDirectory(folder);
        if (!_legendaryEntries.TryGetValue(legendary, out var entries))
        {
            _legendaryEntries[legendary] = entries = [];
        }

        entries.Add($$"""
            "{{appName}}": {
                "app_name": "{{appName}}", "title": {{Json(title)}}, "install_path": {{Json(folder)}},
                "platform": "{{platform}}", "version": {{Json(version)}}, "is_dlc": {{(isDlc ? "true" : "false")}},
                "needs_verification": {{(needsVerification ? "true" : "false")}}, "egl_guid": ""
            }
            """);
        _files.AddFile(Path.Join(legendary, "installed.json"), $"{{ {string.Join(",", entries)} }}");
        _files.AddFile(Path.Join(legendary, "metadata", $"{appName}.json"), $$"""
            { "app_name": "{{appName}}", "metadata": { "id": "{{ItemId}}", "namespace": "{{catalogNamespace}}", "mainGameItem": null } }
            """);
    }

    private void AddManifest(string folder, string catalogNamespace, string appName) =>
        _files.AddFile(Path.Join(folder, ".egstore", $"{Guid}.mancpn"), $$"""
            { "FormatVersion": 0, "CatalogNamespace": "{{catalogNamespace}}", "CatalogItemId": "{{ItemId}}", "AppName": "{{appName}}" }
            """, WrittenAt);

    private static string Json(string? value) => value is null ? "null" : $"\"{value.Replace(@"\", @"\\", StringComparison.Ordinal)}\"";
}
