using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Protocol.Messages;
using JustModIt.Manager.Protocol.Views;

namespace JustModIt.Manager.Ui.Navigation;

/// <summary>Where a place the agent names (a link, a notification) leads in the window.</summary>
public static class AgentLinks
{
    /// <summary>
    /// The routes to open in order: a sidebar destination, then what goes on top of it, so going back from a game or a
    /// project lands on its list.
    /// </summary>
    /// <param name="target">The place, as a link names it; any website can make one, so nothing here starts an action.</param>
    /// <param name="state">The agent's state: the player's games a project link looks in, and whether the package creator is on.</param>
    public static IReadOnlyList<Route> RoutesFor(NavigationTarget target, AppStateView state)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(state);
        return target switch
        {
            GameTarget game when StoreId.TryParse(game.Store, out var store) && !string.IsNullOrWhiteSpace(game.GameId) =>
                [new LibraryRoute(), new GameRoute(new GameKey(store, game.GameId))],
            CatalogueTarget { GameSlug: { Length: > 0 } game, TeamSlug: { Length: > 0 } team, ProjectSlug: { Length: > 0 } project } =>
                [new CatalogueRoute(), new CatalogueProjectRoute(game, team, project)],
            CatalogueTarget { GameSlug: { Length: > 0 } game } => [new CatalogueRoute(), new CatalogueGameRoute(game)],
            CatalogueTarget => [new CatalogueRoute()],
            InstallTarget install => Install(install, state.Games),
            SettingsTarget => [new SettingsRoute()],
            CreatorTarget when state.Settings.CreatorMode => [new CreatorRoute()],
            _ => [new LibraryRoute()],
        };
    }

    // Of several copies of the game, the one that holds the project comes first, then the first that offers it.
    private static IReadOnlyList<Route> Install(InstallTarget link, IReadOnlyList<GameView> games)
    {
        var detected = games.Where(game => game.Detected is not null).ToList();
        var found = detected.SelectMany(game => game.Installs.Select(install => (game, project: install.Project)))
            .Concat(detected.SelectMany(game => game.Projects.Select(project => (game, project: (ProjectOfferView?)project))))
            .FirstOrDefault(entry => entry.project is { } project && Names(link, project));
        return found is ({ } game, { } offer)
            ? [new LibraryRoute(), new GameRoute(game.Key, new SiteChoice(offer.ProjectId, link.Features, link.Variant))]
            : [new CatalogueRoute(), new CatalogueProjectRoute(link.GameSlug, link.TeamSlug, link.ProjectSlug)];
    }

    private static bool Names(InstallTarget link, ProjectOfferView project) =>
        string.Equals(project.GameSlug, link.GameSlug, StringComparison.OrdinalIgnoreCase)
        && string.Equals(project.Team.Slug, link.TeamSlug, StringComparison.OrdinalIgnoreCase)
        && string.Equals(project.ProjectSlug, link.ProjectSlug, StringComparison.OrdinalIgnoreCase);
}
