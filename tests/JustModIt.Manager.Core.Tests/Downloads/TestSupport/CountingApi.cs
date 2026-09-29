using JustModIt.Manager.Core.Catalog;

namespace JustModIt.Manager.Core.Tests.Downloads.TestSupport;

/// <summary>Stands in for the site's counting download endpoint and counts how often it was asked.</summary>
/// <param name="resolve">Gives the storage address the endpoint answers with for a file.</param>
internal sealed class CountingApi(Func<ReleaseFileLocator, Uri> resolve) : ICatalogueApi
{
    private int _resolved;

    public int Resolved => Volatile.Read(ref _resolved);

    public Task<Uri> ResolveDownloadAsync(ReleaseFileLocator file, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _resolved);
        return Task.FromResult(resolve(file));
    }

    public Task<ApiPage<CatalogueGame>> GetGamesAsync(int page, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<IReadOnlyList<ProjectSummary>> GetGameProjectsAsync(string gameSlug, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<ProjectPageDocument> GetProjectPageAsync(string gameSlug, string teamSlug, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<ApiPage<Team>> GetTeamsAsync(int page, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<IReadOnlyList<ProjectStatus>> GetStatusesAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<IReadOnlyList<ProjectType>> GetProjectTypesAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<IReadOnlyList<Genre>> GetGenresAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<IReadOnlyList<GamePlatform>> GetGamePlatformsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<IReadOnlyList<StorefrontPlatform>> GetStorefrontPlatformsAsync(CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<SiteConfiguration> GetSiteConfigurationAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
}
