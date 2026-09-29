using JustModIt.Manager.Protocol.Messages;
using JustModIt.Manager.Protocol.Transport;
using Microsoft.Extensions.Logging;

namespace JustModIt.Manager.Ui;

/// <summary>
/// Stands in for the window until the UI is built: it connects to the agent as the window would, follows what it is
/// sent, and ends when the agent asks it to close or goes away.
/// </summary>
public sealed partial class ConnectionOnlyUiHost : IUiHost
{
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(30);

    public int Run(UiContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        // Nothing on this thread renders, so waiting here blocks no window.
        return RunAsync(context).GetAwaiter().GetResult();
    }

    private static async Task<int> RunAsync(UiContext context)
    {
        var logger = context.Loggers.CreateLogger<ConnectionOnlyUiHost>();
        var connected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var closed = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var client = new ReconnectingPipeClient(context.PipeName, Hello.ForCurrentProcess(ProcessRole.Ui, context.Version),
            context.Loggers.CreateLogger<ReconnectingPipeClient>(), handleRequest: (request, _) =>
            {
                if (request is not ShutdownRequested shutdown)
                {
                    throw new RequestFailedException(ErrorCode.Unsupported);
                }

                LogClosing(logger, shutdown.Reason);
                closed.TrySetResult(0);
                return ValueTask.FromResult<Message?>(new ShutdownAnswer(Accepted: true));
            });
        await using (client.ConfigureAwait(false))
        {
            client.StateChanged += (_, state) =>
            {
                if (state == ConnectionState.Connected)
                {
                    connected.TrySetResult();
                }
                // Nothing here could show that the agent is being replaced, and a window left waiting would hold its lock.
                else if (state == ConnectionState.Incompatible || connected.Task.IsCompleted)
                {
                    LogAgentGone(logger);
                    closed.TrySetResult(0);
                }
            };
            using var states = client.Subscribe<StateChanged>(changed => LogState(logger, changed.State.Version, changed.State.Games.Count));
            using var fronts = client.Subscribe<BringToFront>(_ => LogBroughtToFront(logger));
            using var targets = client.Subscribe<Navigate>(navigate => LogNavigated(logger, navigate.Target.GetType().Name));
            client.Start();

            var first = await Task.WhenAny(connected.Task, closed.Task, Task.Delay(ConnectTimeout)).ConfigureAwait(false);
            if (first != connected.Task && first != closed.Task)
            {
                LogNoAgent(logger);
                return 1;
            }

            return await closed.Task.ConfigureAwait(false);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "State {Version} with {Games} games")]
    private static partial void LogState(ILogger logger, long version, int games);

    [LoggerMessage(Level = LogLevel.Information, Message = "Brought to the front")]
    private static partial void LogBroughtToFront(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Navigated to {Target}")]
    private static partial void LogNavigated(ILogger logger, string target);

    [LoggerMessage(Level = LogLevel.Information, Message = "Closing as the agent asked ({Reason})")]
    private static partial void LogClosing(ILogger logger, ShutdownReason reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The agent went away; closing")]
    private static partial void LogAgentGone(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "No agent answered; closing")]
    private static partial void LogNoAgent(ILogger logger);
}
