using System.Threading.Channels;
using JustModIt.Manager.Protocol.Messages;
using JustModIt.Manager.Protocol.Transport;

namespace JustModIt.Manager.Protocol.Tests.Transport;

public sealed class ReconnectingPipeClientTests
{
    private readonly string _name = TestPipes.NewName();
    private readonly ListLogger _logger = new();
    private readonly Channel<ConnectionState> _states = Channel.CreateUnbounded<ConnectionState>();

    [Fact]
    public async Task Connects_once_the_agent_starts_listening()
    {
        await using var client = StartClient();
        (await _states.NextAsync()).Should().Be(ConnectionState.Connecting);

        await using var server = TestPipes.StartServer(_name, new TestHandler());

        (await _states.NextAsync()).Should().Be(ConnectionState.Connected);
        client.State.Should().Be(ConnectionState.Connected);
        client.Peer.Should().Be(TestPipes.AgentHello);
        (await client.RequestAsync(new Ping(), cancellationToken: TestContext.Current.CancellationToken)).Should().BeNull();
    }

    [Fact]
    public async Task Posts_events_to_the_agent_while_connected_and_drops_them_while_not()
    {
        await using var client = StartClient();
        client.Post(new WindowActivity(IsActive: true)).Should().BeFalse();

        var handler = new TestHandler();
        await using var server = TestPipes.StartServer(_name, handler);
        await _states.ExpectAsync(ConnectionState.Connecting, ConnectionState.Connected);

        client.Post(new WindowActivity(IsActive: true)).Should().BeTrue();
        (await handler.Events.NextAsync()).Should().Be(new WindowActivity(IsActive: true));
    }

    [Fact]
    public async Task Reconnects_after_the_agent_restarts_and_keeps_subscriptions()
    {
        var received = Channel.CreateUnbounded<Navigate>();
        await using var client = StartClient();
        using var subscription = client.Subscribe<Navigate>(message => received.Writer.TryWrite(message));
        var firstHandler = new TestHandler();
        var first = TestPipes.StartServer(_name, firstHandler);
        await _states.ExpectAsync(ConnectionState.Connecting, ConnectionState.Connected);
        var firstConnection = await firstHandler.Connected.NextAsync();
        firstConnection.Post(new Navigate(new SettingsTarget()));
        (await received.NextAsync()).Should().Be(new Navigate(new SettingsTarget()));

        await first.DisposeAsync();
        await _states.ExpectAsync(ConnectionState.Connecting);
        var whileDown = () => client.RequestAsync(new Ping(), cancellationToken: TestContext.Current.CancellationToken);
        await whileDown.Should().ThrowAsync<ConnectionClosedException>();

        var secondHandler = new TestHandler();
        await using var second = TestPipes.StartServer(_name, secondHandler);
        await _states.ExpectAsync(ConnectionState.Connected);
        var secondConnection = await secondHandler.Connected.NextAsync();
        secondConnection.Post(new Navigate(new LibraryTarget()));
        (await received.NextAsync()).Should().Be(new Navigate(new LibraryTarget()));
    }

    [Fact]
    public async Task Subscriptions_receive_only_their_message_type_until_disposed()
    {
        var navigations = Channel.CreateUnbounded<Navigate>();
        var fronts = Channel.CreateUnbounded<BringToFront>();
        var handler = new TestHandler();
        await using var server = TestPipes.StartServer(_name, handler);
        await using var client = StartClient();
        var navigateSubscription = client.Subscribe<Navigate>(message => navigations.Writer.TryWrite(message));
        using var frontSubscription = client.Subscribe<BringToFront>(message => fronts.Writer.TryWrite(message));
        var accepted = await handler.Connected.NextAsync();

        accepted.Post(new Navigate(new CreatorTarget()));
        accepted.Post(new BringToFront());
        (await navigations.NextAsync()).Should().Be(new Navigate(new CreatorTarget()));
        (await fronts.NextAsync()).Should().Be(new BringToFront());

        navigateSubscription.Dispose();
        accepted.Post(new Navigate(new SettingsTarget()));
        accepted.Post(new BringToFront());
        (await fronts.NextAsync()).Should().Be(new BringToFront());
        navigations.Reader.TryRead(out _).Should().BeFalse();
    }

    [Fact]
    public async Task Failing_subscriber_does_not_stop_the_others()
    {
        var received = Channel.CreateUnbounded<BringToFront>();
        var handler = new TestHandler();
        await using var server = TestPipes.StartServer(_name, handler);
        await using var client = StartClient();
        using var failing = client.Subscribe<BringToFront>(_ => throw new InvalidOperationException("subscriber bug"));
        using var working = client.Subscribe<BringToFront>(message => received.Writer.TryWrite(message));
        var accepted = await handler.Connected.NextAsync();

        accepted.Post(new BringToFront());
        accepted.Post(new BringToFront());

        (await received.NextAsync()).Should().Be(new BringToFront());
        (await received.NextAsync()).Should().Be(new BringToFront());
        client.State.Should().Be(ConnectionState.Connected);
    }

    [Fact]
    public async Task Agent_request_is_answered_by_the_request_handler()
    {
        var handler = new TestHandler();
        await using var server = TestPipes.StartServer(_name, handler);
        await using var client = StartClient((request, _) => ValueTask.FromResult<Message?>(
            request is ShutdownRequested ? throw new RequestFailedException(ErrorCode.Failed, "unsaved work") : new BringToFront()));
        var accepted = await handler.Connected.NextAsync();

        var shutdown = () => accepted.RequestAsync(new ShutdownRequested(ShutdownReason.Update), cancellationToken: TestContext.Current.CancellationToken);
        var answer = await accepted.RequestAsync(new Activate([]), cancellationToken: TestContext.Current.CancellationToken);

        (await shutdown.Should().ThrowAsync<RequestFailedException>()).Which.Detail.Should().Be("unsaved work");
        answer.Should().Be(new BringToFront());
    }

    [Fact]
    public async Task Agent_request_without_a_request_handler_is_unsupported()
    {
        var handler = new TestHandler();
        await using var server = TestPipes.StartServer(_name, handler);
        await using var client = StartClient();
        var accepted = await handler.Connected.NextAsync();

        var request = () => accepted.RequestAsync(new Activate([]), cancellationToken: TestContext.Current.CancellationToken);

        (await request.Should().ThrowAsync<RequestFailedException>()).Which.Code.Should().Be(ErrorCode.Unsupported);
    }

    [Fact]
    public async Task Agent_with_another_protocol_is_reported_until_a_compatible_one_answers()
    {
        var newer = TestPipes.AgentHello with { ProtocolVersion = Hello.CurrentProtocolVersion + 1 };
        var incompatible = TestPipes.StartServer(_name, new TestHandler(), hello: newer);
        await using var client = StartClient();

        await _states.ExpectAsync(ConnectionState.Connecting, ConnectionState.Incompatible);
        await incompatible.DisposeAsync();
        await using var compatible = TestPipes.StartServer(_name, new TestHandler());

        await _states.ExpectAsync(ConnectionState.Connected);
        _logger.Entries.Should().ContainSingle(entry => entry.Message.Contains("waiting for it to restart", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Missing_agent_is_retried_with_growing_delays()
    {
        await using var client = StartClient();

        await Task.Delay(TimeSpan.FromMilliseconds(500), TestContext.Current.CancellationToken);

        _logger.Entries.Count(entry => entry.Message == "Connecting to the IPC pipe failed")
            .Should().BeInRange(2, 20, "each failed attempt waits longer than the last");
        client.State.Should().Be(ConnectionState.Connecting);
    }

    [Fact]
    public async Task Disposing_stops_the_client_for_good()
    {
        var handler = new TestHandler();
        await using var server = TestPipes.StartServer(_name, handler);
        var client = StartClient();
        await _states.ExpectAsync(ConnectionState.Connecting, ConnectionState.Connected);

        await client.DisposeAsync();

        await _states.ExpectAsync(ConnectionState.Stopped);
        await handler.Disconnected.NextAsync();
        var restart = client.Start;
        restart.Should().Throw<InvalidOperationException>();
    }

    private ReconnectingPipeClient StartClient(Func<Message, CancellationToken, ValueTask<Message?>>? handleRequest = null)
    {
        var client = new ReconnectingPipeClient(_name, TestPipes.UiHello, _logger, TestPipes.Options, handleRequest);
        client.StateChanged += (_, state) => _states.Writer.TryWrite(state);
        client.Start();
        return client;
    }
}
