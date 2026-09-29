using System.Buffers;
using System.Net;
using System.Net.Http.Headers;

namespace JustModIt.Manager.Core.Catalog;

/// <summary>The HTTP clients that talk to the site: one for its API, one for the storage that serves release files.</summary>
public static class CatalogueHttp
{
    /// <summary>The site is read in bursts hours apart, so an idle connection is not kept with its socket and TLS state.</summary>
    public static readonly TimeSpan IdleConnectionTimeout = TimeSpan.FromSeconds(15);

    private static readonly HttpRequestOptionsKey<bool> CountedKey = new("JustModIt.Manager.Counted");

    private static readonly SearchValues<char> TokenCharacters =
        SearchValues.Create("!#$%&'*+-.^_`|~0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz");

    /// <summary>
    /// A client for the storage, without the API key, a total timeout (the downloader watches for stalls instead) or
    /// decompression (ranges address the stored bytes).
    /// </summary>
    /// <param name="options">Gives the User-Agent.</param>
    /// <param name="connection">Replaces the network connection, e.g. in tests.</param>
    public static HttpClient CreateStorageClient(CatalogueApiOptions options, HttpMessageHandler? connection = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        var client = new HttpClient(connection ?? CreateConnectionHandler(DecompressionMethods.None))
        {
            Timeout = Timeout.InfiniteTimeSpan,
        };
        AddUserAgent(client, options);
        return client;
    }

    internal static HttpClient CreateApiClient(CatalogueApiOptions options, TimeProvider time, HttpMessageHandler? connection)
    {
        HttpMessageHandler handler = new CatalogueRetryHandler(time)
        {
            InnerHandler = connection ?? CreateConnectionHandler(DecompressionMethods.All),
        };
        if (options.ApiKey is { } key)
        {
            handler = new ApiKeyHandler(key) { InnerHandler = handler };
        }

        // The retry handler times every attempt; a total timeout here would cut its retries short.
        var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        AddUserAgent(client, options);
        return client;
    }

    /// <summary>Marks a request that the server counts, e.g. as a download, so it is never repeated once it may have arrived.</summary>
    /// <param name="request">The request to mark before it is sent.</param>
    internal static void MarkCounted(HttpRequestMessage request) => request.Options.Set(CountedKey, true);

    /// <summary>Whether the request was marked as one the server counts.</summary>
    /// <param name="request">The request about to be sent or repeated.</param>
    internal static bool IsCounted(HttpRequestMessage request) => request.Options.TryGetValue(CountedKey, out var counted) && counted;

    // The clients live as long as the app, so pooled connections are recycled to follow DNS changes.
    private static SocketsHttpHandler CreateConnectionHandler(DecompressionMethods decompression) => new()
    {
        // A session cookie sent back would make the API enforce CSRF checks and change what it returns.
        UseCookies = false,
        AutomaticDecompression = decompression,
        PooledConnectionLifetime = TimeSpan.FromMinutes(15),
        PooledConnectionIdleTimeout = IdleConnectionTimeout,
        ConnectTimeout = TimeSpan.FromSeconds(30),
    };

    private static void AddUserAgent(HttpClient client, CatalogueApiOptions options)
    {
        var agent = client.DefaultRequestHeaders.UserAgent;
        agent.Add(new ProductInfoHeaderValue(Token(options.ProductName, "Client"), Token(options.ProductVersion.Split('+')[0], "0")));
        if (options.SiteAddress is { } site)
        {
            agent.Add(new ProductInfoHeaderValue($"(+{site.AbsoluteUri})"));
        }
    }

    private static string Token(string text, string fallback)
    {
        var token = new string([.. text.Where(TokenCharacters.Contains)]);
        return token.Length > 0 ? token : fallback;
    }
}
