using System.Runtime.Versioning;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.Library;
using JustModIt.Manager.Core.Library.Gog;
using JustModIt.Manager.Core.Library.Steam;
using JustModIt.Manager.Core.Platform;
using Microsoft.Extensions.Logging.Abstractions;

namespace JustModIt.Manager.Platform.Windows.Tests;

[SupportedOSPlatform("windows")]
public sealed class LiveLibraryTests
{
    // Games installed on the owner's machine for live testing.
    private static readonly string[] OwnerSteamGames = ["2592160", "474960", "939850", "3994800"];

    public LiveLibraryTests() =>
        Assert.SkipUnless(OperatingSystem.IsWindows() && Environment.GetEnvironmentVariable("MANAGER_LIVE_TESTS") == "1",
            "Live tests run only with MANAGER_LIVE_TESTS=1 on Windows.");

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_real_steam_library_scans_to_sane_results()
    {
        var provider = new SteamLibraryProvider(PhysicalFileSystem.Instance, WindowsRegistry.Instance,
            LibraryEnvironment.ForCurrentUser(), NullLogger<SteamLibraryProvider>.Instance);

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
        Assert.SkipWhen(owned.Count == 0, "None of the owner's test games is installed on this machine.");
        owned.Should().OnlyContain(game => Directory.Exists(game.InstallDirectory) && game.BuildId != null);
        foreach (var game in owned.Where(game => game.IsReady))
        {
            var probed = await provider.ProbeAsync(game, LibraryScanContext.Empty, Token);
            probed!.Key.Should().Be(game.Key);
            probed.InstallDirectory.Should().Be(game.InstallDirectory);
        }

        (await provider.InspectFolderAsync(owned[0].InstallDirectory, Token)).Folder.Should().Be(owned[0].LibraryId);
    }

    [Fact]
    public async Task The_real_gog_library_scans_to_sane_results()
    {
        var provider = GogLibraryProvider.Create(PhysicalFileSystem.Instance, WindowsRegistry.Instance, LibraryEnvironment.ForCurrentUser(),
            NullLogger<GogLibraryProvider>.Instance);

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
    }
}
