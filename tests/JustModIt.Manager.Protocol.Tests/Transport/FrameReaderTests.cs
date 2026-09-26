using System.Buffers.Binary;
using System.Text;
using JustModIt.Manager.Protocol.Messages;
using JustModIt.Manager.Protocol.Transport;

namespace JustModIt.Manager.Protocol.Tests.Transport;

public sealed class FrameReaderTests
{
    private const int MaxFrameLength = 1024;

    [Fact]
    public async Task Reads_written_frames_in_order_then_reports_the_end()
    {
        using var stream = new MemoryStream();
        var writer = new FrameWriter(stream);
        await writer.WriteAsync(FrameCodec.Serialize(new RequestFrame(1, new Ping()), MaxFrameLength), TestContext.Current.CancellationToken);
        await writer.WriteAsync(FrameCodec.Serialize(new EventFrame(new BringToFront()), MaxFrameLength), TestContext.Current.CancellationToken);
        stream.Position = 0;
        var reader = new FrameReader(stream, MaxFrameLength);

        var first = await reader.ReadAsync(TestContext.Current.CancellationToken);
        var second = await reader.ReadAsync(TestContext.Current.CancellationToken);
        var end = await reader.ReadAsync(TestContext.Current.CancellationToken);

        first.Should().Be(new RequestFrame(1, new Ping()));
        second.Should().Be(new EventFrame(new BringToFront()));
        end.Should().BeNull();
    }

    [Fact]
    public async Task Header_is_little_endian()
    {
        var payload = Encoding.UTF8.GetBytes("""{"kind":"event","payload":{"type":"bringToFront"}}""");
        byte[] bytes = [(byte)payload.Length, 0, 0, 0, .. payload];

        var frame = await ReadAsync(bytes);

        frame.Should().Be(new EventFrame(new BringToFront()));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public async Task Stream_ending_inside_the_header_is_torn(int headerBytes)
    {
        var read = () => ReadAsync(Header(10).AsSpan(0, headerBytes).ToArray());

        await read.Should().ThrowAsync<EndOfStreamException>();
    }

    [Fact]
    public async Task Stream_ending_inside_the_payload_is_torn()
    {
        var payload = Encoding.UTF8.GetBytes("""{"kind":"request","id":1,"payload":{"type":"ping"}}""");
        byte[] bytes = [.. Header(payload.Length), .. payload.AsSpan(0, payload.Length - 1)];

        var read = () => ReadAsync(bytes);

        await read.Should().ThrowAsync<EndOfStreamException>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(MaxFrameLength + 1)]
    [InlineData(int.MaxValue)]
    public async Task Length_outside_the_limit_is_rejected_before_the_payload_is_read(int length)
    {
        using var stream = new MemoryStream([.. Header(length), .. new byte[16]]);
        var reader = new FrameReader(stream, MaxFrameLength);

        var read = async () => await reader.ReadAsync(TestContext.Current.CancellationToken);

        await read.Should().ThrowAsync<InvalidDataException>();
        stream.Position.Should().Be(FrameCodec.HeaderLength);
    }

    [Fact]
    public async Task Malformed_payload_is_rejected()
    {
        var payload = Encoding.UTF8.GetBytes("{\"kind\":");

        var read = () => ReadAsync([.. Header(payload.Length), .. payload]);

        await read.Should().ThrowAsync<InvalidDataException>();
    }

    private static byte[] Header(int length)
    {
        var header = new byte[FrameCodec.HeaderLength];
        BinaryPrimitives.WriteInt32LittleEndian(header, length);
        return header;
    }

    private static async Task<Frame?> ReadAsync(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        return await new FrameReader(stream, MaxFrameLength).ReadAsync(TestContext.Current.CancellationToken);
    }
}
