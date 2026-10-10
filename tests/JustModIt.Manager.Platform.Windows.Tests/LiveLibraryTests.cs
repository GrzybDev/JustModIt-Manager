using System.Runtime.Versioning;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.IO;
using JustModIt.Manager.Core.Library;
using JustModIt.Manager.Core.Library.Epic;
using JustModIt.Manager.Core.Library.Gog;
using JustModIt.Manager.Core.Library.Steam;
using JustModIt.Manager.Core.Library.Watching;
using JustModIt.Manager.Core.Platform;
using JustModIt.Manager.Core.Safety;
using JustModIt.Manager.Core.Settings;
using Microsoft.Extensions.Logging.Abstractions;

namespace JustModIt.Manager.Platform.Windows.Tests;

[SupportedOSPlatform("windows")]
public sealed class LiveLibraryTests
{
    // Games installed on the owner's machine for live testing.
    private static readonly string[] OwnerSteamGames = ["2592160", "474960", "939850", "3994800"];

    // Installed through Heroic's Legendary into a folder of a Steam library, without a Steam manifest.
    private static readonly GameKey OwnerEpicGame = new(StoreId.Epic, "2e92a78949e2474aa89271b8b893f3b0");

    public LiveLibraryTests() =>
        Assert.SkipUnless(OperatingSystem.IsWindows() && Environment.GetEnvironmentVariable("MANAGER_LIVE_TESTS") == "1",
            "Live tests run only with MANAGER_LIVE_TESTS=1 on Windows.");

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_real_steam_library_scans_to_sane_results()
    {
        var provider = new SteamLibraryProvider(PhysicalFileSystem.Instance, WindowsRegistry.Instance,
            LibraryEnvironment.ForCurrentUser(), TimeProvider.System, NullLogger<SteamLibraryProvider>.Instance);

        var scan = await provider.ScanAsync(LibraryScanContext.Empty, Token);

        scan.IsPresent.Should().BeTrue();
        scan.IsComplete.Should().BeTrue();
        scan.Libraries.Should().NotBeEmpty().And.OnlyContain(library => Path.IsPathFullyQualified(library.Id));
        scan.Games.Select(game => game.Key).Should().OnlyHaveUniqueItems();
        scan.Games.Select(game => game.InstallDirectory).Should().OnlyHaveUniqueItems();
        scan.Games.Should().OnlyContain(game => scan.Libraries.Any(library => library.Id == game.LibraryId)
            && !string.IsNullOrWhiteSpace(game.Name) && game.Platform == OperatingSystemKind.Windows);
        scan.Games.Select(game => game.Key.StoreGameId).Should().NotContain(["228980", "250820"], "tools are not games");
        scan.Watch.Folders.Should().NotBeEmpty();

        var owned = scan.Games.Where(game => OwnerSteamGames.Contains(game.Key.StoreGameId)).ToList();
        owned.Select(game => game.Key.StoreGameId).Should().BeEquivalentTo(OwnerSteamGames, "the owner installed the test games through Steam");
        owned.Should().OnlyContain(game => Directory.Exists(game.InstallDirectory) && !string.IsNullOrEmpty(game.BuildId));
        foreach (var game in owned.Where(game => game.IsReady))
        {
            var probed = await provider.ProbeAsync(game, LibraryScanContext.Empty, Token);
            probed!.Key.Should().Be(game.Key);
            probed.InstallDirectory.Should().Be(game.InstallDirectory);
        }

        (await provider.InspectFolderAsync(owned[0].InstallDirectory, Token)).Folder.Should().Be(owned[0].LibraryId);
    }

    [Fact]
    public async Task The_library_service_scans_and_watches_the_real_stores()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ExampleApp.Tests", Guid.NewGuid().ToString("N"));
        var environment = LibraryEnvironment.ForCurrentUser();
        var time = TimeProvider.System;
        await using var settings = await SettingsService.LoadAsync(
            new JsonDocumentStore<AppSettings>(Path.Combine(directory, "settings.json"), SettingsFormat.Instance, time,
                NullLogger<JsonDocumentStore<AppSettings>>.Instance),
            time, NullLogger<SettingsService>.Instance, Token);
        using var systemEvents = new WindowsSystemEvents(NullLogger<WindowsSystemEvents>.Instance);
        await using var service = new LibraryService(
            [
                new SteamLibraryProvider(PhysicalFileSystem.Instance, WindowsRegistry.Instance, environment, time,
                    NullLogger<SteamLibraryProvider>.Instance),
                GogLibraryProvider.Create(PhysicalFileSystem.Instance, WindowsRegistry.Instance, WindowsSqliteReader.Instance, environment,
                    NullLogger<GogLibraryProvider>.Instance),
                EpicLibraryProvider.Create(PhysicalFileSystem.Instance, WindowsRegistry.Instance, environment,
                    NullLogger<EpicLibraryProvider>.Instance),
            ],
            settings,
            new RunningGameDetector(WindowsProcessSource.Instance, PhysicalFileSystem.Instance, time),
            PhysicalFileSystem.Instance,
            FileSystemFolderWatcher.Instance,
            WindowsRegistryWatcher.Instance,
            systemEvents,
            new JsonDocumentStore<LibrarySnapshot>(Path.Combine(directory, "library.json"), LibrarySnapshotFormat.Instance, time,
                NullLogger<JsonDocumentStore<LibrarySnapshot>>.Instance),
            time,
            NullLogger<LibraryService>.Instance);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var running = service.RunAsync(stop.Token);
        try
        {
            await service.CheckNowAsync(Token).WaitAsync(TimeSpan.FromMinutes(1), Token);

            var snapshot = service.Snapshot;
            snapshot.IsCached.Should().BeFalse();
            snapshot.FindStore(StoreId.Steam)!.IsPresent.Should().BeTrue();
            service.ActiveWatchCount.Should().BeGreaterThan(0);
            DetectionReport.From(snapshot).Stores.Should().Contain(store => store.Store == StoreId.Steam && store.GameCount > 0)
                .And.Contain(store => store.Store == StoreId.Epic);
            var owned = snapshot.Games.Values.FirstOrDefault(game => game.Key.Store == StoreId.Steam && OwnerSteamGames.Contains(game.Key.StoreGameId));
            if (owned is not null)
            {
                var probe = await service.ProbeAsync(owned.Key, Token);
                probe.Game!.InstallDirectory.Should().Be(owned.InstallDirectory);
            }

            File.Exists(Path.Combine(directory, "library.json")).Should().BeTrue();
        }
        finally
        {
            await stop.CancelAsync();
            await FluentActions.Awaiting(() => running).Should().ThrowAsync<OperationCanceledException>();
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Reading_each_real_library_within_the_time_limit_finds_what_an_unlimited_scan_finds()
    {
        var environment = LibraryEnvironment.ForCurrentUser();
        ILibraryProvider[] providers =
        [
            new SteamLibraryProvider(PhysicalFileSystem.Instance, WindowsRegistry.Instance, environment, TimeProvider.System,
                NullLogger<SteamLibraryProvider>.Instance),
            GogLibraryProvider.Create(PhysicalFileSystem.Instance, WindowsRegistry.Instance, WindowsSqliteReader.Instance, environment,
                NullLogger<GogLibraryProvider>.Instance),
            EpicLibraryProvider.Create(PhysicalFileSystem.Instance, WindowsRegistry.Instance, environment,
                NullLogger<EpicLibraryProvider>.Instance),
        ];
        var limited = LibraryScanContext.Empty with { Access = new LibraryAccess(TimeProvider.System) };

        foreach (var provider in providers)
        {
            var unlimited = await provider.ScanAsync(LibraryScanContext.Empty, Token);
            var scan = await provider.ScanAsync(limited, Token);

            scan.IsComplete.Should().Be(unlimited.IsComplete);
            scan.Libraries.Should().Equal(unlimited.Libraries);
            scan.Games.Select(game => (game.Key, game.InstallDirectory)).Should().Equal(unlimited.Games.Select(game => (game.Key, game.InstallDirectory)));
            scan.UnansweredGames.Should().BeEmpty();
        }
    }

    [Fact]
    public async Task The_real_gog_library_scans_to_sane_results()
    {
        var provider = GogLibraryProvider.Create(PhysicalFileSystem.Instance, WindowsRegistry.Instance, WindowsSqliteReader.Instance,
            LibraryEnvironment.ForCurrentUser(), NullLogger<GogLibraryProvider>.Instance);

        var scan = await provider.ScanAsync(LibraryScanContext.Empty, Token);

        Assert.SkipUnless(scan.IsPresent, "GOG is not installed on this machine.");
        scan.IsComplete.Should().BeTrue();
        scan.Games.Select(game => game.Key).Should().OnlyHaveUniqueItems();
        scan.Games.Should().OnlyContain(game => scan.Libraries.Any(library => library.Id == game.LibraryId)
            && game.Key.StoreGameId.All(char.IsAsciiDigit) && !string.IsNullOrWhiteSpace(game.Name));
        foreach (var game in scan.Games.Where(game => game.IsReady && game.Reason == GameStateReason.None))
        {
            Directory.Exists(game.InstallDirectory).Should().BeTrue();
            (await provider.InspectFolderAsync(game.InstallDirectory, Token)).IsAccepted.Should().BeTrue();
            (await provider.ProbeAsync(game, LibraryScanContext.Empty, Token))!.State.Should().Be(GameInstallState.Installed);
        }

        var galaxyInstalled = WindowsRegistry.Instance.ReadKey(RegistryHive.LocalMachine, RegistryView.Registry32,
            @"SOFTWARE\GOG.com\GalaxyClient") is not null;
        var galaxy = GalaxyDatabase.Locate(PhysicalFileSystem.Instance, WindowsSqliteReader.Instance, LibraryEnvironment.ForCurrentUser())!
            .Read();
        foreach (var game in scan.Games.Where(game => game.LibraryId == GogRegistrySource.LibrarySource))
        {
            (game.Launch is LaunchLink link && link.Uri.StartsWith("goggalaxy:", StringComparison.Ordinal)).Should().Be(
                galaxyInstalled && galaxy.Manages(game.Key.StoreGameId, game.InstallDirectory), "only Galaxy's own games open in Galaxy");
        }
    }

    [Fact]
    public async Task The_real_epic_library_finds_the_owners_game_and_no_engine_content()
    {
        var environment = LibraryEnvironment.ForCurrentUser();
        var provider = EpicLibraryProvider.Create(PhysicalFileSystem.Instance, WindowsRegistry.Instance, environment,
            NullLogger<EpicLibraryProvider>.Instance);

        var scan = await provider.ScanAsync(LibraryScanContext.Empty, Token);

        Assert.SkipUnless(scan.IsPresent, "Neither the Epic Games Launcher nor Legendary is installed on this machine.");
        scan.IsComplete.Should().BeTrue();
        scan.Libraries.Should().OnlyContain(library => library.State == LibraryState.Online);
        scan.Games.Select(game => game.Key).Should().OnlyHaveUniqueItems();
        scan.Games.Should().NotContain(game => game.Key.StoreGameId == "ue" || game.Name.Contains("Unreal", StringComparison.Ordinal),
            "the engine and its plugins are no games");
        var owned = scan.Games.FirstOrDefault(game => game.Key == OwnerEpicGame);
        Assert.SkipWhen(owned is null, "The owner's Epic test game is not installed on this machine.");
        owned.Should().Match<InstalledGame>(game => game.IsReady && !string.IsNullOrEmpty(game.BuildId)
            && game.Platform == OperatingSystemKind.Windows && Directory.Exists(game.InstallDirectory));
        owned.Launch.Should().Be(LaunchLink.TryCreate("heroic://launch?appName=7315f614b0504081bcdceef138075c9c&runner=legendary"));
        (await provider.ProbeAsync(owned, LibraryScanContext.Empty, Token)).Should().Be(owned);
        (await provider.InspectFolderAsync(owned.InstallDirectory, Token)).Folder.Should().Be(owned.InstallDirectory);

        var steam = await new SteamLibraryProvider(PhysicalFileSystem.Instance, WindowsRegistry.Instance, environment, TimeProvider.System,
            NullLogger<SteamLibraryProvider>.Instance).ScanAsync(LibraryScanContext.Empty, Token);
        steam.Games.Should().NotContain(game => PathComparison.AreSame(game.InstallDirectory, owned.InstallDirectory),
            "Steam has no manifest for a game Legendary installed into its library");
    }
}
