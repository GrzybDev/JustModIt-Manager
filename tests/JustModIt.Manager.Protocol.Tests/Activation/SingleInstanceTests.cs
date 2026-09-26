using System.Collections.Concurrent;
using System.IO.Pipes;
using JustModIt.Manager.Protocol.Activation;
using JustModIt.Manager.Protocol.Messages;
using JustModIt.Manager.Protocol.Tests.Transport;
using JustModIt.Manager.Protocol.Transport;

namespace JustModIt.Manager.Protocol.Tests.Activation;

public sealed class SingleInstanceTests
{
    // No process ever has this id, so such an agent counts as already gone.
    private static readonly Hello _exitedAgent = TestPipes.AgentHello with { ProcessId = int.MaxValue };

    private readonly string _lock = $@"Local\ExampleApp.Tests.{Guid.NewGuid():N}";
    private readonly string _pipe = TestPipes.NewName();

    [Fact]
    public async Task First_launch_claims_the_role()
    {
        var claim = await ClaimAsync(["--background"]);

        using var instanceLock = claim.Should().BeOfType<Claimed>().Subject.Lock;
        InstanceLock.TryAcquire(_lock).Should().BeNull();
    }

    [Fact]
    public async Task Later_launch_lets_the_agent_take_the_foreground_then_hands_over_its_command_line()
    {
        using var running = InstanceLock.TryAcquire(_lock);
        var steps = new ConcurrentQueue<string>();
        var received = new TaskCompletionSource<Message>(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new TestHandler
        {
            OnRequest = (_, request, _) =>
            {
                steps.Enqueue("activated");
                received.SetResult(request);
                return ValueTask.FromResult<Message?>(null);
            },
        };
        await using var server = TestPipes.StartServer(_pipe, handler);
        string[] args = ["example-app://catalogue/example-ąćź", "--background"];

        var claim = await ClaimAsync(args, onAgentFound: agent => steps.Enqueue($"found {agent.ProcessId}"));

        claim.Should().Be(new Forwarded(TestPipes.AgentHello));
        var activate = (await received.Task).Should().BeOfType<Activate>().Subject;
        activate.Args.Should().Equal(args);
        steps.Should().Equal($"found {TestPipes.AgentHello.ProcessId}", "activated");
    }

    [Fact]
    public async Task Launch_gives_up_when_nothing_listens_while_the_role_stays_taken()
    {
        using var running = InstanceLock.TryAcquire(_lock);

        var claim = await ClaimAsync([], TimeSpan.FromMilliseconds(300));

        claim.Should().BeOfType<Unreachable>().Which.Error.Should().BeOfType<TimeoutException>();
    }

    [Fact]
    public async Task Launch_while_the_agent_starts_is_handed_over_once_it_listens()
    {
        using var running = InstanceLock.TryAcquire(_lock);
        var handler = new TestHandler();
        var claiming = ClaimAsync(["--background"]);
        await Task.Delay(TimeSpan.FromMilliseconds(200), TestContext.Current.CancellationToken);

        await using var server = TestPipes.StartServer(_pipe, handler);

        (await claiming).Should().Be(new Forwarded(TestPipes.AgentHello));
    }

    [Fact]
    public async Task Launch_after_the_agent_stopped_listening_takes_over_once_it_lets_go()
    {
        var running = InstanceLock.TryAcquire(_lock)!;
        var claiming = ClaimAsync([]);
        await Task.Delay(TimeSpan.FromMilliseconds(200), TestContext.Current.CancellationToken);

        running.Dispose();

        using var instanceLock = (await claiming).Should().BeOfType<Claimed>().Subject.Lock;
    }

    [Fact]
    public async Task Agent_dropping_the_connection_is_waited_for_and_taken_over()
    {
        var running = InstanceLock.TryAcquire(_lock)!;
        var handler = new TestHandler
        {
            OnRequest = async (connection, _, _) =>
            {
                running.Dispose();
                await connection.DisposeAsync();
                return null;
            },
        };
        await using var server = TestPipes.StartServer(_pipe, handler, hello: _exitedAgent);

        var claim = await ClaimAsync([]);

        using var instanceLock = claim.Should().BeOfType<Claimed>().Subject.Lock;
    }

    [Fact]
    public async Task Agent_speaking_nonsense_is_unreachable()
    {
        using var running = InstanceLock.TryAcquire(_lock);
        await using var squatter = new NamedPipeServerStream(_pipe, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        var answering = AnswerWithNonsenseAsync(squatter);

        var claim = await ClaimAsync([]);

        claim.Should().BeOfType<Unreachable>().Which.Error.Should().BeOfType<InvalidDataException>();
        await answering;
    }

    [Fact]
    public async Task Agent_refusing_the_command_line_is_unreachable()
    {
        using var running = InstanceLock.TryAcquire(_lock);
        var handler = new TestHandler { OnRequest = (_, _, _) => throw new RequestFailedException(ErrorCode.Failed) };
        await using var server = TestPipes.StartServer(_pipe, handler);

        var claim = await ClaimAsync([]);

        claim.Should().BeOfType<Unreachable>().Which.Error.Should().BeOfType<RequestFailedException>();
    }

    [Fact]
    public async Task Launch_during_the_agent_exit_waits_for_it_and_takes_over()
    {
        var running = InstanceLock.TryAcquire(_lock)!;
        var handler = new TestHandler
        {
            OnRequest = (_, _, _) =>
            {
                running.Dispose();
                throw new RequestFailedException(ErrorCode.ShuttingDown);
            },
        };
        await using var server = TestPipes.StartServer(_pipe, handler, hello: _exitedAgent);

        var claim = await ClaimAsync([]);

        using var instanceLock = claim.Should().BeOfType<Claimed>().Subject.Lock;
    }

    [Fact]
    public async Task Agent_that_does_not_finish_exiting_in_time_is_unreachable()
    {
        using var running = InstanceLock.TryAcquire(_lock);
        var handler = new TestHandler { OnRequest = (_, _, _) => throw new RequestFailedException(ErrorCode.ShuttingDown) };
        var livingAgent = TestPipes.AgentHello with { ProcessId = Environment.ProcessId };
        await using var server = TestPipes.StartServer(_pipe, handler, hello: livingAgent);

        var claim = await ClaimAsync([], TimeSpan.FromMilliseconds(500));

        claim.Should().BeOfType<Unreachable>().Which.Error.Should().BeOfType<TimeoutException>();
    }

    private Task<InstanceClaim> ClaimAsync(
        IReadOnlyList<string> args,
        TimeSpan? timeout = null,
        Action<Hello>? onAgentFound = null) =>
        SingleInstance.ClaimOrForwardAsync(
            _lock,
            _pipe,
            TestPipes.LauncherHello,
            args,
            timeout ?? TimeSpan.FromSeconds(10),
            onAgentFound,
            TestPipes.Options,
            cancellationToken: TestContext.Current.CancellationToken);

    private static async Task AnswerWithNonsenseAsync(NamedPipeServerStream pipe)
    {
        await pipe.WaitForConnectionAsync(TestContext.Current.CancellationToken);
        byte[] nonsense = [8, 0, 0, 0, .. "not json"u8];
        await pipe.WriteAsync(nonsense, TestContext.Current.CancellationToken);
    }
}
