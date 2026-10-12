using JustModIt.Manager.Core;
using JustModIt.Manager.Core.Catalog;
using JustModIt.Manager.Core.IO;
using JustModIt.Manager.Core.Jobs;
using JustModIt.Manager.Protocol.Messages;
using Microsoft.Extensions.Logging;

namespace JustModIt.Manager.Agent.Storage;

/// <summary>Measures the app's own folders and clears what the app can fetch again.</summary>
/// <param name="locations">The app's folders.</param>
/// <param name="pages">Where the saved project pages are.</param>
/// <param name="jobs">Deletes downloaded packages while no job runs.</param>
/// <param name="logger">Logs files that could not be deleted.</param>
public sealed partial class AppStorage(AppDataLocations locations, CatalogueCache pages, JobRunner jobs, ILogger<AppStorage> logger)
{
    // Links are not followed, so a folder linked in from elsewhere is neither counted nor emptied.
    private static readonly EnumerationOptions AllFiles = new()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.ReparsePoint,
    };

    /// <summary>How much space the app's folders take, read file by file.</summary>
    public AppDataUsage Measure()
    {
        var data = locations.DataDirectory;
        // Only on Windows do the cache and the logs lie inside the data folder.
        var total = Size(data) + new[] { locations.CacheDirectory, locations.LogsDirectory }
            .Where(folder => !PathComparison.IsWithin(data, folder)).Sum(Size);
        return new AppDataUsage(data, total, Size(locations.DownloadsDirectory),
            Size(locations.ImageCacheDirectory) + Size(locations.CatalogueCacheDirectory), Size(locations.LogsDirectory));
    }

    /// <summary>Deletes the window's images, the saved project pages and the downloaded packages that installs do not need.</summary>
    /// <param name="installed">The release file ids of the installs' packages, which stay.</param>
    /// <param name="cancellationToken">Stops deleting.</param>
    /// <returns>False when a job runs, and nothing was deleted.</returns>
    public async Task<bool> ClearCacheAsync(IReadOnlySet<Guid> installed, CancellationToken cancellationToken)
    {
        if (!await jobs.ClearDownloadsAsync(installed, cancellationToken).ConfigureAwait(false))
        {
            return false;
        }

        DeleteFiles(locations.ImageCacheDirectory, cancellationToken);
        DeleteFiles(pages.PagesDirectory, cancellationToken);
        return true;
    }

    private static long Size(string folder) =>
        Directory.Exists(folder) ? new DirectoryInfo(folder).EnumerateFiles("*", AllFiles).Sum(Length) : 0;

    // A folder's listing gives the size a file had when it was last closed, so a file kept open, as the logs are, would
    // count as empty; the file itself tells its size now.
    private static long Length(FileInfo file)
    {
        file.Refresh();
        return file.Exists ? file.Length : 0;
    }

    // A file in use, e.g. an image the window is writing, stays; it is as current as one fetched again would be.
    private void DeleteFiles(string folder, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(folder))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(folder, "*", AllFiles))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                File.Delete(file);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                LogNotDeleted(Path.GetFileName(file), exception.GetType().Name);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "The cached file {File} could not be deleted: {Error}")]
    private partial void LogNotDeleted(string file, string error);
}
