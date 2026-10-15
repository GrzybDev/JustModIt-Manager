using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace JustModIt.Manager.Core.Tests.Catalog.TestSupport;

internal sealed record SentRequest(
    HttpMethod Method,
    Uri Uri,
    string Accept,
    string UserAgent,
    AuthenticationHeaderValue? Authorization,
    RangeHeaderValue? Range,
    RangeConditionHeaderValue? IfRange,
    bool HasCookie,
    string? Installation)
{
    public string PathAndQuery => Uri.PathAndQuery;
}

/// <summary>Stands in for the network: answers each request from a function and remembers what was sent.</summary>
/// <param name="respond">Gives the response to each request.</param>
internal sealed class FakeHttpHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
{
    private readonly ConcurrentQueue<SentRequest> _requests = new();

    public FakeHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
        : this((request, _) => Task.FromResult(respond(request)))
    {
    }

    public IReadOnlyList<SentRequest> Requests => [.. _requests];

    public IEnumerable<string> Paths => Requests.Select(request => request.PathAndQuery);

    /// <summary>Answers by path and query; anything else gets the HTML 404 page the backend sends for unknown routes.</summary>
    /// <param name="routes">The responses, keyed by path and query.</param>
    public static FakeHttpHandler Routes(IReadOnlyDictionary<string, Func<HttpResponseMessage>> routes) =>
        new(request => routes.TryGetValue(request.RequestUri!.PathAndQuery, out var route)
            ? route()
            : Text(HttpStatusCode.NotFound, "<h1>Not Found</h1>", "text/html"));

    /// <summary>Gives the responses in order, one per request, and fails the test when they run out.</summary>
    /// <param name="responses">One response per expected request.</param>
    public static FakeHttpHandler Sequence(params Func<HttpResponseMessage>[] responses)
    {
        var next = 0;
        return new FakeHttpHandler(_ =>
        {
            var index = Interlocked.Increment(ref next) - 1;
            return index < responses.Length
                ? responses[index]()
                : throw new InvalidOperationException($"Request {index + 1} was not expected.");
        });
    }

    public static FakeHttpHandler Failing(Func<Exception> failure) =>
        new((_, _) => Task.FromException<HttpResponseMessage>(failure()));

    public static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        Text(status, json, "application/json");

    public static HttpResponseMessage Text(HttpStatusCode status, string content, string mediaType) =>
        new(status) { Content = new StringContent(content, Encoding.UTF8, mediaType) };

    /// <summary>Headers whose body never arrives, like a connection that died silently.</summary>
    /// <param name="mediaType">The content type the headers announce.</param>
    public static HttpResponseMessage Stalled(string mediaType = "application/json") =>
        new(HttpStatusCode.OK)
        {
            Content = new StreamContent(new StalledStream()) { Headers = { ContentType = new MediaTypeHeaderValue(mediaType) } },
        };

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _requests.Enqueue(new SentRequest(request.Method, request.RequestUri!, request.Headers.Accept.ToString(),
            request.Headers.UserAgent.ToString(), request.Headers.Authorization, request.Headers.Range, request.Headers.IfRange,
            request.Headers.Contains("Cookie"),
            request.Headers.TryGetValues("X-JustModIt-Installation", out var installation) ? string.Join(",", installation) : null));
        var response = await respond(request, cancellationToken);
        response.RequestMessage ??= request;
        return response;
    }

    internal sealed class StalledStream : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return 0;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
