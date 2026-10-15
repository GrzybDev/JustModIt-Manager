using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using JustModIt.Manager.Core.IO;

namespace JustModIt.Manager.Core.Catalog;

/// <summary>
/// Talks to the site's API without cookies, on paths in the exact form the backend routes, trailing slash included,
/// since it redirects other forms and a redirect would turn a counted request into a second one.
/// </summary>
public sealed class CatalogueApiClient : ICatalogueApi, IDisposable
{
    // The largest page the site serves; a backend that does not know the parameter serves its own size.
    private const string GamesPageSize = "&page_size=48";

    private static readonly MediaTypeWithQualityHeaderValue JsonMediaType = new("application/json");
    private static readonly string[] NeededScopes = ["catalog.read", "site.read", "releases.download"];

    // The site counts one download of a release per installation with it; nothing else carries it.
    private const string InstallationHeader = "X-JustModIt-Installation";

    private readonly HttpClient _http;
    private readonly Uri _apiAddress;
    private readonly bool _hasKey;
    private readonly ApiKeyStatus _keyStatus = new();
    private readonly TimeProvider _time;
    private readonly Func<Guid>? _installation;

    /// <summary>Creates a client with pooled connections of its own.</summary>
    /// <param name="options">The API's address, the app's name for the User-Agent and the optional key.</param>
    /// <param name="time">The clock for attempt timeouts, retry delays and Retry-After dates.</param>
    /// <param name="connection">Replaces the network connection, e.g. in tests.</param>
    /// <param name="installation">The id of this installation of the app, sent only with counted downloads; null sends none.</param>
    public CatalogueApiClient(CatalogueApiOptions options, TimeProvider time, HttpMessageHandler? connection = null,
        Func<Guid>? installation = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(time);
        _apiAddress = options.ApiAddress;
        _hasKey = options.ApiKey is not null;
        _time = time;
        _installation = installation;
        _http = CatalogueHttp.CreateApiClient(options, _keyStatus, time, connection);
    }

    /// <summary>Completes the first time the site refuses the API key, which is dropped then; without a key, never.</summary>
    public Task<ApiKeyRefusal> KeyRefused => _keyStatus.Refused;

    private static CatalogueJsonContext Json => CatalogueJsonContext.Default;

    public Task<ApiPage<CatalogueGame>> GetGamesAsync(int page, CancellationToken cancellationToken) =>
        GetAsync("games/v1/" + PageQuery(page) + GamesPageSize, Json.ApiPageCatalogueGame, cancellationToken);

    public Task<ApiPage<GameDownloads>> GetGameDownloadsAsync(int page, CancellationToken cancellationToken) =>
        GetAsync("games/v1/" + PageQuery(page) + GamesPageSize + "&ordering=popular", Json.ApiPageGameDownloads, cancellationToken);

    public Task<IReadOnlyList<ProjectSummary>> GetGameProjectsAsync(string gameSlug, CancellationToken cancellationToken) =>
        GetAsync($"games/v1/{Segment(gameSlug)}/projects/", Json.IReadOnlyListProjectSummary, cancellationToken);

    public async Task<ProjectPageDocument> GetProjectPageAsync(string gameSlug, string teamSlug,
        CancellationToken cancellationToken)
    {
        var path = $"games/v1/{Segment(gameSlug)}/projects/{Segment(teamSlug)}/";
        var json = await GetBytesAsync(path, counted: false, cancellationToken).ConfigureAwait(false);
        return Parse(path, () => ProjectPageDocument.Parse(json));
    }

    // Without the previews of each team's projects, which the app does not read.
    public Task<ApiPage<Team>> GetTeamsAsync(int page, CancellationToken cancellationToken) =>
        GetAsync("teams/v1/" + PageQuery(page) + "&previews=0", Json.ApiPageTeam, cancellationToken);

    public Task<IReadOnlyList<ProjectStatus>> GetStatusesAsync(CancellationToken cancellationToken) =>
        GetAsync("projects/v1/statuses/", Json.IReadOnlyListProjectStatus, cancellationToken);

    public Task<IReadOnlyList<ProjectType>> GetProjectTypesAsync(CancellationToken cancellationToken) =>
        GetAsync("projects/v1/types/", Json.IReadOnlyListProjectType, cancellationToken);

    public Task<IReadOnlyList<Genre>> GetGenresAsync(CancellationToken cancellationToken) =>
        GetAsync("games/v1/genres/", Json.IReadOnlyListGenre, cancellationToken);

    public Task<IReadOnlyList<GamePlatform>> GetGamePlatformsAsync(CancellationToken cancellationToken) =>
        GetAsync("games/v1/platforms/", Json.IReadOnlyListGamePlatform, cancellationToken);

    public Task<IReadOnlyList<StorefrontPlatform>> GetStorefrontPlatformsAsync(CancellationToken cancellationToken) =>
        GetAsync("projects/v1/storefront-platforms/", Json.IReadOnlyListStorefrontPlatform, cancellationToken);

    public async Task<SiteConfiguration> GetSiteConfigurationAsync(CancellationToken cancellationToken)
    {
        // The only route without a trailing slash.
        const string path = "system/v1/config";
        var root = await GetAsync(path, Json.JsonElement, cancellationToken).ConfigureAwait(false);
        return root.ValueKind == JsonValueKind.Object
            ? SiteConfiguration.Read(root)
            : throw new CatalogueException(CatalogueErrorReason.InvalidResponse, $"GET {path} returned a JSON {root.ValueKind}.");
    }

    public async Task<Uri> ResolveDownloadAsync(ReleaseFileLocator file, CancellationToken cancellationToken)
    {
        var path = $"{ReleasePath(file.Release)}files/{file.FileUuid:D}/download/";
        var json = await GetDownloadAsync(path, cancellationToken).ConfigureAwait(false);
        return AddressOf(path, Parse(path, () => JsonSerializer.Deserialize(json, Json.DownloadLink)).DownloadUrl);
    }

    public async Task<IReadOnlyDictionary<Guid, Uri>> ResolveDownloadsAsync(ReleaseLocator release, IReadOnlyList<Guid> files,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(files);
        if (files.Count == 0)
        {
            throw new ArgumentException("At least one file is needed.", nameof(files));
        }

        var path = $"{ReleasePath(release)}download/?files={string.Join(',', files.Select(file => file.ToString("D")))}";
        var json = await GetDownloadAsync(path, cancellationToken).ConfigureAwait(false);
        var links = Parse(path, () => JsonSerializer.Deserialize(json, Json.ReleaseDownloadLinks)).Files
            .GroupBy(link => link.Uuid)
            .ToDictionary(link => link.Key, link => link.First().DownloadUrl);
        return files.ToDictionary(file => file, file => links.TryGetValue(file, out var address)
            ? AddressOf(path, address)
            : throw new CatalogueException(CatalogueErrorReason.InvalidResponse, $"GET {path} returned no address for file {file:D}."));
    }

    /// <summary>
    /// Drops the API key when the site says it lacks a scope the app needs; a site that cannot say leaves the key, since
    /// every request still drops it when refused.
    /// </summary>
    /// <param name="cancellationToken">Cancels the check.</param>
    public async Task CheckKeyAsync(CancellationToken cancellationToken)
    {
        if (!_hasKey || _keyStatus.IsRefused)
        {
            return;
        }

        ApiKeyDetails details;
        try
        {
            details = await GetAsync("api-keys/v1/self/", Json.ApiKeyDetails, cancellationToken).ConfigureAwait(false);
        }
        catch (CatalogueException)
        {
            return;
        }

        if (!NeededScopes.All(details.Scopes.Contains))
        {
            _keyStatus.Refuse(ApiKeyRefusal.NoAccess);
        }
    }

    public void Dispose() => _http.Dispose();

    /// <summary>
    /// Escapes a slug for a path, refusing a blank one or dots alone before anything is sent, since they would change the
    /// path's meaning.
    /// </summary>
    /// <param name="slug">A game's, team's or project's slug, which may be Unicode.</param>
    internal static string Segment(string slug)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slug);
        if (slug.Trim('.').Length == 0)
        {
            throw new ArgumentException("A slug cannot consist of dots only.", nameof(slug));
        }

        return Uri.EscapeDataString(slug);
    }

    private static string ReleasePath(ReleaseLocator release) =>
        $"projects/v1/teams/{Segment(release.TeamSlug)}/{Segment(release.ProjectSlug)}/releases/{release.ReleaseUuid:D}/";

    // The site counts these requests, and answers for a release nobody may download any more as for one that is gone.
    private async Task<byte[]> GetDownloadAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            return await GetBytesAsync(path, counted: true, cancellationToken).ConfigureAwait(false);
        }
        catch (CatalogueException exception) when (exception.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound)
        {
            throw new CatalogueException(CatalogueErrorReason.ReleaseUnavailable, exception.Message, exception, exception.StatusCode);
        }
    }

    // The bytes are verified against a checksum that comes over the same connection, so only TLS protects them; plain http
    // is accepted only from a development backend that is itself plain http.
    private Uri AddressOf(string path, string address) =>
        Uri.TryCreate(address, UriKind.Absolute, out var url)
        && (url.Scheme == Uri.UriSchemeHttps || (url.Scheme == Uri.UriSchemeHttp && _apiAddress.Scheme == Uri.UriSchemeHttp))
            ? url
            : throw new CatalogueException(CatalogueErrorReason.InvalidResponse, $"GET {path} returned an unusable download address.");

    private static string PageQuery(int page)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(page, 1);
        return "?page=" + page.ToString(CultureInfo.InvariantCulture);
    }

    private static T Parse<T>(string path, Func<T?> parse)
    {
        try
        {
            return parse() ?? throw new CatalogueException(CatalogueErrorReason.InvalidResponse, $"GET {path} returned null.");
        }
        catch (JsonException exception)
        {
            throw new CatalogueException(CatalogueErrorReason.InvalidResponse, $"GET {path} returned malformed JSON.", exception);
        }
    }

    private async Task<T> GetAsync<T>(string path, JsonTypeInfo<T> type, CancellationToken cancellationToken)
    {
        var json = await GetBytesAsync(path, counted: false, cancellationToken).ConfigureAwait(false);
        return Parse(path, () => JsonSerializer.Deserialize(json, type));
    }

    private async Task<byte[]> GetBytesAsync(string path, bool counted, CancellationToken cancellationToken)
    {
        var operation = "GET " + path;
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(_apiAddress, path));
        request.Headers.Accept.Add(JsonMediaType);
        if (counted)
        {
            CatalogueHttp.MarkCounted(request);
            if (_installation is not null)
            {
                request.Headers.Add(InstallationHeader, _installation().ToString("D"));
            }
        }

        try
        {
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                var body = await CatalogueErrors.ReadJsonAsync(response.Content, cancellationToken).ConfigureAwait(false);
                throw CatalogueErrors.ForStatus(response, operation, _time, DetailOf(body),
                    body is { ValueKind: JsonValueKind.Object } error ? error.GetTextOrNull("code") : null);
            }

            // Cloudflare answers for a failing origin with its own HTML page, sometimes with a success status.
            if (!CatalogueErrors.IsJson(response.Content))
            {
                throw new CatalogueException(CatalogueErrorReason.ServerError,
                    $"{operation} returned {response.Content.Headers.ContentType?.MediaType ?? "no content type"} instead of JSON.",
                    statusCode: response.StatusCode);
            }

            return await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (CatalogueErrors.ForTransport(exception, operation, cancellationToken) is { } error)
        {
            throw error;
        }
    }

    private static string? DetailOf(JsonElement? body) =>
        body is { } error ? (error.ValueKind == JsonValueKind.Object ? error.GetTextOrNull("detail") : null) ?? error.GetRawText() : null;
}
