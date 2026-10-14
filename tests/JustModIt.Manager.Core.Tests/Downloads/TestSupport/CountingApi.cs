using JustModIt.Manager.Core.Catalog;

namespace JustModIt.Manager.Core.Tests.Downloads.TestSupport;

/// <summary>Stands in for the site's counting download endpoints and records what they were asked for.</summary>
/// <param name="resolve">Gives the storage address the endpoints answer with for a file.</param>
internal sealed class CountingApi(Func<ReleaseFileLocator, Uri> resolve) : ICatalogueApi
{
    private readonly System.Collections.Concurrent.ConcurrentQueue<IReadOnlyList<Guid>> _requests = new();
    private int _resolved;

    /// <summary>How many requests either endpoint answered.</summary>
    public int Resolved => Volatile.Read(ref _resolved);

    /// <summary>The files each request asked for, a single file's request as a list of one.</summary>
    public IReadOnlyList<IReadOnlyList<Guid>> Requests => [.. _requests];

    /// <summary>Runs before each request is answered, e.g. to look at what was saved before it.</summary>
    public Action? BeforeAnswer { get; set; }

    /// <summary>Answers the next request of a release's files with this failure instead.</summary>
    public Exception? NextFailure { get; set; }

    public Task<Uri> ResolveDownloadAsync(ReleaseFileLocator file, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _resolved);
        _requests.Enqueue([file.FileUuid]);
        BeforeAnswer?.Invoke();
        return Task.FromResult(resolve(file));
    }

    public Task<IReadOnlyDictionary<Guid, Uri>> ResolveDownloadsAsync(ReleaseLocator release, IReadOnlyList<Guid> files,
        CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _resolved);
        _requests.Enqueue(files);
        BeforeAnswer?.Invoke();
        if (NextFailure is { } failure)
        {
            NextFailure = null;
            throw failure;
        }

        return Task.FromResult<IReadOnlyDictionary<Guid, Uri>>(files.ToDictionary(file => file,
            file => resolve(new ReleaseFileLocator(release.TeamSlug, release.ProjectSlug, release.ReleaseUuid, file))));
    }

    public Task<ApiPage<CatalogueGame>> GetGamesAsync(int page, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<ApiPage<GameDownloads>> GetGameDownloadsAsync(int page, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

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
