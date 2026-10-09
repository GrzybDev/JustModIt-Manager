using System.Net;
using System.Text.Json;

namespace JustModIt.Manager.Core.Catalog;

/// <summary>Turns failed requests into typed catalogue errors.</summary>
internal static class CatalogueErrors
{
    /// <summary>The error for a response with a failure status.</summary>
    /// <param name="response">The response, whose Retry-After is kept.</param>
    /// <param name="operation">The request as the logs name it, e.g. <c>GET games/v1/</c>.</param>
    /// <param name="time">The clock that turns a Retry-After date into a delay.</param>
    /// <param name="serverDetail">The server's own explanation, in the site's language; it goes to the logs only.</param>
    public static CatalogueException ForStatus(HttpResponseMessage response, string operation, TimeProvider time,
        string? serverDetail = null)
    {
        var status = response.StatusCode;
        var reason = status switch
        {
            HttpStatusCode.NotFound or HttpStatusCode.Gone => CatalogueErrorReason.NotFound,
            HttpStatusCode.TooManyRequests => CatalogueErrorReason.RateLimited,
            HttpStatusCode.ServiceUnavailable => CatalogueErrorReason.Maintenance,
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => CatalogueErrorReason.Forbidden,
            HttpStatusCode.BadRequest or HttpStatusCode.RequestedRangeNotSatisfiable => CatalogueErrorReason.BadRequest,
            HttpStatusCode.RequestTimeout or HttpStatusCode.GatewayTimeout => CatalogueErrorReason.Timeout,
            _ => CatalogueErrorReason.ServerError,
        };
        var message = serverDetail is null
            ? $"{operation} failed with HTTP {(int)status}."
            : $"{operation} failed with HTTP {(int)status}: {serverDetail}";
        return new CatalogueException(reason, message, statusCode: status, retryAfter: RetryAfter(response, time));
    }

    /// <summary>The error for a request that failed before a response arrived.</summary>
    /// <param name="exception">What the request threw.</param>
    /// <param name="operation">The request as the logs name it, e.g. <c>GET games/v1/</c>.</param>
    /// <param name="cancellationToken">The caller's token, whose own cancellation is no error.</param>
    /// <returns>Null when the exception is the caller's own cancellation or not a transport failure.</returns>
    public static CatalogueException? ForTransport(Exception exception, string operation, CancellationToken cancellationToken) =>
        exception switch
        {
            OperationCanceledException when cancellationToken.IsCancellationRequested => null,
            OperationCanceledException or TimeoutException =>
                new CatalogueException(CatalogueErrorReason.Timeout, $"{operation} timed out.", exception),
            HttpRequestException or IOException =>
                new CatalogueException(CatalogueErrorReason.Offline, $"{operation} could not reach the server.", exception),
            _ => null,
        };

    /// <summary>How long the server asks to wait before trying again; null when it does not say.</summary>
    /// <param name="response">The response whose Retry-After header is read.</param>
    /// <param name="time">The clock that turns a date into a delay.</param>
    public static TimeSpan? RetryAfter(HttpResponseMessage response, TimeProvider time)
    {
        var header = response.Headers.RetryAfter;
        var delay = header?.Delta ?? header?.Date - time.GetUtcNow();
        return delay is { } value ? TimeSpan.FromTicks(Math.Max(0, value.Ticks)) : null;
    }

    /// <summary>The JSON of an error's body; null when the body is not JSON.</summary>
    /// <param name="content">The failed response's content.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    public static async Task<JsonElement?> ReadJsonAsync(HttpContent content, CancellationToken cancellationToken)
    {
        if (!IsJson(content))
        {
            return null;
        }

        try
        {
            using var body = JsonDocument.Parse(await content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false));
            return body.RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Whether the content is JSON, judged by its media type.</summary>
    /// <param name="content">A response's content.</param>
    public static bool IsJson(HttpContent content) =>
        content.Headers.ContentType?.MediaType is { } mediaType
        && (mediaType.Equals("application/json", StringComparison.OrdinalIgnoreCase)
            || mediaType.EndsWith("+json", StringComparison.OrdinalIgnoreCase));
}
