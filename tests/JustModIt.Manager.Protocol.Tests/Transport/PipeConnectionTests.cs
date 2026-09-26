using JustModIt.Manager.Protocol.Messages;
using JustModIt.Manager.Protocol.Transport;

namespace JustModIt.Manager.Protocol.Tests.Transport;

public sealed class PipeConnectionTests
{
    private readonly string _name = TestPipes.NewName();

    [Fact]
    public async Task Handshake_tells_each_side_who_the_peer_is()
    {
        var handler = new TestHandler();
        await using var server = TestPipes.StartServer(_name, handler);

        await using var client = await TestPipes.ConnectAsync(_name);
        var accepted = await handler.Connected.NextAsync();

        client.Peer.Should().Be(TestPipes.AgentHello);
        accepted.Peer.Should().Be(TestPipes.UiHello);
    }

    [Fact]
    public async Task Request_gets_its_response()
    {
        var handler = new TestHandler
        {
            OnRequest = (_, request, _) => ValueTask.FromResult<Message?>(
                request is Activate activate ? new Navigate(new CatalogueTarget(activate.Args[0])) : null),
        };
        await using var server = TestPipes.StartServer(_name, handler);
        await using var client = await TestPipes.ConnectAsync(_name);

        var pong = await client.RequestAsync(new Ping(), cancellationToken: TestContext.Current.CancellationToken);
        var answer = await client.RequestAsync(new Activate(["example-game"]), cancellationToken: TestContext.Current.CancellationToken);

        pong.Should().BeNull();
        answer.Should().Be(new Navigate(new CatalogueTarget("example-game")));
    }

    [Fact]
    public async Task Typed_error_reaches_the_requester()
    {
        var handler = new TestHandler
        {
            OnRequest = (_, _, _) => throw new RequestFailedException(ErrorCode.ShuttingDown, "exiting"),
        };
        await using var server = TestPipes.StartServer(_name, handler);
        await using var client = await TestPipes.ConnectAsync(_name);

        var request = () => client.RequestAsync(new Activate([]), cancellationToken: TestContext.Current.CancellationToken);

        var failure = await request.Should().ThrowAsync<RequestFailedException>();
        failure.Which.Code.Should().Be(ErrorCode.ShuttingDown);
        failure.Which.Detail.Should().Be("exiting");
    }

    [Fact]
    public async Task Crashing_handler_answers_failed_and_the_connection_keeps_working()
    {
        var handler = new TestHandler
        {
            OnRequest = (_, request, _) => request is Activate
                ? throw new InvalidOperationException("handler bug")
                : ValueTask.FromResult<Message?>(null),
        };
        await using var server = TestPipes.StartServer(_name, handler);
        await using var client = await TestPipes.ConnectAsync(_name);

        var crash = () => client.RequestAsync(new Activate([]), cancellationToken: TestContext.Current.CancellationToken);

        (await crash.Should().ThrowAsync<RequestFailedException>()).Which.Code.Should().Be(ErrorCode.Failed);
        (await client.RequestAsync(new Ping(), cancellationToken: TestContext.Current.CancellationToken)).Should().BeNull();
    }

    [Fact]
    public async Task Client_without_a_handler_answers_the_server_as_unsupported()
    {
        var handler = new TestHandler();
        await using var server = TestPipes.StartServer(_name, handler);
        await using var client = await TestPipes.ConnectAsync(_name);
        var accepted = await handler.Connected.NextAsync();

        var request = () => accepted.RequestAsync(new BringToFront(), cancellationToken: TestContext.Current.CancellationToken);

        (await request.Should().ThrowAsync<RequestFailedException>()).Which.Code.Should().Be(ErrorCode.Unsupported);
    }

    [Fact]
    public async Task Ping_is_answered_by_every_connection_whatever_its_handler()
    {
        var handler = new TestHandler { OnRequest = (_, _, _) => throw new RequestFailedException(ErrorCode.Unsupported) };
        await using var server = TestPipes.StartServer(_name, handler);
        await using var client = await TestPipes.ConnectAsync(_name);
        var accepted = await handler.Connected.NextAsync();

        (await client.RequestAsync(new Ping(), cancellationToken: TestContext.Current.CancellationToken)).Should().BeNull();
        (await accepted.RequestAsync(new Ping(), cancellationToken: TestContext.Current.CancellationToken)).Should().BeNull();
    }

    [Fact]
    public async Task Concurrent_requests_are_matched_to_their_own_responses()
    {
        const int Count = 64;
        var handler = new TestHandler
        {
            OnRequest = async (_, request, cancellationToken) =>
            {
                var number = int.Parse(((Activate)request).Args[0], System.Globalization.CultureInfo.InvariantCulture);
                // Later requests finish first, so responses leave in a different order than requests arrived.
                await Task.Delay(Count - number, cancellationToken);
                return new Activate([$"answer {number}"]);
            },
        };
        await using var server = TestPipes.StartServer(_name, handler);
        await using var client = await TestPipes.ConnectAsync(_name);

        var answers = await Task.WhenAll(Enumerable.Range(0, Count).Select(async number =>
        {
            var answer = await client.RequestAsync(
                new Activate([number.ToString(System.Globalization.CultureInfo.InvariantCulture)]),
                cancellationToken: TestContext.Current.CancellationToken);
            return ((Activate)answer!).Args[0];
        }));

        answers.Should().Equal(Enumerable.Range(0, Count).Select(number => $"answer {number}"));
    }

    [Fact]
    public async Task Several_clients_are_served_at_the_same_time()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new TestHandler
        {
            OnRequest = async (_, _, cancellationToken) =>
            {
                await release.Task.WaitAsync(cancellationToken);
                return null;
            },
        };
        await using var server = TestPipes.StartServer(_name, handler);
        var clients = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => TestPipes.ConnectAsync(_name, TestPipes.LauncherHello)));
        try
        {
            var requests = clients.Select(client => client.RequestAsync(new Activate([]), cancellationToken: TestContext.Current.CancellationToken)).ToArray();
            for (var i = 0; i < clients.Length; i++)
            {
                await handler.Connected.NextAsync();
            }

            server.Connections.Should().HaveCount(5);
            release.SetResult();
            (await Task.WhenAll(requests)).Should().AllSatisfy(answer => answer.Should().BeNull());
        }
        finally
        {
            foreach (var client in clients)
            {
                await client.DisposeAsync();
            }
        }
    }

    [Fact]
    public async Task Slow_request_times_out_and_its_late_answer_is_ignored()
    {
        var slow = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new TestHandler
        {
            OnRequest = async (_, request, cancellationToken) =>
            {
                if (request is Activate)
                {
                    await slow.Task.WaitAsync(cancellationToken);
                }

                return null;
            },
        };
        await using var server = TestPipes.StartServer(_name, handler);
        await using var client = await TestPipes.ConnectAsync(_name);

        var request = () => client.RequestAsync(new Activate([]), TimeSpan.FromMilliseconds(100), TestContext.Current.CancellationToken);

        await request.Should().ThrowAsync<TimeoutException>();
        slow.SetResult();
        (await client.RequestAsync(new Ping(), cancellationToken: TestContext.Current.CancellationToken)).Should().BeNull();
        client.Completion.IsCompleted.Should().BeFalse();
    }

    [Fact]
    public async Task Cancelled_request_throws_cancellation()
    {
        var handler = new TestHandler
        {
            OnRequest = async (_, _, cancellationToken) =>
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
                return null;
            },
        };
        await using var server = TestPipes.StartServer(_name, handler);
        await using var client = await TestPipes.ConnectAsync(_name);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        var request = () => client.RequestAsync(new Activate([]), cancellationToken: cancellation.Token);

        await request.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task Events_arrive_in_order_in_both_directions()
    {
        var serverHandler = new TestHandler();
        var clientHandler = new TestHandler();
        await using var server = TestPipes.StartServer(_name, serverHandler);
        await using var client = await TestPipes.ConnectAsync(_name, handler: clientHandler);
        await serverHandler.Connected.NextAsync();

        for (var i = 0; i < 50; i++)
        {
            server.Broadcast(new Navigate(new CatalogueTarget($"game-{i}")));
            await client.SendEventAsync(new Navigate(new CatalogueTarget($"game-{i}")), TestContext.Current.CancellationToken);
        }

        for (var i = 0; i < 50; i++)
        {
            (await clientHandler.Events.NextAsync()).Should().Be(new Navigate(new CatalogueTarget($"game-{i}")));
            (await serverHandler.Events.NextAsync()).Should().Be(new Navigate(new CatalogueTarget($"game-{i}")));
        }
    }

    [Fact]
    public async Task Broadcast_reaches_only_the_connections_the_filter_selects()
    {
        var serverHandler = new TestHandler();
        var uiHandler = new TestHandler();
        var launcherHandler = new TestHandler();
        await using var server = TestPipes.StartServer(_name, serverHandler);
        await using var ui = await TestPipes.ConnectAsync(_name, TestPipes.UiHello, uiHandler);
        await using var launcher = await TestPipes.ConnectAsync(_name, TestPipes.LauncherHello, launcherHandler);
        await serverHandler.Connected.NextAsync();
        await serverHandler.Connected.NextAsync();

        server.Broadcast(new BringToFront(), connection => connection.Peer.Role == ProcessRole.Ui);
        server.Broadcast(new ShutdownRequested(ShutdownReason.Exit));

        (await uiHandler.Events.NextAsync()).Should().Be(new BringToFront());
        (await uiHandler.Events.NextAsync()).Should().Be(new ShutdownRequested(ShutdownReason.Exit));
        (await launcherHandler.Events.NextAsync()).Should().Be(new ShutdownRequested(ShutdownReason.Exit));
    }

    [Fact]
    public async Task Disposing_sends_the_queued_events_first()
    {
        var handler = new TestHandler();
        await using var server = TestPipes.StartServer(_name, handler);
        var client = await TestPipes.ConnectAsync(_name);

        for (var i = 0; i < 20; i++)
        {
            await client.SendEventAsync(new Navigate(new CatalogueTarget($"game-{i}")), TestContext.Current.CancellationToken);
        }

        await client.DisposeAsync();

        for (var i = 0; i < 20; i++)
        {
            (await handler.Events.NextAsync()).Should().Be(new Navigate(new CatalogueTarget($"game-{i}")));
        }

        await handler.Disconnected.NextAsync();
    }

    [Fact]
    public async Task Stopping_the_server_fails_pending_requests_and_closes_clients()
    {
        var handler = new TestHandler
        {
            OnRequest = async (_, _, cancellationToken) =>
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
                return null;
            },
        };
        var server = TestPipes.StartServer(_name, handler);
        await using var client = await TestPipes.ConnectAsync(_name);
        var pending = client.RequestAsync(new Activate([]), cancellationToken: TestContext.Current.CancellationToken);
        await handler.Connected.NextAsync();

        await server.DisposeAsync();

        var request = () => pending;
        await request.Should().ThrowAsync<ConnectionClosedException>();
        await client.Completion.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        var later = () => client.RequestAsync(new Ping(), cancellationToken: TestContext.Current.CancellationToken);
        await later.Should().ThrowAsync<ConnectionClosedException>();
    }

    [Fact]
    public async Task Message_over_the_frame_limit_fails_without_breaking_the_connection()
    {
        await using var server = TestPipes.StartServer(_name, new TestHandler());
        await using var client = await TestPipes.ConnectAsync(_name);

        var request = () => client.RequestAsync(
            new Activate([new string('x', TestPipes.Options.MaxFrameLength)]),
            cancellationToken: TestContext.Current.CancellationToken);

        await request.Should().ThrowAsync<InvalidOperationException>();
        (await client.RequestAsync(new Ping(), cancellationToken: TestContext.Current.CancellationToken)).Should().BeNull();
    }
}
