using JustModIt.Manager.Protocol.Activation;

namespace JustModIt.Manager.Agent;

/// <summary>
/// Work that runs for as long as the agent does, started once the agent listens and cancelled when it exits; an app adds
/// its own by registering more.
/// </summary>
public interface IAgentService
{
    /// <summary>Does the service's work until the agent exits.</summary>
    /// <param name="start">How this agent was started; a start with the system delays heavy work.</param>
    /// <param name="cancellationToken">Cancelled when the agent exits.</param>
    Task RunAsync(LaunchArguments start, CancellationToken cancellationToken);
}

/// <summary>
/// How long heavy work waits after the agent starts; a start with the system waits longer, so the app does not compete
/// with everything else signing in.
/// </summary>
public static class StartupDelays
{
    /// <summary>The wait before the first catalogue refresh.</summary>
    /// <param name="start">How this agent was started.</param>
    public static TimeSpan Catalogue(LaunchArguments start)
    {
        ArgumentNullException.ThrowIfNull(start);
        return start.Background ? TimeSpan.FromMinutes(2) : TimeSpan.Zero;
    }

    /// <summary>The wait before the first check for an update of the app.</summary>
    /// <param name="start">How this agent was started.</param>
    public static TimeSpan UpdateCheck(LaunchArguments start)
    {
        ArgumentNullException.ThrowIfNull(start);
        return start.Background ? TimeSpan.FromMinutes(5) : TimeSpan.FromMinutes(1);
    }
}
