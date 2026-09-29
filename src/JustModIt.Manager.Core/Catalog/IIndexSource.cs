using JustModIt.Manager.Core.Games;

namespace JustModIt.Manager.Core.Catalog;

/// <summary>What an installable index is built for.</summary>
/// <param name="Games">The games found in the player's stores.</param>
/// <param name="InstalledProjects">
/// Projects installed here, resolved even once the catalogue no longer lists them, so they can still be updated or
/// repaired.
/// </param>
/// <param name="Listing">The catalogue as last read; a source with an index of its own may ignore it.</param>
public sealed record IndexScope(IReadOnlyCollection<GameKey> Games, IReadOnlyCollection<ProjectLocator> InstalledProjects,
    CatalogueListing Listing);

/// <summary>
/// Where the installable index comes from, so an index endpoint of the backend can replace today's gathering from the
/// site's public pages without changing any caller.
/// </summary>
public interface IIndexSource
{
    /// <summary>Builds the index of the projects the scope's games and installs may use.</summary>
    /// <param name="scope">The games, installed projects and listing to index.</param>
    /// <param name="cancellationToken">Cancels the build.</param>
    /// <exception cref="CatalogueException">The index could not be built; nothing partial is returned.</exception>
    Task<InstallableIndex> BuildAsync(IndexScope scope, CancellationToken cancellationToken);
}

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

/// <summary>Project pages read straight from the site, with nothing reused.</summary>
/// <param name="api">The site's API.</param>
public sealed class ApiProjectPageSource(ICatalogueApi api) : IProjectPageSource
{
    public async Task<ProjectPageDocument?> GetPageAsync(ProjectLocator project, DateTimeOffset? listedUpdatedAt,
        CancellationToken cancellationToken)
    {
        try
        {
            return await api.GetProjectPageAsync(project.GameSlug, project.TeamSlug, cancellationToken).ConfigureAwait(false);
        }
        catch (CatalogueException exception) when (exception.Reason == CatalogueErrorReason.NotFound)
        {
            return null;
        }
    }
}
