using JustModIt.Manager.Agent.Lifetime;
using JustModIt.Manager.Agent.Ui;
using JustModIt.Manager.Protocol.Activation;
using JustModIt.Manager.Protocol.Messages;
using JustModIt.Manager.Protocol.Transport;

namespace JustModIt.Manager.Agent.Ipc;

/// <summary>Everything that reaches the agent's pipe: later launches, and the window's commands and queries.</summary>
/// <param name="commands">Carries out the window's commands.</param>
/// <param name="queries">Answers the window's queries.</param>
/// <param name="activation">Acts on a later launch's command line.</param>
/// <param name="lifetime">Tells whether the agent is leaving.</param>
/// <param name="ui">Follows the window's connection.</param>
/// <param name="links">Reads links in a later launch's command line.</param>
public sealed class AgentRequestHandler(
    AgentCommands commands,
    AgentQueries queries,
    ActivationHandler activation,
    AgentLifetime lifetime,
    UiLauncher ui,
    AppLinks links) : IConnectionHandler
{
    public void OnConnected(PipeConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);
        if (connection.Peer.Role == ProcessRole.Ui)
        {
            ui.OnConnected(connection);
        }
    }

    public void OnDisconnected(PipeConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);
        if (connection.Peer.Role == ProcessRole.Ui)
        {
            ui.OnDisconnected(connection);
        }
    }

    public void OnEvent(PipeConnection connection, Message message)
    {
        ArgumentNullException.ThrowIfNull(connection);
        if (message is WindowActivity activity)
        {
            ui.OnActivity(connection, activity.IsActive);
        }
    }

    public ValueTask<Message?> HandleRequestAsync(PipeConnection connection, Message request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        if (request is Activate activate)
        {
            // A launch during an exit waits for this process to end and takes over, so it is not lost.
            if (lifetime.IsLeaving)
            {
                throw new RequestFailedException(ErrorCode.ShuttingDown);
            }

            activation.Handle(LaunchArguments.Parse(activate.Args, links));
            return ValueTask.FromResult<Message?>(null);
        }

        // A launcher has nothing to ask for but its activation.
        if (connection.Peer.Role != ProcessRole.Ui)
        {
            throw new RequestFailedException(ErrorCode.Unsupported);
        }

        return request is GetProjectPage or GetCataloguePage or GetCatalogueFilters or GetDetectionReport or GetLeftoverBackups
            ? queries.HandleAsync(request, cancellationToken)
            : commands.HandleAsync(request, cancellationToken);
    }
}
