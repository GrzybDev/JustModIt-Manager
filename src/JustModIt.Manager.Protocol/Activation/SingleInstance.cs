using System.ComponentModel;
using System.Diagnostics;
using JustModIt.Manager.Protocol.Messages;
using JustModIt.Manager.Protocol.Transport;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace JustModIt.Manager.Protocol.Activation;

/// <summary>The outcome of <see cref="SingleInstance.ClaimOrForwardAsync"/>.</summary>
public abstract record InstanceClaim;

/// <summary>This process now holds the role and keeps the lock for as long as it runs it.</summary>
/// <param name="Lock">The role's lock, held until this process stops running the role.</param>
public sealed record Claimed(InstanceLock Lock) : InstanceClaim;

/// <summary>The running agent took the command line; this process can exit.</summary>
/// <param name="Agent">The running agent's hello.</param>
public sealed record Forwarded(Hello Agent) : InstanceClaim;

/// <summary>The role is taken, but the agent did not take the command line in time.</summary>
/// <param name="Error">Why the hand-over failed, for the log.</param>
public sealed record Unreachable(Exception Error) : InstanceClaim;

public static partial class SingleInstance
{
    /// <summary>
    /// Takes the role's lock or, when another process holds it or the caller takes none, hands the command line to the
    /// running agent.
    /// </summary>
    /// <param name="lockName">The role's lock from <see cref="InstanceNames"/>; null only hands the command line over.</param>
    /// <param name="pipeName">The agent's pipe.</param>
    /// <param name="hello">This process's hello.</param>
    /// <param name="args">The command line to hand over.</param>
    /// <param name="timeout">
    /// How long to keep trying, with a backoff and until an exiting agent's process ends, so a launch is not lost while the
    /// agent starts or exits.
    /// </param>
    /// <param name="onAgentFound">
    /// Runs before the command line is sent, so this process can let the agent take the foreground before it acts.
    /// </param>
    /// <param name="options">The transport's settings; null for the defaults.</param>
    /// <param name="logger">Null logs nothing.</param>
    /// <param name="cancellationToken">Gives up the claim.</param>
    public static async Task<InstanceClaim> ClaimOrForwardAsync(
        string? lockName,
        string pipeName,
        Hello hello,
        IReadOnlyList<string> args,
        TimeSpan timeout,
        Action<Hello>? onAgentFound = null,
        IpcOptions? options = null,
        ILogger? logger = null,
        CancellationToken cancellationToken = default)
    {
        options ??= IpcOptions.Default;
        logger ??= NullLogger.Instance;
        var attempts = new Attempts(timeout, options);
        while (true)
        {
            if (lockName is not null && InstanceLock.TryAcquire(lockName) is { } instanceLock)
            {
                return new Claimed(instanceLock);
            }

            Hello? agent = null;
            try
            {
                var connection = await PipeClient
                    .ConnectAsync(pipeName, hello, options: options, logger: logger, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
                await using (connection.ConfigureAwait(false))
                {
                    agent = connection.Peer;
                    onAgentFound?.Invoke(agent);
                    await connection.RequestAsync(new Activate(args), attempts.Remaining, cancellationToken).ConfigureAwait(false);
                }

                return new Forwarded(agent);
            }
            catch (Exception ex) when (agent is null && ex is TimeoutException or IOException && attempts.Remaining > TimeSpan.Zero)
            {
                LogAgentNotListening(logger, ex);
            }
            catch (Exception ex) when (agent is not null
                && ex is ConnectionClosedException or RequestFailedException { Code: ErrorCode.ShuttingDown })
            {
                LogAgentExiting(logger, agent.ProcessId);
            }
            catch (Exception ex) when (ex is TimeoutException or IOException or UnauthorizedAccessException
                or InvalidDataException or ProtocolMismatchException or RequestFailedException)
            {
                LogForwardFailed(logger, ex);
                return new Unreachable(ex);
            }

            try
            {
                if (agent is null)
                {
                    await attempts.WaitAsync(cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    await WaitForExitAsync(agent.ProcessId, attempts.Remaining, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (Exception ex) when (ex is TimeoutException or InvalidOperationException or Win32Exception)
            {
                LogForwardFailed(logger, ex);
                return new Unreachable(ex);
            }
        }
    }

    /// <summary>Waits for the agent to answer on its pipe and tells its hello, sending it nothing.</summary>
    /// <param name="pipeName">The agent's pipe.</param>
    /// <param name="hello">This process's hello.</param>
    /// <param name="timeout">How long to keep trying, with a backoff, while the agent starts.</param>
    /// <param name="options">The transport's settings; null for the defaults.</param>
    /// <returns>The agent's hello, also when it speaks another protocol; null when none answered in time.</returns>
    public static async Task<Hello?> FindAgentAsync(string pipeName, Hello hello, TimeSpan timeout, IpcOptions? options = null)
    {
        options ??= IpcOptions.Default;
        var attempts = new Attempts(timeout, options);
        do
        {
            try
            {
                var connection = await PipeClient.ConnectAsync(pipeName, hello, options: options).ConfigureAwait(false);
                await using (connection.ConfigureAwait(false))
                {
                    return connection.Peer;
                }
            }
            catch (ProtocolMismatchException exception)
            {
                return exception.Peer;
            }
            catch (Exception exception) when (exception is TimeoutException or IOException)
            {
                await attempts.WaitAsync(CancellationToken.None).ConfigureAwait(false);
            }
        }
        while (attempts.Remaining > TimeSpan.Zero);

        return null;
    }

    private static async Task WaitForExitAsync(int processId, TimeSpan timeout, CancellationToken cancellationToken)
    {
        Process process;
        try
        {
            process = Process.GetProcessById(processId);
        }
        catch (ArgumentException)
        {
            return;
        }

        using (process)
        {
            await process.WaitForExitAsync(cancellationToken).WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
        }
    }

    // The tries of one launch to reach the agent: a backoff between them, cut short by the time the launch may take.
    private sealed class Attempts(TimeSpan timeout, IpcOptions options)
    {
        private readonly long _started = Stopwatch.GetTimestamp();
        private readonly Backoff _backoff = new(options.RetryDelay, options.MaxRetryDelay);

        public TimeSpan Remaining
        {
            get
            {
                var remaining = timeout - Stopwatch.GetElapsedTime(_started);
                return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
            }
        }

        public Task WaitAsync(CancellationToken cancellationToken)
        {
            var delay = _backoff.Next();
            var remaining = Remaining;
            return Task.Delay(delay < remaining ? delay : remaining, cancellationToken);
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "The running agent is not listening; trying again")]
    private static partial void LogAgentNotListening(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "The running agent {ProcessId} is exiting; waiting to take over")]
    private static partial void LogAgentExiting(ILogger logger, int processId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not hand the command line to the running agent")]
    private static partial void LogForwardFailed(ILogger logger, Exception exception);
}
