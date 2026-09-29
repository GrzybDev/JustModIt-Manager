using Microsoft.Extensions.Logging;

namespace JustModIt.Manager.Core.Catalog;

public enum PageFreshness
{
    /// <summary>A saved page serves while the catalogue shows no change to the project and it is not too old.</summary>
    Current,

    /// <summary>Always asked for; right before a job, whose release and file ids must be the site's current ones.</summary>
    Fresh,
}

/// <summary>A project page with how current it is.</summary>
/// <param name="Document">The page with the JSON the site sent.</param>
/// <param name="FetchedAt">When the page was read from the site.</param>
/// <param name="RefreshError">Why a fresh copy could not be had; the page is then the one saved earlier.</param>
/// <param name="IsGone">The site no longer has the page; the one saved earlier stays readable.</param>
public sealed record CataloguePage(ProjectPageDocument Document, DateTimeOffset FetchedAt, CatalogueException? RefreshError = null,
    bool IsGone = false)
{
    public ProjectPage Page => Document.Page;

    public bool IsStale => RefreshError is not null;
}

/// <summary>
/// Project pages from the site, saved on disk and reused while current, with nothing kept in memory, so an app that sits
/// in the tray holds no page it is not showing.
/// </summary>
/// <param name="api">Reads pages from the site.</param>
/// <param name="cache">Keeps the saved pages.</param>
/// <param name="time">The clock that ages saved pages.</param>
/// <param name="logger">Logs pages that are gone, served stale or not saved.</param>
public sealed partial class ProjectPageCache(ICatalogueApi api, CatalogueCache cache, TimeProvider time,
    ILogger<ProjectPageCache> logger) : IProjectPageSource
{
    /// <summary>
    /// How long a saved page serves at most, short since staff hiding a release and other changes leave the project's
    /// change time alone, so that only a burst of checks the player asks for reuses a page.
    /// </summary>
    public static readonly TimeSpan MaxAge = TimeSpan.FromHours(1);

    public async Task<ProjectPageDocument?> GetPageAsync(ProjectLocator project, DateTimeOffset? listedUpdatedAt,
        CancellationToken cancellationToken)
    {
        var page = await GetAsync(project, PageFreshness.Current, listedUpdatedAt, cancellationToken).ConfigureAwait(false);
        return page is { IsGone: false } ? page.Document : null;
    }

    /// <summary>A project page, saved or read from the site as the freshness asks.</summary>
    /// <param name="project">The project as the index locates it.</param>
    /// <param name="freshness">Whether a saved page may serve.</param>
    /// <param name="listedUpdatedAt">When the catalogue last saw the project change; null when it does not list it.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>Null when the site does not have the page and it was never saved.</returns>
    /// <exception cref="CatalogueException">The page could not be fetched and was never saved.</exception>
    public async Task<CataloguePage?> GetAsync(ProjectLocator project, PageFreshness freshness, DateTimeOffset? listedUpdatedAt,
        CancellationToken cancellationToken)
    {
        var saved = await cache.LoadPageAsync(project.ProjectUuid, cancellationToken).ConfigureAwait(false);
        if (freshness == PageFreshness.Current && saved is not null && IsCurrent(saved, listedUpdatedAt))
        {
            return new CataloguePage(saved.Document, saved.FetchedAt);
        }

        ProjectPageDocument fetched;
        try
        {
            fetched = await api.GetProjectPageAsync(project.GameSlug, project.TeamSlug, cancellationToken).ConfigureAwait(false);
        }
        catch (CatalogueException exception) when (exception.Reason == CatalogueErrorReason.NotFound)
        {
            return Gone(saved, exception);
        }
        catch (CatalogueException exception) when (saved is not null)
        {
            LogServedSaved(project.ProjectUuid, exception.Reason, exception);
            return new CataloguePage(saved.Document, saved.FetchedAt, exception);
        }

        // The slugs may now lead to another project, e.g. one the team made after deleting this one.
        if (fetched.Page.Uuid != project.ProjectUuid)
        {
            return Gone(saved, new CatalogueException(CatalogueErrorReason.NotFound,
                $"The page of project {project.ProjectUuid:D} now shows project {fetched.Page.Uuid:D}."));
        }

        var now = time.GetUtcNow();
        try
        {
            await cache.SavePageAsync(fetched, now, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The fresh page is in hand; a full or read-only disk only costs the offline copy.
            LogNotSaved(project.ProjectUuid, exception);
        }

        return new CataloguePage(fetched, now);
    }

    // A copy from the future, saved while the clock was wrong, would otherwise never grow old.
    private bool IsCurrent(CachedProjectPage saved, DateTimeOffset? listedUpdatedAt) =>
        time.GetUtcNow() - saved.FetchedAt is var age && age >= TimeSpan.Zero && age < MaxAge
        && listedUpdatedAt is { } listed && saved.Document.Page.UpdatedAt is { } changed && changed >= listed;

    private CataloguePage? Gone(CachedProjectPage? saved, CatalogueException exception)
    {
        LogGone(exception.Message);
        return saved is null ? null : new CataloguePage(saved.Document, saved.FetchedAt, exception, IsGone: true);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Project page {Project} could not be refreshed ({Reason}), so the saved one serves")]
    private partial void LogServedSaved(Guid project, CatalogueErrorReason reason, Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "A project page is gone from the site: {Detail}")]
    private partial void LogGone(string detail);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Project page {Project} could not be saved for offline use")]
    private partial void LogNotSaved(Guid project, Exception exception);
}
