using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using JustModIt.Manager.Protocol.Messages;
using JustModIt.Manager.Protocol.Transport;
using Microsoft.Extensions.Logging;

namespace JustModIt.Manager.Protocol.Tests.Transport;

public sealed class PipeServerTests
{
    private readonly string _name = TestPipes.NewName();

    [Fact]
    public async Task Client_with_another_protocol_version_is_refused_but_learns_the_server_version()
    {
        var logger = new ListLogger();
        await using var server = TestPipes.StartServer(_name, new TestHandler(), logger: logger);
        var newer = TestPipes.UiHello with { ProtocolVersion = Hello.CurrentProtocolVersion + 1 };

        var connect = () => TestPipes.ConnectAsync(_name, newer);

        var mismatch = await connect.Should().ThrowAsync<ProtocolMismatchException>();
        mismatch.Which.Peer.Should().Be(TestPipes.AgentHello);
        await logger.WaitForAsync(entry => entry.Level == LogLevel.Warning && entry.Message.Contains("protocol", StringComparison.Ordinal));
        await using var client = await TestPipes.ConnectAsync(_name);
        (await client.RequestAsync(new Ping(), cancellationToken: TestContext.Current.CancellationToken)).Should().BeNull();
    }

    [Fact]
    public async Task Client_that_never_says_hello_is_dropped_and_frees_its_slot()
    {
        var options = TestPipes.Options with { MaxConnections = 1, HandshakeTimeout = TimeSpan.FromMilliseconds(200) };
        await using var server = TestPipes.StartServer(_name, new TestHandler(), options);
        await using var silent = await ConnectRawAsync();

        await using var client = await TestPipes.ConnectAsync(_name);

        (await client.RequestAsync(new Ping(), cancellationToken: TestContext.Current.CancellationToken)).Should().BeNull();
        (await silent.ReadAsync(new byte[1024], TestContext.Current.CancellationToken)).Should().BeGreaterThan(0, "the server's hello was sent");
    }

    [Fact]
    public async Task Peer_opening_with_nonsense_fails_the_handshake_at_once()
    {
        await using var squatter = new NamedPipeServerStream(_name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        var answering = AnswerWithNonsenseAsync();
        var options = TestPipes.Options with { HandshakeTimeout = TimeSpan.FromMinutes(1) };

        var connect = () => PipeClient
            .ConnectAsync(_name, TestPipes.UiHello, TestPipes.ConnectTimeout, options: options, cancellationToken: TestContext.Current.CancellationToken)
            .WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        await connect.Should().ThrowAsync<InvalidDataException>();
        await answering;

        async Task AnswerWithNonsenseAsync()
        {
            await squatter.WaitForConnectionAsync(TestContext.Current.CancellationToken);
            await squatter.WriteAsync(Frame(Encoding.UTF8.GetBytes("not json")), TestContext.Current.CancellationToken);
        }
    }

    public static TheoryData<string, byte[]> MalformedFrames => new()
    {
        { "not json", Frame(Encoding.UTF8.GetBytes("not json")) },
        { "unknown message", Frame(Encoding.UTF8.GetBytes("""{"kind":"request","id":1,"payload":{"type":"formatDisk"}}""")) },
        { "oversized", Header(TestPipes.Options.MaxFrameLength + 1) },
        { "negative length", Header(-5) },
        { "torn", [.. Header(100), 1, 2, 3] },
    };

    [Theory]
    [MemberData(nameof(MalformedFrames))]
    public async Task Malformed_frame_closes_only_that_connection(string name, byte[] bytes)
    {
        var logger = new ListLogger();
        var handler = new TestHandler();
        await using var server = TestPipes.StartServer(_name, handler, logger: logger);
        await using var other = await TestPipes.ConnectAsync(_name);
        await using var raw = await ConnectRawAsync();
        var reader = new FrameReader(raw, TestPipes.Options.MaxFrameLength);
        await new FrameWriter(raw).WriteAsync(
            FrameCodec.Serialize(new EventFrame(TestPipes.LauncherHello), TestPipes.Options.MaxFrameLength),
            TestContext.Current.CancellationToken);
        (await reader.ReadAsync(TestContext.Current.CancellationToken)).Should().Be(new EventFrame(TestPipes.AgentHello));

        await raw.WriteAsync(bytes, TestContext.Current.CancellationToken);
        if (name == "torn")
        {
            raw.Close();
        }
        else
        {
            (await reader.ReadAsync(TestContext.Current.CancellationToken)).Should().BeNull(name);
        }

        (await handler.Disconnected.NextAsync()).Peer.Should().Be(TestPipes.LauncherHello);
        (await other.RequestAsync(new Ping(), cancellationToken: TestContext.Current.CancellationToken)).Should().BeNull();
        logger.Entries.Should().NotContain(entry => entry.Level >= LogLevel.Error);
    }

    [Fact]
    public async Task Peer_that_stops_reading_is_disconnected_instead_of_holding_the_server()
    {
        var options = TestPipes.Options with { SendQueueCapacity = 4 };
        var handler = new TestHandler();
        await using var server = TestPipes.StartServer(_name, handler, options);
        await using var stuck = await ConnectRawAsync();
        await new FrameWriter(stuck).WriteAsync(
            FrameCodec.Serialize(new EventFrame(TestPipes.UiHello), options.MaxFrameLength),
            TestContext.Current.CancellationToken);
        await handler.Connected.NextAsync();
        var bulky = new Activate([new string('x', 64 * 1024)]);

        for (var i = 0; i < 64; i++)
        {
            server.Broadcast(bulky);
        }

        (await handler.Disconnected.NextAsync()).Peer.Should().Be(TestPipes.UiHello);
    }

    [Fact]
    public async Task Broadcast_while_a_connection_closes_leaves_its_queued_frames_to_drain()
    {
        var logger = new ListLogger();
        var handler = new TestHandler();
        await using var server = TestPipes.StartServer(_name, handler, logger: logger);
        await using var raw = await ConnectRawAsync();
        var reader = new FrameReader(raw, TestPipes.Options.MaxFrameLength);
        await new FrameWriter(raw).WriteAsync(
            FrameCodec.Serialize(new EventFrame(TestPipes.UiHello), TestPipes.Options.MaxFrameLength),
            TestContext.Current.CancellationToken);
        (await reader.ReadAsync(TestContext.Current.CancellationToken)).Should().Be(new EventFrame(TestPipes.AgentHello));
        var accepted = await handler.Connected.NextAsync();
        var bulky = new Activate([new string('x', 64 * 1024)]);
        for (var i = 0; i < 4; i++)
        {
            server.Broadcast(bulky);
        }

        var closing = accepted.DisposeAsync();
        server.Broadcast(new BringToFront());

        for (var i = 0; i < 4; i++)
        {
            (await reader.ReadAsync(TestContext.Current.CancellationToken))
                .Should().BeOfType<EventFrame>().Which.Payload.Should().BeOfType<Activate>();
        }

        (await reader.ReadAsync(TestContext.Current.CancellationToken)).Should().BeNull();
        await closing;
        logger.Entries.Should().NotContain(entry => entry.Level >= LogLevel.Warning);
    }

    [Fact]
    public async Task Taken_pipe_name_makes_the_server_back_off_and_recover_once_it_is_free()
    {
        var logger = new ListLogger();
        var squatter = new NamedPipeServerStream(_name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        await using var server = TestPipes.StartServer(_name, new TestHandler(), logger: logger);

        await Task.Delay(TimeSpan.FromMilliseconds(500), TestContext.Current.CancellationToken);
        await squatter.DisposeAsync();
        await using var client = await TestPipes.ConnectAsync(_name);

        await logger.WaitForAsync(entry => entry.Message.StartsWith("Listening on the IPC pipe again", StringComparison.Ordinal));
        var failures = logger.Entries.Count(entry => entry.Exception is IOException);
        failures.Should().BeInRange(2, 20, "each failed attempt waits longer than the last");
        logger.Entries.Should().ContainSingle(entry => entry.Level == LogLevel.Warning);
    }

    [Fact]
    [SupportedOSPlatform("windows")]
    public async Task Pipe_the_user_may_not_join_is_retried_rather_than_ending_the_listener()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Pipe security descriptors exist only on Windows.");
        var logger = new ListLogger();
        var security = new PipeSecurity();
        security.AddAccessRule(new PipeAccessRule(WindowsIdentity.GetCurrent().User!, PipeAccessRights.ReadWrite, AccessControlType.Allow));
        var squatter = NamedPipeServerStreamAcl.Create(
            _name,
            PipeDirection.InOut,
            NamedPipeServerStream.MaxAllowedServerInstances,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous,
            0,
            0,
            security);
        await using var server = TestPipes.StartServer(_name, new TestHandler(), logger: logger);

        await logger.WaitForAsync(entry => entry.Exception is UnauthorizedAccessException);
        await squatter.DisposeAsync();
        await using var client = await TestPipes.ConnectAsync(_name);

        (await client.RequestAsync(new Ping(), cancellationToken: TestContext.Current.CancellationToken)).Should().BeNull();
    }

    [Fact]
    public async Task Connecting_without_a_server_gives_up_after_the_timeout()
    {
        var stopwatch = Stopwatch.StartNew();

        var connect = () => PipeClient.ConnectAsync(
            _name,
            TestPipes.UiHello,
            TimeSpan.FromMilliseconds(300),
            options: TestPipes.Options,
            cancellationToken: TestContext.Current.CancellationToken);

        await connect.Should().ThrowAsync<TimeoutException>();
        stopwatch.Elapsed.Should().BeGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(250)).And.BeLessThan(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Server_started_after_the_client_is_found_within_the_timeout()
    {
        var connect = TestPipes.ConnectAsync(_name);
        await Task.Delay(TimeSpan.FromMilliseconds(200), TestContext.Current.CancellationToken);

        await using var server = TestPipes.StartServer(_name, new TestHandler());

        await using var client = await connect;
        client.Peer.Should().Be(TestPipes.AgentHello);
    }

    private async Task<NamedPipeClientStream> ConnectRawAsync()
    {
        var stream = new NamedPipeClientStream(".", _name, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await stream.ConnectAsync(10_000, TestContext.Current.CancellationToken);
        return stream;
    }

    private static byte[] Header(int length)
    {
        var header = new byte[FrameCodec.HeaderLength];
        BinaryPrimitives.WriteInt32LittleEndian(header, length);
        return header;
    }

    private static byte[] Frame(byte[] payload) => [.. Header(payload.Length), .. payload];
}
