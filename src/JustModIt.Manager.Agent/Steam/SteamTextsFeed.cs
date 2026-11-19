using JustModIt.Manager.Core.Catalog;
using JustModIt.Manager.Core.Collections;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.State;
using JustModIt.Manager.Core.Steam;
using JustModIt.Manager.Protocol.Activation;
using Microsoft.Extensions.Logging;

namespace JustModIt.Manager.Agent.Steam;

/// <summary>
/// Hands the app's way into Steam's interface the achievement texts of the games whose texts are on, read from the
/// catalogue's cache, and the games whose launch options the app manages, whenever the state changes them.
/// </summary>
/// <param name="coordinator">The state that tells which games' texts are on.</param>
/// <param name="cache">The texts the projects published.</param>
/// <param name="steamUi">The way into Steam's interface.</param>
/// <param name="logger">Receives texts that could not be read.</param>
internal sealed partial class SteamTextsFeed(StateCoordinator coordinator, AchievementTextsCache cache, SteamUiService steamUi,
    ILogger<SteamTextsFeed> logger) : IAgentService
{
    public async Task RunAsync(LaunchArguments start, CancellationToken cancellationToken)
    {
        var version = -1L;
        List<Wanted>? delivered = null;
        ValueList<GameKey> games = default;
        while (true)
        {
            var state = await coordinator.WaitForChangeAsync(version, cancellationToken).ConfigureAwait(false);
            version = state.Version;
            // Nothing is read for Steam while nothing can bring it there, and before Steam's interface told how it can, a demand
            // made without the texts would take the plugin or the classic mode's switch away.
            if (state.SteamUi.Delivery == SteamUiDelivery.None)
            {
                continue;
            }

            List<Wanted> wanted =
            [
                .. state.Games.Select(game => game.AchievementTexts is { IsOn: true } texts
                    ? new Wanted(texts.Project, game.Key, texts.Texts.Revision)
                    : null).OfType<Wanted>(),
            ];
            if (delivered is not null && delivered.SequenceEqual(wanted) && games == state.LaunchOptionsGames)
            {
                continue;
            }

            var texts = new List<AchievementTexts>();
            delivered = [];
            foreach (var game in wanted)
            {
                if (await LoadAsync(game, cancellationToken).ConfigureAwait(false) is { } loaded)
                {
                    texts.Add(loaded);
                    delivered.Add(game);
                }
            }

            games = state.LaunchOptionsGames;
            steamUi.Demand(SteamUiDemand.From(texts, games));
        }
    }

    private async Task<AchievementTexts?> LoadAsync(Wanted game, CancellationToken cancellationToken)
    {
        try
        {
            return await cache.GetAsync(game.Project, game.Game, cancellationToken).ConfigureAwait(false);
        }
        // Read again with the next change of the state.
        catch (CatalogueException exception)
        {
            LogTextsNotRead(game.Project, exception.Reason);
            return null;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The achievement texts of project {Project} could not be read ({Reason})")]
    private partial void LogTextsNotRead(Guid project, CatalogueErrorReason reason);

    /// <summary>The texts a game takes.</summary>
    /// <param name="Project">The project that published them.</param>
    /// <param name="Game">The Steam game.</param>
    /// <param name="Revision">Their revision as the state knows it.</param>
    private sealed record Wanted(Guid Project, GameKey Game, int Revision);
}
