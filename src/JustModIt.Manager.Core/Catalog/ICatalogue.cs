namespace JustModIt.Manager.Core.Catalog;

/// <inheritdoc cref="CatalogueService"/>
public interface ICatalogue
{
    /// <summary>What the app knows of the catalogue now.</summary>
    CatalogueSnapshot Current { get; }

    /// <summary>Raised for every new snapshot, in order, outside any lock; handlers must return quickly.</summary>
    event EventHandler<CatalogueSnapshot>? Changed;

    /// <summary>Shows the copy saved by an earlier run without asking the site anything.</summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    Task<CatalogueSnapshot> LoadCachedAsync(CancellationToken cancellationToken);

    /// <summary>Builds the index for new games or installs from the catalogue read last, without reading it again.</summary>
    /// <param name="request">The games and installs to index.</param>
    /// <param name="cancellationToken">Cancels the build.</param>
    Task<CatalogueSnapshot> RebuildIndexAsync(IndexRequest request, CancellationToken cancellationToken);

    /// <summary>A project page, saved or read from the site as the freshness asks.</summary>
    /// <param name="project">The project as the index locates it.</param>
    /// <param name="freshness">Whether a saved page may serve; <see cref="PageFreshness.Fresh"/> right before a job.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>Null when the site does not have the page and it was never saved.</returns>
    /// <exception cref="CatalogueException">The page could not be fetched and was never saved.</exception>
    Task<CataloguePage?> GetProjectPageAsync(ProjectLocator project, PageFreshness freshness, CancellationToken cancellationToken);
}
