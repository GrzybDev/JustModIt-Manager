using JustModIt.Manager.Core.Catalog;

namespace JustModIt.Manager.Core.Downloads;

/// <summary>A release file to download, with what the catalogue says about it for verifying the bytes.</summary>
/// <param name="Locator">Names the file for the download endpoint.</param>
/// <param name="Format">How the app installs the package, which gives its file's extension.</param>
/// <param name="Size">In bytes; null when the catalogue does not say.</param>
/// <param name="Md5">Lower-case hex, or empty when the catalogue does not say; the storage's ETag serves then.</param>
/// <param name="Sha256">Lower-case hex, or empty when the catalogue does not say.</param>
public sealed record PackageRequest(ReleaseFileLocator Locator, PackageFormat Format, long? Size, string Md5 = "", string Sha256 = "")
{
    /// <summary>The request for a release file, with the catalogue's size and hashes.</summary>
    /// <param name="locator">Names the file for the download endpoint.</param>
    /// <param name="file">The file as the catalogue describes it.</param>
    /// <exception cref="ArgumentException">The file is in a format the app does not install.</exception>
    public static PackageRequest For(ReleaseFileLocator locator, ReleaseFile file)
    {
        ArgumentNullException.ThrowIfNull(file);
        var format = file.Format ?? throw new ArgumentException($"Release file {file.Uuid:D} is in no format the app installs.", nameof(file));
        return new PackageRequest(locator, format, file.Size, file.Md5.ToLowerInvariant(), file.Sha256.ToLowerInvariant());
    }
}

/// <summary>How far a download got.</summary>
/// <param name="BytesReceived">The bytes on disk so far, a resumed part included.</param>
/// <param name="TotalBytes">The package's size; null when unknown.</param>
public readonly record struct DownloadProgress(long BytesReceived, long? TotalBytes);

/// <summary>A verified package in the downloads folder.</summary>
/// <param name="Path">The package's full path.</param>
/// <param name="Size">In bytes.</param>
/// <param name="Md5">Lower-case hex of the bytes.</param>
/// <param name="Sha256">Null when the catalogue gave none to verify against.</param>
/// <param name="Format">How the app installs the package.</param>
/// <param name="FromCache">An earlier verified download was reused without asking the site, which counts nothing.</param>
public sealed record DownloadedPackage(string Path, long Size, string Md5, string? Sha256, PackageFormat Format, bool FromCache);

public interface IPackageDownloader
{
    /// <summary>
    /// The verified package, downloaded or reused, which the site counts only when the bytes are fetched anew; a download
    /// cut short resumes later without counting again.
    /// </summary>
    /// <param name="request">The file and what to verify it against.</param>
    /// <param name="progress">Receives the bytes done and the total; null for none.</param>
    /// <param name="cancellationToken">Stops the download, keeping the partial file for a resume.</param>
    /// <exception cref="Catalog.CatalogueException">The package could not be downloaded or failed verification.</exception>
    Task<DownloadedPackage> DownloadAsync(PackageRequest request, IProgress<DownloadProgress>? progress,
        CancellationToken cancellationToken);

    /// <summary>The verified package if it was downloaded before; asks nothing of the site.</summary>
    /// <param name="request">The file and what to verify it against.</param>
    /// <param name="cancellationToken">Cancels the check.</param>
    Task<DownloadedPackage?> FindCachedAsync(PackageRequest request, CancellationToken cancellationToken);

    /// <summary>Deletes every download except the kept ones.</summary>
    /// <param name="keep">The file uuids whose downloads stay, e.g. of installed projects and of jobs tried again.</param>
    /// <param name="keepPartial">Partial downloads that a later attempt may still resume stay too.</param>
    /// <param name="cancellationToken">Stops the pruning.</param>
    /// <returns>How many files were deleted.</returns>
    Task<int> PruneAsync(IReadOnlySet<Guid> keep, bool keepPartial, CancellationToken cancellationToken);
}
