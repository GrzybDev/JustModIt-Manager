using JustModIt.Manager.Core.Games;
using Microsoft.Extensions.Logging;

namespace JustModIt.Manager.Core.Catalog;

public enum CatalogueState
{
    /// <summary>Nothing saved and nothing read from the site yet.</summary>
    Empty,

    /// <summary>The copy saved by an earlier run, until the site is asked.</summary>
    Cached,

    Refreshing,

    Fresh,

    /// <summary>The site could not be read; the last good copy serves.</summary>
    Stale,

    /// <summary>The site could not be read and no copy was ever saved.</summary>
    Failed,
}

/// <summary>What the app knows of the catalogue at one moment, immutable so any thread may hold it.</summary>
public sealed record CatalogueSnapshot
{
    public static CatalogueSnapshot Empty { get; } = new();

    public CatalogueState State { get; init; }

    public InstallableIndex Index { get; init; } = InstallableIndex.Empty;

    public SiteConfiguration Site { get; init; } = new();

    /// <summary>When the catalogue was last read from the site; null before the first time.</summary>
    public DateTimeOffset? FetchedAt { get; init; }

    /// <summary>Why the last refresh failed; null after a successful one.</summary>
    public CatalogueErrorReason? Failure { get; init; }

    /// <summary>How long the site asked to wait after refusing the last refresh.</summary>
    public TimeSpan? RetryAfter { get; init; }
}

/// <summary>The games and installs an index is built for.</summary>
/// <param name="Games">The games found in the player's stores.</param>
/// <param name="InstalledProjects">The projects installed on this machine.</param>
public sealed record IndexRequest(IReadOnlyCollection<GameKey> Games, IReadOnlyCollection<ProjectLocator> InstalledProjects);

/// <summary>
/// Keeps the catalogue offline first, serving the copy saved on disk at once and replacing it only once the site answered,
/// with operations run one at a time, in the order asked for, never under a lock.
/// </summary>
/// <param name="crawler">Reads the whole catalogue from the site.</param>
/// <param name="indexSource">Builds the installable index.</param>
/// <param name="pages">Serves project pages, reusing saved ones while current.</param>
/// <param name="cache">Keeps the catalogue on disk for offline use.</param>
/// <param name="logger">Logs failed refreshes, saves and listeners.</param>
public sealed partial class CatalogueService(
    CatalogueCrawler crawler,
    IIndexSource indexSource,
    ProjectPageCache pages,
    CatalogueCache cache,
    ILogger<CatalogueService> logger) : ICatalogue
{
    private readonly Lock _gate = new();
    private Task _tail = Task.CompletedTask;
    private CatalogueSnapshot _current = CatalogueSnapshot.Empty;

    // The whole catalogue is only needed while the player browses it or an index is built, so a resident app lets the
    // collector reclaim it and reads it back from disk when asked again.
    private WeakReference<CatalogueListing>? _listing;

    public event EventHandler<CatalogueSnapshot>? Changed;

    public CatalogueSnapshot Current => Volatile.Read(ref _current);

    public Task<CatalogueSnapshot> LoadCachedAsync(CancellationToken cancellationToken) => Enqueue(async () =>
    {
        if (Current.FetchedAt is not null)
        {
            return Current;
        }

        var index = await cache.LoadIndexAsync(cancellationToken).ConfigureAwait(false);
        var site = await cache.LoadSiteAsync(cancellationToken).ConfigureAwait(false);
        return index is null
            ? Current
            : Publish(Current with { State = CatalogueState.Cached, Index = index, Site = site ?? new(), FetchedAt = index.BuiltAt });
    });

    /// <summary>Reads the whole catalogue from the site and builds the index for the request.</summary>
    /// <param name="request">The games and installs to index.</param>
    /// <param name="cancellationToken">Cancels the refresh, leaving the snapshot as it was.</param>
    public Task<CatalogueSnapshot> RefreshAsync(IndexRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Enqueue(async () =>
        {
            var before = Current;
            Publish(before with { State = CatalogueState.Refreshing });
            try
            {
                CatalogueListing listing;
                try
                {
                    listing = await crawler.CrawlAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (CatalogueException exception)
                {
                    return Failed(before, exception);
                }

                Volatile.Write(ref _listing, new WeakReference<CatalogueListing>(listing));
                await SaveQuietlyAsync(() => cache.SaveListingAsync(listing, cancellationToken)).ConfigureAwait(false);
                await SaveQuietlyAsync(() => cache.SaveSiteAsync(listing.Reference.Site, listing.FetchedAt, cancellationToken))
                    .ConfigureAwait(false);
                return await BuildIndexAsync(before with { Site = listing.Reference.Site }, request, listing, readNow: true,
                    cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                Publish(before);
                throw;
            }
        });
    }

    public Task<CatalogueSnapshot> RebuildIndexAsync(IndexRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Enqueue(async () =>
        {
            var listing = await GetListingAsync(cancellationToken).ConfigureAwait(false);
            return listing is null
                ? Current
                : await BuildIndexAsync(Current, request, listing, readNow: false, cancellationToken).ConfigureAwait(false);
        });
    }

    /// <summary>The whole catalogue for browsing; null until it was read from the site once.</summary>
    /// <param name="cancellationToken">Cancels reading it back from disk.</param>
    public async Task<CatalogueListing?> GetListingAsync(CancellationToken cancellationToken)
    {
        var reference = Volatile.Read(ref _listing);
        if (reference is not null && reference.TryGetTarget(out var held))
        {
            return held;
        }

        var loaded = await cache.LoadListingAsync(cancellationToken).ConfigureAwait(false);
        if (loaded is not null)
        {
            // A refresh may have finished meanwhile; its listing is newer than the one on disk.
            Interlocked.CompareExchange(ref _listing, new WeakReference<CatalogueListing>(loaded), reference);
        }

        return loaded;
    }

    public async Task<CataloguePage?> GetProjectPageAsync(ProjectLocator project, PageFreshness freshness,
        CancellationToken cancellationToken)
    {
        var listed = Current.Index.FindProject(project.ProjectUuid)?.UpdatedAt;
        if (listed is null && freshness == PageFreshness.Current)
        {
            listed = (await GetListingAsync(cancellationToken).ConfigureAwait(false))?.FindProject(project.ProjectUuid)?.Project.UpdatedAt;
        }

        return await pages.GetAsync(project, freshness, listed, cancellationToken).ConfigureAwait(false);
    }

    // readNow tells a listing just read from the site from one kept from an earlier read.
    private async Task<CatalogueSnapshot> BuildIndexAsync(CatalogueSnapshot basis, IndexRequest request, CatalogueListing listing,
        bool readNow, CancellationToken cancellationToken)
    {
        InstallableIndex index;
        try
        {
            index = await indexSource.BuildAsync(new IndexScope(request.Games, request.InstalledProjects, listing), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (CatalogueException exception)
        {
            return Failed(basis, exception);
        }

        await SaveQuietlyAsync(() => cache.SaveIndexAsync(index, cancellationToken)).ConfigureAwait(false);
        return Publish(readNow
            ? basis with { State = CatalogueState.Fresh, Index = index, FetchedAt = listing.FetchedAt, Failure = null, RetryAfter = null }
            // An index built offline from saved pages must not hide that the last refresh failed.
            : basis with
            {
                State = basis.Failure is not null ? CatalogueState.Stale
                    : basis.State == CatalogueState.Fresh ? CatalogueState.Fresh
                    : CatalogueState.Cached,
                Index = index,
                FetchedAt = listing.FetchedAt,
            });
    }

    private CatalogueSnapshot Failed(CatalogueSnapshot basis, CatalogueException exception)
    {
        LogRefreshFailed(exception.Reason, exception);
        return Publish(basis with
        {
            State = basis.FetchedAt is null ? CatalogueState.Failed : CatalogueState.Stale,
            Failure = exception.Reason,
            RetryAfter = exception.RetryAfter,
        });
    }

    private CatalogueSnapshot Publish(CatalogueSnapshot snapshot)
    {
        Volatile.Write(ref _current, snapshot);
        foreach (var handler in Changed?.GetInvocationList() ?? [])
        {
            try
            {
                ((EventHandler<CatalogueSnapshot>)handler)(this, snapshot);
            }
            catch (Exception exception)
            {
                LogListenerFailed(exception);
            }
        }

        return snapshot;
    }

    private Task<T> Enqueue<T>(Func<Task<T>> operation)
    {
        lock (_gate)
        {
            var next = RunAfterAsync(_tail, operation);
            _tail = next;
            return next;
        }
    }

    private static async Task<T> RunAfterAsync<T>(Task previous, Func<Task<T>> operation)
    {
        // Yielding even after a finished operation keeps the next one, and the listeners it raises, out of the lock that
        // queued it. An earlier operation's failure belongs to its own caller.
        await previous.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing | ConfigureAwaitOptions.ForceYielding);
        return await operation().ConfigureAwait(false);
    }

    // The fresh data is already in hand; a full or read-only disk only costs the offline copy.
    private async Task SaveQuietlyAsync(Func<Task> save)
    {
        try
        {
            await save().ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            LogCacheNotSaved(exception);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The catalogue could not be refreshed ({Reason})")]
    private partial void LogRefreshFailed(CatalogueErrorReason reason, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The catalogue could not be saved for offline use")]
    private partial void LogCacheNotSaved(Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "A catalogue listener failed")]
    private partial void LogListenerFailed(Exception exception);
}
