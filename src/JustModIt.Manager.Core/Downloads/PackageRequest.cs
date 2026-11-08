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
    /// <summary>The file this one copies with the same bytes, whose kept download serves for it; null for none.</summary>
    public Guid? CopiedFrom { get; init; }

    /// <summary>
    /// A kept download of the install being replaced that serves for this file though its bytes may differ: a file this one
    /// replaces, or this file's own archive from before its layout; null for none.
    /// </summary>
    public Guid? HeldDownload { get; init; }

    /// <summary>The held download is a shared object, not what a browser gets.</summary>
    public bool HeldIsSharedObject { get; init; }

    /// <summary>
    /// The bytes wanted are the file's shared object, to install with its layout's rule, not what a browser gets, which
    /// the uuid names too and which an install without a rule needs.
    /// </summary>
    public bool IsSharedObject { get; init; }

    /// <summary>The request for a release file, with what the catalogue says of the bytes the app gets for it.</summary>
    /// <param name="locator">Names the file for the download endpoint.</param>
    /// <param name="file">The file as the catalogue describes it.</param>
    /// <exception cref="ArgumentException">The file is in a format the app does not install.</exception>
    public static PackageRequest For(ReleaseFileLocator locator, ReleaseFile file)
    {
        var format = file.Format ?? throw new ArgumentException($"Release file {file.Uuid:D} is in no format the app installs.", nameof(file));
        // Beside a layout, the file's own size and hashes describe what a browser gets.
        return new PackageRequest(locator, format, file.DownloadSize, file.Layout is null ? file.Md5.ToLowerInvariant() : "",
            (file.Layout?.ObjectSha256 ?? file.Sha256).ToLowerInvariant())
        {
            CopiedFrom = file.CopiedFrom,
            IsSharedObject = file.Layout is not null,
        };
    }
}

/// <summary>Files of one release that one install takes, the base's first.</summary>
/// <param name="Release">The release the files belong to.</param>
/// <param name="Files">The files in the order they install.</param>
/// <param name="ResolveTogether">
/// The site gives the addresses of a release's files in one request; a backend that does not has only the endpoint of a
/// single file.
/// </param>
public sealed record ReleaseDownload(ReleaseLocator Release, IReadOnlyList<PackageRequest> Files, bool ResolveTogether);

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
public sealed record DownloadedPackage(string Path, long Size, string Md5, string? Sha256, PackageFormat Format, bool FromCache)
{
    /// <summary>The held or copied file whose kept download served; null when the package holds the bytes the request describes.</summary>
    public Guid? CopyOf { get; init; }
}

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

    /// <summary>
    /// The verified packages of a release's files, each reused, resumed or downloaded in order; the files that have no
    /// address saved are asked for in one request, which the site counts once when it includes a base file, and every
    /// address is saved, the first file's first, before any byte is fetched.
    /// </summary>
    /// <param name="download">The release and its files.</param>
    /// <param name="progress">Receives the bytes done and the total of the files to fetch; null for none.</param>
    /// <param name="cancellationToken">Stops the downloads, keeping the partial files for a resume.</param>
    /// <returns>The packages in the order of the files.</returns>
    /// <exception cref="Catalog.CatalogueException">A package could not be downloaded or failed verification.</exception>
    Task<IReadOnlyList<DownloadedPackage>> DownloadReleaseAsync(ReleaseDownload download, IProgress<DownloadProgress>? progress,
        CancellationToken cancellationToken);

    /// <summary>
    /// The held download, the verified package if it was downloaded before, or the original's when the file is a copy of
    /// the same size; asks nothing of the site.
    /// </summary>
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
