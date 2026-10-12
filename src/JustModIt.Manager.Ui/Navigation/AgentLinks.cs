using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Protocol.Messages;

namespace JustModIt.Manager.Ui.Navigation;

/// <summary>Where a place the agent names (a link, a notification) leads in the window.</summary>
public static class AgentLinks
{
    /// <summary>
    /// The routes to open in order: a sidebar destination, then what goes on top of it, so going back from a game or a
    /// project lands on its list.
    /// </summary>
    /// <param name="target">The place, as a link names it; any website can make one, so nothing here starts an action.</param>
    /// <param name="creatorMode">Whether the package creator is on; otherwise its link opens the player's games.</param>
    public static IReadOnlyList<Route> RoutesFor(NavigationTarget target, bool creatorMode) => target switch
    {
        GameTarget game when StoreId.TryParse(game.Store, out var store) && !string.IsNullOrWhiteSpace(game.GameId) =>
            [new LibraryRoute(), new GameRoute(new GameKey(store, game.GameId))],
        CatalogueTarget { GameSlug: { Length: > 0 } game, TeamSlug: { Length: > 0 } team, ProjectSlug: { Length: > 0 } project } =>
            [new CatalogueRoute(), new CatalogueProjectRoute(game, team, project)],
        CatalogueTarget { GameSlug: { Length: > 0 } game } => [new CatalogueRoute(), new CatalogueGameRoute(game)],
        CatalogueTarget => [new CatalogueRoute()],
        SettingsTarget => [new SettingsRoute()],
        CreatorTarget when creatorMode => [new CreatorRoute()],
        _ => [new LibraryRoute()],
    };
}
