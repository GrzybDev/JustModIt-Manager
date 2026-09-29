namespace JustModIt.Manager.Core.Catalog;

/// <summary>The site's public API, which reports every failure as a <see cref="CatalogueException"/>.</summary>
public interface ICatalogueApi
{
    /// <summary>
    /// One page, counted from 1, of the games that have a listed project. Abandoned projects are not listed, so a game
    /// whose only project is abandoned is missing here.
    /// </summary>
    /// <param name="page">The page number, counted from 1.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    Task<ApiPage<CatalogueGame>> GetGamesAsync(int page, CancellationToken cancellationToken);

    /// <summary>Every visible project of the game, abandoned ones included.</summary>
    /// <param name="gameSlug">The game's slug as the catalogue lists it.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    Task<IReadOnlyList<ProjectSummary>> GetGameProjectsAsync(string gameSlug, CancellationToken cancellationToken);

    /// <summary>A project's public page, with the JSON exactly as the site sent it.</summary>
    /// <param name="gameSlug">The game's slug.</param>
    /// <param name="teamSlug">The slug of the team that made the project, which makes at most one per game.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    Task<ProjectPageDocument> GetProjectPageAsync(string gameSlug, string teamSlug, CancellationToken cancellationToken);

    /// <summary>One page of the listed teams.</summary>
    /// <param name="page">The page number, counted from 1.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    Task<ApiPage<Team>> GetTeamsAsync(int page, CancellationToken cancellationToken);

    /// <summary>The project statuses the site's admins defined.</summary>
    /// <param name="cancellationToken">Cancels the request.</param>
    Task<IReadOnlyList<ProjectStatus>> GetStatusesAsync(CancellationToken cancellationToken);

    /// <summary>The kinds of project the site hosts.</summary>
    /// <param name="cancellationToken">Cancels the request.</param>
    Task<IReadOnlyList<ProjectType>> GetProjectTypesAsync(CancellationToken cancellationToken);

    /// <summary>The genres games are filed under.</summary>
    /// <param name="cancellationToken">Cancels the request.</param>
    Task<IReadOnlyList<Genre>> GetGenresAsync(CancellationToken cancellationToken);

    /// <summary>The hardware platforms games are listed for.</summary>
    /// <param name="cancellationToken">Cancels the request.</param>
    Task<IReadOnlyList<GamePlatform>> GetGamePlatformsAsync(CancellationToken cancellationToken);

    /// <summary>The stores projects link games to.</summary>
    /// <param name="cancellationToken">Cancels the request.</param>
    Task<IReadOnlyList<StorefrontPlatform>> GetStorefrontPlatformsAsync(CancellationToken cancellationToken);

    /// <summary>What the site says about itself in its public configuration.</summary>
    /// <param name="cancellationToken">Cancels the request.</param>
    Task<SiteConfiguration> GetSiteConfigurationAsync(CancellationToken cancellationToken);

    /// <summary>
    /// The storage address of a release file, which the site counts as a download, so it is asked for once per real
    /// download right before the bytes are needed, never to check availability, and never repeated once it may have
    /// reached the server.
    /// </summary>
    /// <param name="file">The release file to download.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    Task<Uri> ResolveDownloadAsync(ReleaseFileLocator file, CancellationToken cancellationToken);
}
