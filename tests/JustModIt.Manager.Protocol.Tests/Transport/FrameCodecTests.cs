using System.Text;
using JustModIt.Manager.Protocol.Messages;
using JustModIt.Manager.Protocol.Transport;

namespace JustModIt.Manager.Protocol.Tests.Transport;

public sealed class FrameCodecTests
{
    private static readonly Dictionary<string, Frame> _frames = new()
    {
        ["hello"] = new EventFrame(new Hello(Hello.CurrentProtocolVersion, "1.2.3-beta.4", ProcessRole.Launcher, 4242)),
        ["ping"] = new RequestFrame(1, new Ping()),
        ["activate"] = new RequestFrame(2, new Activate(["--background", "example-app://catalogue/example-ąćęłńóśźż"])),
        ["bring to front"] = new EventFrame(new BringToFront()),
        ["navigate to library"] = new EventFrame(new Navigate(new LibraryTarget())),
        ["navigate to game"] = new EventFrame(new Navigate(new GameTarget("gog", "1000000001"))),
        ["navigate to catalogue"] = new EventFrame(new Navigate(new CatalogueTarget())),
        ["navigate to catalogue game"] = new EventFrame(new Navigate(new CatalogueTarget("example-game"))),
        ["navigate to settings"] = new EventFrame(new Navigate(new SettingsTarget())),
        ["navigate to creator"] = new EventFrame(new Navigate(new CreatorTarget())),
        ["shutdown"] = new EventFrame(new ShutdownRequested(ShutdownReason.Update)),
        ["empty response"] = new ResponseFrame(3),
        ["response with payload"] = new ResponseFrame(4, new Ping()),
        ["error response"] = new ResponseFrame(5, Error: new RemoteError(ErrorCode.Unsupported, "no handler")),
    };

    public static TheoryData<string> FrameNames => [.. _frames.Keys];

    [Theory]
    [MemberData(nameof(FrameNames))]
    public void Every_frame_round_trips(string name)
    {
        var frame = _frames[name];
        var payload = FrameCodec.Serialize(frame, FrameCodec.DefaultMaxFrameLength);

        var read = FrameCodec.Deserialize(payload);

        read.GetType().Should().Be(frame.GetType());
        FrameCodec.Serialize(read, FrameCodec.DefaultMaxFrameLength).Should().Equal(payload);
    }

    [Fact]
    public void Request_names_its_kind_and_message_type()
    {
        Json(new RequestFrame(7, new Activate(["--background"])))
            .Should().Be("""{"kind":"request","id":7,"payload":{"type":"activate","args":["--background"]}}""");
    }

    [Fact]
    public void Hello_carries_the_version_role_and_process()
    {
        Json(new EventFrame(new Hello(1, "0.1.0", ProcessRole.Ui, 42)))
            .Should().Be("""{"kind":"event","payload":{"type":"hello","protocolVersion":1,"appVersion":"0.1.0","role":"Ui","processId":42}}""");
    }

    [Fact]
    public void Successful_response_without_payload_is_only_an_id()
    {
        Json(new ResponseFrame(3)).Should().Be("""{"kind":"response","id":3}""");
    }

    [Fact]
    public void Failed_response_carries_only_the_error()
    {
        Json(new ResponseFrame(3, Error: new RemoteError(ErrorCode.ShuttingDown)))
            .Should().Be("""{"kind":"response","id":3,"error":{"code":"ShuttingDown"}}""");
    }

    [Fact]
    public void Navigation_target_names_its_page()
    {
        Json(new EventFrame(new Navigate(new GameTarget("steam", "480"))))
            .Should().Be("""{"kind":"event","payload":{"type":"navigate","target":{"page":"game","store":"steam","gameId":"480"}}}""");
    }

    [Fact]
    public void Frame_over_the_limit_is_not_serialized()
    {
        var frame = new RequestFrame(1, new Activate([new string('x', 200)]));

        var serialize = () => FrameCodec.Serialize(frame, maxFrameLength: 100);

        serialize.Should().Throw<InvalidOperationException>();
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("""{"id":1,"payload":{"type":"ping"}}""")]
    [InlineData("""{"kind":"upload","id":1}""")]
    [InlineData("""{"kind":"request","id":1,"payload":{"type":"formatDisk"}}""")]
    [InlineData("""{"kind":"request","id":1,"payload":{"args":[]}}""")]
    [InlineData("""{"kind":"request","id":1}""")]
    [InlineData("""{"kind":"request","id":"one","payload":{"type":"ping"}}""")]
    [InlineData("""{"kind":"request","id":1,"payload":{"type":"activate"}}""")]
    [InlineData("""{"kind":"request","id":1,"payload":{"type":"activate","args":null}}""")]
    [InlineData("""{"kind":"event","payload":{"type":"navigate","target":{"page":"uninstall"}}}""")]
    [InlineData("""{"kind":"event","payload":{"type":"shutdownRequested","reason":"Reboot"}}""")]
    [InlineData("""{"kind":"event","payload":{"type":"hello","protocolVersion":1}}""")]
    public void Malformed_payload_is_rejected_as_invalid_data(string json)
    {
        var deserialize = () => FrameCodec.Deserialize(Encoding.UTF8.GetBytes(json));

        deserialize.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void Invalid_utf8_is_rejected_as_invalid_data()
    {
        var deserialize = () => FrameCodec.Deserialize([(byte)'{', 0xC3, 0x28, (byte)'}']);

        deserialize.Should().Throw<InvalidDataException>();
    }

    private static string Json(Frame frame) =>
        Encoding.UTF8.GetString(FrameCodec.Serialize(frame, FrameCodec.DefaultMaxFrameLength));
}
