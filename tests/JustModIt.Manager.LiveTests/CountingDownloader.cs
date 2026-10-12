using JustModIt.Manager.Core.Downloads;

namespace JustModIt.Manager.LiveTests;

/// <summary>Counts the downloads of packages that were not kept yet, which is at least what the site counts.</summary>
/// <param name="inner">The downloader that does the work.</param>
internal sealed class CountingDownloader(IPackageDownloader inner) : IPackageDownloader
{
    private int _fromSite;

    public int FromSite => Volatile.Read(ref _fromSite);

    public async Task<DownloadedPackage> DownloadAsync(PackageRequest request, IProgress<DownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (await inner.FindCachedAsync(request, cancellationToken).ConfigureAwait(false) is { } cached)
        {
            return cached;
        }

        Interlocked.Increment(ref _fromSite);
        return await inner.DownloadAsync(request, progress, cancellationToken).ConfigureAwait(false);
    }

    public Task<DownloadedPackage?> FindCachedAsync(PackageRequest request, CancellationToken cancellationToken) =>
        inner.FindCachedAsync(request, cancellationToken);

    public Task<int> PruneAsync(IReadOnlySet<Guid> keep, bool keepPartial, CancellationToken cancellationToken) =>
        inner.PruneAsync(keep, keepPartial, cancellationToken);
}
