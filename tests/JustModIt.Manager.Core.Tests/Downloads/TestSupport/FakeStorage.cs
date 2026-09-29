using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;

namespace JustModIt.Manager.Core.Tests.Downloads.TestSupport;

internal sealed record StorageRequest(Uri Uri, RangeHeaderValue? Range, RangeConditionHeaderValue? IfRange);

/// <summary>
/// Serves one object like the site's storage: a strong ETag with the MD5 of the bytes, and ranges honoured while
/// If-Range still names the stored object.
/// </summary>
internal sealed class FakeStorage : HttpMessageHandler
{
    private readonly ConcurrentQueue<StorageRequest> _requests = new();
    private readonly ConcurrentQueue<Func<HttpResponseMessage?>> _next = new();
    private int _running;

    public FakeStorage(byte[] content) => Store(content);

    public byte[] Content { get; private set; } = [];

    public string ETag { get; set; } = "";

    public IReadOnlyList<StorageRequest> Requests => [.. _requests];

    /// <summary>Makes the next response end with a dropped connection after this many bytes of its body.</summary>
    public int? DropAfter { get; set; }

    /// <summary>Makes the next response stop sending after this many bytes of its body, without closing.</summary>
    public int? StallAfter { get; set; }

    /// <summary>Holds every response until released, to see what waits for a running download.</summary>
    public TaskCompletionSource? Gate { get; set; }

    /// <summary>The most requests that were served at the same time.</summary>
    public int MostRunning { get; private set; }

    public void Store(byte[] content)
    {
        Content = content;
        ETag = $"\"{Md5Of(content)}\"";
    }

    /// <summary>Answers the next request with <paramref name="respond"/> instead of the object.</summary>
    /// <param name="respond">Gives the response, or null to serve the object after all.</param>
    public void Next(Func<HttpResponseMessage?> respond) => _next.Enqueue(respond);

    public static string Md5Of(byte[] content)
    {
        using var md5 = IncrementalHash.CreateHash(HashAlgorithmName.MD5);
        md5.AppendData(content);
        return Convert.ToHexStringLower(md5.GetHashAndReset());
    }

    /// <summary>Gives pseudo-random bytes that are the same for every call with the same length.</summary>
    /// <param name="length">How many bytes to give; it also seeds them.</param>
    public static byte[] Bytes(int length)
    {
        var bytes = new byte[length];
        new Random(length).NextBytes(bytes);
        return bytes;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        _requests.Enqueue(new StorageRequest(request.RequestUri!, request.Headers.Range, request.Headers.IfRange));
        var running = Interlocked.Increment(ref _running);
        MostRunning = Math.Max(MostRunning, running);
        try
        {
            if (Gate is { } gate)
            {
                await gate.Task.WaitAsync(cancellationToken);
            }

            if (_next.TryDequeue(out var respond) && respond() is { } overridden)
            {
                return overridden;
            }

            return Serve(request);
        }
        finally
        {
            Interlocked.Decrement(ref _running);
        }
    }

    private HttpResponseMessage Serve(HttpRequestMessage request)
    {
        long from = 0;
        var partial = false;
        if (request.Headers.Range?.Ranges.FirstOrDefault()?.From is { } start
            && (request.Headers.IfRange?.EntityTag is not { } tag || tag.Tag == ETag))
        {
            if (start >= Content.Length)
            {
                return new HttpResponseMessage(HttpStatusCode.RequestedRangeNotSatisfiable) { Content = new ByteArrayContent([]) };
            }

            from = start;
            partial = true;
        }

        var body = new BodyStream(Content, (int)from, DropAfter, StallAfter);
        DropAfter = null;
        StallAfter = null;
        var response = new HttpResponseMessage(partial ? HttpStatusCode.PartialContent : HttpStatusCode.OK)
        {
            Content = new StreamContent(body),
        };
        response.Content.Headers.ContentLength = Content.Length - from;
        response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
        if (partial)
        {
            response.Content.Headers.ContentRange = new ContentRangeHeaderValue(from, Content.Length - 1, Content.Length);
        }

        response.Headers.ETag = EntityTagHeaderValue.Parse(ETag);
        response.Headers.AcceptRanges.Add("bytes");
        return response;
    }

    private sealed class BodyStream(byte[] content, int start, int? dropAfter, int? stallAfter) : Stream
    {
        private int _sent;

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
            var limit = content.Length - start;
            if (dropAfter is { } drop && _sent >= drop)
            {
                throw new IOException("The connection was reset.");
            }

            if (stallAfter is { } stall && _sent >= stall)
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }

            var cap = Math.Min(limit, Math.Min(dropAfter ?? int.MaxValue, stallAfter ?? int.MaxValue));
            var count = Math.Min(buffer.Length, Math.Min(64 * 1024, cap - _sent));
            if (count <= 0)
            {
                return 0;
            }

            content.AsMemory(start + _sent, count).CopyTo(buffer);
            _sent += count;
            return count;
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
