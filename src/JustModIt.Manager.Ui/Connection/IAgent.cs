using System.ComponentModel;
using JustModIt.Manager.Protocol.Messages;
using JustModIt.Manager.Protocol.Views;

namespace JustModIt.Manager.Ui.Connection;

/// <summary>How the window's connection to the agent stands.</summary>
public enum AgentLink
{
    /// <summary>Not connected yet, or connecting again after the agent went away.</summary>
    Connecting,

    Connected,

    /// <summary>The agent speaks another protocol version, which happens only while the app updates itself.</summary>
    Incompatible,
}

/// <summary>
/// The agent as the window sees it: the newest state for bindings, the events it sends and the requests it answers.
/// Properties change and events are raised on the UI thread only.
/// </summary>
public interface IAgent : INotifyPropertyChanged
{
    AgentLink Link { get; }

    /// <summary>
    /// The newest state, each job in it as the newest report has it; an empty one until the agent sent the first. A change is
    /// raised for a new state only: a report moves its job on here quietly, and <see cref="JobsProgressed"/> announces it.
    /// </summary>
    AppStateView State { get; }

    /// <summary>The agent sent a state since the window started, so <see cref="State"/> is real.</summary>
    bool HasState { get; }

    /// <summary>
    /// Answers the agent's request to close the window: true once the window agreed and will close, false when the player's
    /// unsaved work keeps it open. Null accepts at once.
    /// </summary>
    Func<ShutdownReason, Task<bool>>? ShutdownHandler { get; set; }

    /// <summary>
    /// Jobs that <see cref="State"/> lists moved on, at most about ten times a second; a job the state no longer lists, or a
    /// report older than what the state shows, never comes.
    /// </summary>
    event EventHandler<IReadOnlyList<JobView>>? JobsProgressed;

    /// <summary>A job ended, with how; it may come before or after the state that no longer lists the job.</summary>
    event EventHandler<JobEnded>? JobEnded;

    /// <summary>The automation did or found something, to show while the window is in front.</summary>
    event EventHandler<NoticeView>? Noticed;

    /// <summary>The agent asks the window to show a place, e.g. for a link the player opened.</summary>
    event EventHandler<NavigationTarget>? NavigationRequested;

    /// <summary>The agent asks the window to come to the front.</summary>
    event EventHandler? BringToFrontRequested;

    /// <summary>Sends a command or a query and waits for the answer.</summary>
    /// <param name="request">A message of <c>Protocol.Messages</c>.</param>
    /// <param name="cancellationToken">Stops waiting; the agent may still act on a request already sent.</param>
    /// <returns>The answer's payload; null for an empty answer.</returns>
    /// <exception cref="Protocol.Transport.ConnectionClosedException">The window is not connected right now.</exception>
    /// <exception cref="Protocol.Transport.RequestFailedException">The agent refused the request.</exception>
    /// <exception cref="TimeoutException">The agent did not answer in time.</exception>
    Task<Message?> RequestAsync(Message request, CancellationToken cancellationToken = default);

    /// <summary>Tells the agent whether the window is in front, which decides where notices go.</summary>
    /// <param name="isActive">Whether the window is in front.</param>
    void ReportActivity(bool isActive);
}
