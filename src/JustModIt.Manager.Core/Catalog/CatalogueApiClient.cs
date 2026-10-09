using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace JustModIt.Manager.Core.Catalog;

/// <summary>
/// Talks to the site's API without cookies, on paths in the exact form the backend routes, trailing slash included,
/// since it redirects other forms and a redirect would turn a counted request into a second one.
/// </summary>
public sealed class CatalogueApiClient : ICatalogueApi, IDisposable
{
    private static readonly MediaTypeWithQualityHeaderValue JsonMediaType = new("application/json");

    private readonly HttpClient _http;
    private readonly Uri _apiAddress;
    private readonly TimeProvider _time;

    /// <summary>Creates a client with pooled connections of its own.</summary>
    /// <param name="options">The API's address, the app's name for the User-Agent and the optional key.</param>
    /// <param name="time">The clock for attempt timeouts, retry delays and Retry-After dates.</param>
    /// <param name="connection">Replaces the network connection, e.g. in tests.</param>
    public CatalogueApiClient(CatalogueApiOptions options, TimeProvider time, HttpMessageHandler? connection = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(time);
        _apiAddress = options.ApiAddress;
        _time = time;
        _http = CatalogueHttp.CreateApiClient(options, time, connection);
    }

    private static CatalogueJsonContext Json => CatalogueJsonContext.Default;

    public Task<ApiPage<CatalogueGame>> GetGamesAsync(int page, CancellationToken cancellationToken) =>
        GetAsync("games/v1/" + PageQuery(page), Json.ApiPageCatalogueGame, cancellationToken);

    public Task<IReadOnlyList<ProjectSummary>> GetGameProjectsAsync(string gameSlug, CancellationToken cancellationToken) =>
        GetAsync($"games/v1/{Segment(gameSlug)}/projects/", Json.IReadOnlyListProjectSummary, cancellationToken);

    public async Task<ProjectPageDocument> GetProjectPageAsync(string gameSlug, string teamSlug,
        CancellationToken cancellationToken)
    {
        var path = $"games/v1/{Segment(gameSlug)}/projects/{Segment(teamSlug)}/";
        var json = await GetBytesAsync(path, counted: false, cancellationToken).ConfigureAwait(false);
        return Parse(path, () => ProjectPageDocument.Parse(json));
    }

    public Task<ApiPage<Team>> GetTeamsAsync(int page, CancellationToken cancellationToken) =>
        GetAsync("teams/v1/" + PageQuery(page), Json.ApiPageTeam, cancellationToken);

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
        var path = $"projects/v1/teams/{Segment(file.TeamSlug)}/{Segment(file.ProjectSlug)}/releases/{file.ReleaseUuid:D}/files/{file.FileUuid:D}/download/";
        byte[] json;
        try
        {
            json = await GetBytesAsync(path, counted: true, cancellationToken).ConfigureAwait(false);
        }
        catch (CatalogueException exception) when (exception.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound)
        {
            throw new CatalogueException(CatalogueErrorReason.ReleaseUnavailable, exception.Message, exception, exception.StatusCode);
        }

        var link = Parse(path, () => JsonSerializer.Deserialize(json, Json.DownloadLink));
        // The bytes are verified against a checksum that comes over the same connection, so only TLS protects them;
        // plain http is accepted only from a development backend that is itself plain http.
        return Uri.TryCreate(link.DownloadUrl, UriKind.Absolute, out var url)
            && (url.Scheme == Uri.UriSchemeHttps || (url.Scheme == Uri.UriSchemeHttp && _apiAddress.Scheme == Uri.UriSchemeHttp))
                ? url
                : throw new CatalogueException(CatalogueErrorReason.InvalidResponse, $"GET {path} returned an unusable download address.");
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
        }

        try
        {
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                throw CatalogueErrors.ForStatus(response, operation, _time,
                    await ReadDetailAsync(response.Content, cancellationToken).ConfigureAwait(false));
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

    private static async Task<string?> ReadDetailAsync(HttpContent content, CancellationToken cancellationToken)
    {
        if (!CatalogueErrors.IsJson(content))
        {
            return null;
        }

        try
        {
            var body = JsonSerializer.Deserialize(await content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false),
                Json.JsonElement);
            return body.ValueKind == JsonValueKind.Object && body.TryGetProperty("detail", out var detail)
                && detail.ValueKind == JsonValueKind.String
                    ? detail.GetString()
                    : body.GetRawText();
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
