using JustModIt.Manager.Core.Games;

namespace JustModIt.Manager.Core.Catalog;

/// <summary>What an installable index is built for.</summary>
/// <param name="Games">The games found in the player's stores.</param>
/// <param name="InstalledProjects">
/// Projects installed here, resolved even once the catalogue no longer lists them, so they can still be updated or
/// repaired.
/// </param>
/// <param name="Listing">The catalogue as last read, which links the games to their projects.</param>
public sealed record IndexScope(IReadOnlyCollection<GameKey> Games, IReadOnlyCollection<ProjectLocator> InstalledProjects,
    CatalogueListing Listing);

/// <summary>Project pages for the index, which a cache may answer while they are current.</summary>
public interface IProjectPageSource
{
    /// <summary>The page of a project, as current as the source keeps it.</summary>
    /// <param name="project">The project whose page is wanted.</param>
    /// <param name="listedUpdatedAt">When the catalogue last saw the project change; null when it does not list it.</param>
    /// <returns>Null when the site no longer has the page.</returns>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <exception cref="CatalogueException">The page could be neither fetched nor reused.</exception>
    Task<ProjectPageDocument?> GetPageAsync(ProjectLocator project, DateTimeOffset? listedUpdatedAt,
        CancellationToken cancellationToken);
}
