using System.Net;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;

namespace JustModIt.Manager.Core.Catalog;

/// <summary>
/// Repeats idempotent API requests that failed in a way that may pass, such as the network, a timeout, a busy server or a
/// Cloudflare page, timing each attempt over the whole body so that a response stopping halfway is repeated too.
/// </summary>
/// <param name="time">The clock for attempt timeouts and retry delays.</param>
internal sealed class CatalogueRetryHandler(TimeProvider time) : DelegatingHandler
{
    public const int MaxAttempts = 3;
    public const long MaxResponseLength = 32 * 1024 * 1024;

    public static readonly TimeSpan AttemptTimeout = TimeSpan.FromSeconds(15);
    public static readonly TimeSpan FirstDelay = TimeSpan.FromSeconds(1);

    /// <summary>A server asking for a longer pause gets its answer passed on, so the caller comes back later instead.</summary>
    public static readonly TimeSpan MaxRetryAfter = TimeSpan.FromSeconds(30);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var repeatable = (request.Method == HttpMethod.Get || request.Method == HttpMethod.Head) && !CatalogueHttp.IsCounted(request);
        for (var attempt = 1; ; attempt++)
        {
            var (response, failure) = await AttemptAsync(request, cancellationToken).ConfigureAwait(false);
            var another = attempt < MaxAttempts;
            TimeSpan delay;
            if (failure is not null)
            {
                // The server counts every request that reached it, so a counted one is repeated only when it never left.
                if (!another || !(repeatable || NeverSent(failure)))
                {
                    ExceptionDispatchInfo.Throw(failure);
                }

                delay = Backoff(attempt);
            }
            else
            {
                if (!another || !repeatable || !IsTransient(response!) || RetryDelay(response!, attempt) is not { } wait)
                {
                    return response!;
                }

                response!.Dispose();
                delay = wait;
            }

            await Task.Delay(delay, time, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<(HttpResponseMessage? Response, Exception? Failure)> AttemptAsync(HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        using var timeout = new CancellationTokenSource(AttemptTimeout, time);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        HttpResponseMessage? response = null;
        try
        {
            response = await base.SendAsync(request, linked.Token).ConfigureAwait(false);
            await response.Content.LoadIntoBufferAsync(MaxResponseLength, linked.Token).ConfigureAwait(false);
            return (response, null);
        }
        catch (OperationCanceledException exception) when (timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            response?.Dispose();
            return (null, new TimeoutException($"No complete response arrived within {AttemptTimeout.TotalSeconds} s.", exception));
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException && !cancellationToken.IsCancellationRequested)
        {
            response?.Dispose();
            return (null, exception);
        }
        catch
        {
            response?.Dispose();
            throw;
        }
    }

    private static bool NeverSent(Exception failure) =>
        failure is HttpRequestException { HttpRequestError: HttpRequestError.NameResolutionError or HttpRequestError.ConnectionError };

    private static bool IsTransient(HttpResponseMessage response) =>
        response.StatusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500
        || (response.IsSuccessStatusCode && !CatalogueErrors.IsJson(response.Content));

    private TimeSpan? RetryDelay(HttpResponseMessage response, int attempt) =>
        CatalogueErrors.RetryAfter(response, time) is { } asked
            ? asked <= MaxRetryAfter ? asked : null
            : Backoff(attempt);

    // Jitter keeps many clients that failed together from coming back in the same instant.
    private static TimeSpan Backoff(int attempt) =>
        FirstDelay * Math.Pow(2, attempt - 1) * (0.8 + (RandomNumberGenerator.GetInt32(401) / 1000.0));
}
