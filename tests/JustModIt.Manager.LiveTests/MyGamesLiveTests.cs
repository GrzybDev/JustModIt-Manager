using System.Runtime.Versioning;
using JustModIt.Manager.Core.Catalog;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.State;
using JustModIt.Manager.Tests;

namespace JustModIt.Manager.LiveTests;

[SupportedOSPlatform("windows")]
public sealed class MyGamesLiveTests
{
    private static readonly TimeSpan SettleTimeout = TimeSpan.FromMinutes(3);

    [Fact]
    public async Task Lists_only_the_detected_games_with_an_installable_release()
    {
        LiveTest.SkipUnlessEnabledOnWindows();
        var games = LiveGames.Load();
        GameKey[] installable = [games.TargetLanguage, games.ReplacesFiles, games.AddsFiles];
        await using var engine = await LiveEngine.StartAsync(nameof(MyGamesLiveTests), LiveTest.Token);
        var coordinator = engine.StartCoordinator();

        // One scan finds all four games, so the index that offers the three also holds the announced game's project.
        var state = await SettleAsync(coordinator, state => state is { IsLibraryCached: false, CatalogueState: CatalogueState.Fresh }
            && installable.All(key => state.Find(key) is not null));

        foreach (var key in installable)
        {
            var game = state.Find(key)!;
            game.Status.State.Should().Be(MyGameState.NotInstalled, $"{game.Name} is installed and idle, with nothing on it");
            game.Project.Should().NotBeNull();
            game.Project!.Plan!.Parts[0].File.Format.Should().NotBeNull();
        }

        state.Find(games.Announced).Should().BeNull("its project is only announced");
        var announced = engine.Catalogue.Current.Index.Find(games.Announced).Should().ContainSingle().Subject;
        ReleaseSelector.Select(announced, games.Announced, OperatingSystemKind.Windows).Unavailable
            .Should().Be(SelectionUnavailability.NoPublicRelease);
        state.Find(games.TargetLanguage)!.OffersTargetLanguage.Should().BeTrue("its Steam page lists the app's target language");
        state.Find(games.AddsFiles)!.OffersTargetLanguage.Should().BeFalse();
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
