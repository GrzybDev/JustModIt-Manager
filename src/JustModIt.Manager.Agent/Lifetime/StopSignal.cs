using System.Runtime.InteropServices;
using JustModIt.Manager.Protocol.Activation;
using Microsoft.Extensions.Logging;

namespace JustModIt.Manager.Agent.Lifetime;

/// <summary>
/// Exits at once when the system stops the agent (a logout, a shutdown, its service being stopped), so running jobs undo
/// what they changed and the session ends cleanly instead of with the process.
/// </summary>
public sealed partial class StopSignal : IAgentService, IDisposable
{
    private readonly PosixSignalRegistration _registration;

    /// <summary>Starts listening for the system's stop signal.</summary>
    /// <param name="lifetime">Exits the agent.</param>
    /// <param name="logger">Hears that the system stopped the agent.</param>
    public StopSignal(AgentLifetime lifetime, ILogger<StopSignal> logger) =>
        _registration = PosixSignalRegistration.Create(PosixSignal.SIGTERM, signal =>
        {
            // The signal would otherwise end the process at once, in the middle of whatever a job was changing.
            signal.Cancel = true;
            LogStopped(logger);
            lifetime.ExitAtOnce();
        });

    public Task RunAsync(LaunchArguments start, CancellationToken cancellationToken) => Task.CompletedTask;

    public void Dispose() => _registration.Dispose();

    [LoggerMessage(Level = LogLevel.Information, Message = "The system stops the agent")]
    private static partial void LogStopped(ILogger logger);
}
