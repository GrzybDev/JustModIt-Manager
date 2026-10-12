using System.Runtime.InteropServices;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using JustModIt.Manager.Core.IO;
using Microsoft.Extensions.Logging;

namespace JustModIt.Manager.Core.Catalog;

/// <summary>A project page as saved, with when it was fetched.</summary>
/// <param name="Document">The page with the JSON the site sent.</param>
/// <param name="FetchedAt">When the page was read from the site.</param>
public sealed record CachedProjectPage(ProjectPageDocument Document, DateTimeOffset FetchedAt);

/// <summary>
/// The catalogue data last read from the site, each file with its format version and fetch time, kept so the app works
/// offline: a file that is missing, damaged or of another format version reads as absent and is fetched again, and one
/// that cannot be saved is only logged, since the data is in hand.
/// </summary>
/// <param name="directory">The catalogue's cache folder.</param>
/// <param name="time">The clock for retrying a file the system holds briefly.</param>
/// <param name="logger">Logs files that could not be read or saved.</param>
public sealed partial class CatalogueCache(string directory, TimeProvider time, ILogger<CatalogueCache> logger)
{
    private const int ListingVersion = 1;
    private const int IndexVersion = 1;
    private const int SiteVersion = 1;
    private const int PageVersion = 1;

    private const string VersionProperty = "version";
    private const string FetchedAtProperty = "fetched_at";
    private const string DataProperty = "data";
    private const long MaxLength = 256L * 1024 * 1024;

    private static readonly JsonWriterOptions WriteOptions = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private static CatalogueJsonContext Json => CatalogueJsonContext.Default;

    private string ListingFile => Path.Combine(directory, "listing.json");

    private string IndexFile => Path.Combine(directory, "index.json");

    private string SiteFile => Path.Combine(directory, "site.json");

    /// <summary>The folder of the saved project pages, each of which is read from the site again when it is gone.</summary>
    public string PagesDirectory => Path.Combine(directory, "projects");

    /// <summary>The saved catalogue listing; null when there is none to read.</summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    public async Task<CatalogueListing?> LoadListingAsync(CancellationToken cancellationToken) =>
        await ReadAsync(ListingFile, ListingVersion, Json.ListingData, cancellationToken).ConfigureAwait(false) is (var fetchedAt, var data)
            ? new CatalogueListing(fetchedAt, data.Games, data.Reference)
            : null;

    /// <summary>Saves the catalogue listing in place of the last one.</summary>
    /// <param name="listing">The listing just read from the site.</param>
    /// <param name="cancellationToken">Cancels the write, leaving the last listing.</param>
    public Task SaveListingAsync(CatalogueListing listing, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(listing);
        return WriteAsync(ListingFile, ListingVersion, listing.FetchedAt,
            writer => JsonSerializer.Serialize(writer, new ListingData(listing.Games, listing.Reference), Json.ListingData),
            cancellationToken);
    }

    /// <summary>The saved installable index; null when there is none to read.</summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    public async Task<InstallableIndex?> LoadIndexAsync(CancellationToken cancellationToken) =>
        await ReadAsync(IndexFile, IndexVersion, Json.IndexData, cancellationToken).ConfigureAwait(false) is (var builtAt, var data)
            ? new InstallableIndex(builtAt, data.Entries)
            : null;

    /// <summary>Saves the installable index in place of the last one.</summary>
    /// <param name="index">The index just built.</param>
    /// <param name="cancellationToken">Cancels the write, leaving the last index.</param>
    public Task SaveIndexAsync(InstallableIndex index, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(index);
        return WriteAsync(IndexFile, IndexVersion, index.BuiltAt,
            writer => JsonSerializer.Serialize(writer, new IndexData(index.Entries), Json.IndexData), cancellationToken);
    }

    /// <summary>The saved site configuration; null when there is none to read.</summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    public async Task<SiteConfiguration?> LoadSiteAsync(CancellationToken cancellationToken) =>
        await ReadAsync(SiteFile, SiteVersion, Json.SiteConfiguration, cancellationToken).ConfigureAwait(false) is (_, var site)
            ? site
            : null;

    /// <summary>Saves the site configuration in place of the last one.</summary>
    /// <param name="site">The configuration as the site published it.</param>
    /// <param name="fetchedAt">When it was read from the site.</param>
    /// <param name="cancellationToken">Cancels the write, leaving the last configuration.</param>
    public Task SaveSiteAsync(SiteConfiguration site, DateTimeOffset fetchedAt, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(site);
        return WriteAsync(SiteFile, SiteVersion, fetchedAt, writer => JsonSerializer.Serialize(writer, site, Json.SiteConfiguration),
            cancellationToken);
    }

    /// <summary>A saved project page; null when there is none to read or it shows another project.</summary>
    /// <param name="projectUuid">The project's id, which names the file.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    public async Task<CachedProjectPage?> LoadPageAsync(Guid projectUuid, CancellationToken cancellationToken)
    {
        var path = PageFile(projectUuid);
        if (await ReadAsync(path, PageVersion, Json.JsonElement, cancellationToken).ConfigureAwait(false) is not (var fetchedAt, var data))
        {
            return null;
        }

        try
        {
            var page = ProjectPageDocument.Parse(JsonMarshal.GetRawUtf8Value(data).ToArray());
            return page.Page.Uuid == projectUuid ? new CachedProjectPage(page, fetchedAt) : null;
        }
        catch (JsonException exception)
        {
            LogUnreadable(Path.GetFileName(path), exception);
            return null;
        }
    }

    /// <summary>Saves the page as the site sent it, so fields this version of the app does not know are kept.</summary>
    /// <param name="page">The page just read from the site.</param>
    /// <param name="fetchedAt">When it was read.</param>
    /// <param name="cancellationToken">Cancels the write, leaving the page saved earlier.</param>
    public Task SavePageAsync(ProjectPageDocument page, DateTimeOffset fetchedAt, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(page);
        return WriteAsync(PageFile(page.Page.Uuid), PageVersion, fetchedAt, writer => writer.WriteRawValue(page.Json.Span),
            cancellationToken);
    }

    private string PageFile(Guid projectUuid) => Path.Combine(PagesDirectory, $"{projectUuid:D}.json");

    private async Task WriteAsync(string path, int version, DateTimeOffset fetchedAt, Action<Utf8JsonWriter> writeData,
        CancellationToken cancellationToken)
    {
        try
        {
            await AtomicFile.WriteJsonAsync(path, WriteOptions, writer =>
            {
                writer.WriteStartObject();
                writer.WriteNumber(VersionProperty, version);
                writer.WriteString(FetchedAtProperty, fetchedAt);
                writer.WritePropertyName(DataProperty);
                writeData(writer);
                writer.WriteEndObject();
            }, cancellationToken, time).ConfigureAwait(false);
        }
        // A full or read-only disk only costs the offline copy.
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            LogNotSaved(Path.GetFileName(path), exception);
        }
    }

    private async Task<(DateTimeOffset FetchedAt, T Data)?> ReadAsync<T>(string path, int version, JsonTypeInfo<T> type,
        CancellationToken cancellationToken)
    {
        byte[] content;
        try
        {
            content = await SharedFileRead.ReadAllBytesAsync(path, MaxLength, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            return null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            LogUnreadable(Path.GetFileName(path), exception);
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(content);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || root.GetOrNull(VersionProperty, JsonValueKind.Number) is not { } stored
                || !stored.TryGetInt32(out var storedVersion) || storedVersion != version
                || root.GetDateTimeOffsetOrNull(FetchedAtProperty) is not { } fetchedAt
                || !root.TryGetProperty(DataProperty, out var data))
            {
                LogOutdated(Path.GetFileName(path));
                return null;
            }

            return data.Deserialize(type) is { } value ? (fetchedAt, value) : null;
        }
        catch (JsonException exception)
        {
            LogUnreadable(Path.GetFileName(path), exception);
            return null;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Cached {File} could not be read, so it is fetched again")]
    private partial void LogUnreadable(string file, Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "Cached {File} has another format, so it is fetched again")]
    private partial void LogOutdated(string file);

    [LoggerMessage(Level = LogLevel.Warning, Message = "{File} could not be saved for offline use")]
    private partial void LogNotSaved(string file, Exception exception);
}

/// <summary>The data of <c>listing.json</c>.</summary>
/// <param name="Games">The listed games with their projects.</param>
/// <param name="Reference">The site's reference lists.</param>
internal sealed record ListingData(IReadOnlyList<ListedGame> Games, CatalogueReference Reference);

/// <summary>The data of <c>index.json</c>.</summary>
/// <param name="Entries">The index's projects.</param>
internal sealed record IndexData(IReadOnlyList<IndexEntry> Entries);
