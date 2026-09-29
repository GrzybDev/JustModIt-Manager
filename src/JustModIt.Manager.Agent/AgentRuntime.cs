using JustModIt.Manager.Agent.Ipc;
using JustModIt.Manager.Agent.Lifetime;
using JustModIt.Manager.Core.Catalog;
using JustModIt.Manager.Core.Jobs;
using JustModIt.Manager.Core.Library;
using JustModIt.Manager.Core.State;
using JustModIt.Manager.Protocol.Activation;
using JustModIt.Manager.Protocol.Transport;
using Microsoft.Extensions.Logging;

namespace JustModIt.Manager.Agent;

/// <summary>
/// Runs the agent: listens first, so no launch is lost while the rest starts, then keeps the engine's loops and the
/// agent's services going until an exit is decided.
/// </summary>
/// <param name="server">The agent's pipe, which starts listening first.</param>
/// <param name="library">Detects and watches the games.</param>
/// <param name="coordinator">Owns the engine's state.</param>
/// <param name="catalogue">Refreshes the catalogue on its schedule.</param>
/// <param name="publisher">Sends the state to the window and the tray.</param>
/// <param name="jobs">The running jobs, which an exit waits for.</param>
/// <param name="activation">Acts on the command line the agent started with.</param>
/// <param name="lifetime">Decides when the agent exits.</param>
/// <param name="services">The agent's other services, run alongside the loops.</param>
/// <param name="options">The app's version, for the log.</param>
/// <param name="logger">Receives the start, the stop and failed loops.</param>
public sealed partial class AgentRuntime(
    PipeServer server,
    LibraryService library,
    StateCoordinator coordinator,
    CatalogueRefreshLoop catalogue,
    StatePublisher publisher,
    JobRunner jobs,
    ActivationHandler activation,
    AgentLifetime lifetime,
    IEnumerable<IAgentService> services,
    AgentOptions options,
    ILogger<AgentRuntime> logger)
{
    /// <summary>Runs until the agent exits, or until <paramref name="cancellationToken"/> tears it down.</summary>
    /// <param name="start">The command line the agent started with.</param>
    /// <param name="cancellationToken">Tears the agent down without the usual exit, e.g. at the end of a test.</param>
    public async Task RunAsync(LaunchArguments start, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(start);
        LogStarting(options.Version, start.Background);
        server.Start();
        using var stopping = new CancellationTokenSource();
        Task[] loops =
        [
            RunLoopAsync(nameof(LibraryService), library.RunAsync, stopping.Token),
            RunLoopAsync(nameof(StateCoordinator), coordinator.RunAsync, stopping.Token),
            RunLoopAsync(nameof(StatePublisher), publisher.RunAsync, stopping.Token),
            RunLoopAsync(nameof(CatalogueRefreshLoop), token => catalogue.RunAsync(StartupDelays.Catalogue(start), token), stopping.Token),
            .. services.Select(service => RunLoopAsync(service.GetType().Name, token => service.RunAsync(start, token), stopping.Token)),
        ];
        activation.Handle(start);

        using (cancellationToken.Register(lifetime.Abandon))
        {
            await lifetime.Exited.ConfigureAwait(false);

            // Cancelled jobs undo their changes first; meanwhile the pipe keeps telling launches to wait for this process.
            await jobs.WhenIdleAsync(cancellationToken).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }

        LogStopping();
        await stopping.CancelAsync().ConfigureAwait(false);
        await Task.WhenAll(loops).ConfigureAwait(false);
    }

    private async Task RunLoopAsync(string name, Func<CancellationToken, Task> loop, CancellationToken stopping)
    {
        try
        {
            // Off the caller's thread, so no loop's synchronous start holds up the others.
            await Task.Run(() => loop(stopping), stopping).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            LogLoopFailed(exception, name);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Agent {Version} starting (background: {Background})")]
    private partial void LogStarting(string version, bool background);

    [LoggerMessage(Level = LogLevel.Information, Message = "Agent stopping")]
    private partial void LogStopping();

    [LoggerMessage(Level = LogLevel.Critical, Message = "The {Loop} loop failed and stopped")]
    private partial void LogLoopFailed(Exception exception, string loop);
}
