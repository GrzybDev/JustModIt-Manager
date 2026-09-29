using System.Runtime.Versioning;
using JustModIt.Manager.Core.Catalog;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.State;

namespace JustModIt.Manager.LiveTests;

[SupportedOSPlatform("windows")]
public sealed class MyGamesLiveTests
{
    private static readonly TimeSpan SettleTimeout = TimeSpan.FromMinutes(3);

    private static readonly GameKey[] Installable = [OwnerGames.Dispatch, OwnerGames.QuantumBreak, OwnerGames.ManOfMedan];

    [Fact]
    public async Task Detects_the_owners_games_in_the_real_steam_library()
    {
        LiveTest.SkipUnlessEnabledOnWindows();
        await using var engine = await LiveEngine.StartAsync(nameof(MyGamesLiveTests), LiveTest.Token);

        var snapshot = engine.Library.Snapshot;

        snapshot.IsCached.Should().BeFalse();
        foreach (var key in OwnerGames.All)
        {
            var game = snapshot.Find(key);
            game.Should().NotBeNull($"{key} is installed through Steam");
            Directory.Exists(game!.InstallDirectory).Should().BeTrue();
            game.Platform.Should().Be(OperatingSystemKind.Windows);
            game.BuildId.Should().NotBeNullOrEmpty();
        }
    }

    [Fact]
    public async Task Lists_only_the_detected_games_with_an_installable_release()
    {
        LiveTest.SkipUnlessEnabledOnWindows();
        await using var engine = await LiveEngine.StartAsync(nameof(MyGamesLiveTests), LiveTest.Token);
        var coordinator = engine.StartCoordinator();

        // One scan finds all four games, so the index that offers the three also holds White Whisper's project.
        var state = await SettleAsync(coordinator, state => state is { IsLibraryCached: false, CatalogueState: CatalogueState.Fresh }
            && Installable.All(key => state.Find(key) is not null));

        foreach (var key in Installable)
        {
            var game = state.Find(key)!;
            game.Status.State.Should().Be(MyGameState.NotInstalled, $"{game.Name} is installed and idle, with nothing on it");
            game.Project.Should().NotBeNull();
            game.Project!.Selection.Default!.Package!.Format.Should().NotBeNull();
        }

        state.Find(OwnerGames.WhiteWhisper).Should().BeNull("its project has no release yet");
        GameOffer.For(engine.Catalogue.Current.Index, OwnerGames.WhiteWhisper, OperatingSystemKind.Windows, LiveApp.Language)
            .Projects.Should().ContainSingle().Which.Selection.Unavailable.Should().Be(SelectionUnavailability.NoPublicRelease);
        state.Find(OwnerGames.Dispatch)!.OffersTargetLanguage.Should().BeTrue("its Steam page lists the app's target language");
        state.Find(OwnerGames.ManOfMedan)!.OffersTargetLanguage.Should().BeFalse();
    }

    private static async Task<AppState> SettleAsync(StateCoordinator coordinator, Func<AppState, bool> settled)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(LiveTest.Token);
        timeout.CancelAfter(SettleTimeout);
        var state = coordinator.Current;
        try
        {
            while (!settled(state))
            {
                state = await coordinator.WaitForChangeAsync(state.Version, timeout.Token);
            }
        }
        catch (OperationCanceledException) when (!LiveTest.Token.IsCancellationRequested)
        {
            var games = string.Join(", ", state.Games.Select(game => game.Name));
            Assert.Fail($"The app did not settle in {SettleTimeout}: library cached {state.IsLibraryCached}, catalogue "
                + $"{state.CatalogueState} ({state.CatalogueFailure}), games [{games}].");
        }

        return state;
    }
}
