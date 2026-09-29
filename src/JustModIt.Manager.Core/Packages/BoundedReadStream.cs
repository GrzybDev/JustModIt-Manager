namespace JustModIt.Manager.Core.Packages;

/// <summary>
/// Yields exactly the size an archive entry declares, which the size limits and the free-space check trusted, so an entry
/// that inflates past it (a zip bomb) or ends early (corruption) fails instead.
/// </summary>
/// <param name="inner">The entry's decompressing stream, disposed with this one.</param>
/// <param name="length">The entry's declared size in bytes.</param>
/// <param name="entryName">Names the entry in the error.</param>
internal sealed class BoundedReadStream(Stream inner, long length, string entryName) : Stream
{
    private readonly long _length = length;
    private long _remaining = length;

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => _length;

    public override long Position
    {
        get => _length - _remaining;
        set => throw new NotSupportedException();
    }

    public override int Read(Span<byte> buffer)
    {
        if (buffer.IsEmpty)
        {
            return 0;
        }

        if (_remaining == 0)
        {
            Span<byte> probe = stackalloc byte[1];
            return inner.Read(probe) == 0 ? 0 : throw Mismatch();
        }

        return Consume(inner.Read(buffer[..Allowed(buffer.Length)]));
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (buffer.IsEmpty)
        {
            return 0;
        }

        if (_remaining == 0)
        {
            var probe = new byte[1];
            return await inner.ReadAsync(probe, cancellationToken).ConfigureAwait(false) == 0 ? 0 : throw Mismatch();
        }

        return Consume(await inner.ReadAsync(buffer[..Allowed(buffer.Length)], cancellationToken).ConfigureAwait(false));
    }

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    public override async ValueTask DisposeAsync()
    {
        await inner.DisposeAsync().ConfigureAwait(false);
        await base.DisposeAsync().ConfigureAwait(false);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            inner.Dispose();
        }

        base.Dispose(disposing);
    }

    private int Allowed(int requested) => (int)Math.Min(requested, _remaining);

    private int Consume(int read)
    {
        if (read == 0)
        {
            throw Mismatch();
        }

        _remaining -= read;
        return read;
    }

    private InvalidPackageException Mismatch() => new(PackageError.PayloadMismatch, entryName);
}
