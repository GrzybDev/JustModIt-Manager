using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using JustModIt.Manager.Protocol.Messages;
using JustModIt.Manager.Protocol.Transport;
using JustModIt.Manager.Protocol.Views;
using Microsoft.Extensions.Logging;

namespace JustModIt.Manager.Ui.Connection;

/// <summary>
/// The window's connection to the agent over its pipe, reconnecting whenever it drops. Everything the agent sends reaches
/// the UI thread through the dispatcher, coalesced: a busy UI thread skips the states and progress in between.
/// </summary>
public sealed partial class AgentConnection : ObservableObject, IAgent, IAsyncDisposable
{
    private readonly ReconnectingPipeClient _client;
    private readonly IDisposable[] _subscriptions;
    private readonly Lock _gate = new();
    private readonly Dictionary<Guid, JobView> _pendingProgress = [];

    // The newest snapshot of each job the state lists, from the state or a later report; only the UI thread touches it.
    private readonly Dictionary<Guid, JobView> _jobs = [];
    private AppStateView? _pendingState;
    private AppStateView _received = new();
    private AppStateView? _state;
    private bool _flushQueued;
    private bool _isActive;

    /// <summary>Creates the connection, which starts connecting at once.</summary>
    /// <param name="pipeName">The agent's pipe.</param>
    /// <param name="version">This app's version, for the agent's log.</param>
    /// <param name="loggers">Receive the connection's log.</param>
    /// <param name="options">The transport's settings; null for the defaults.</param>
    public AgentConnection(string pipeName, string version, ILoggerFactory loggers, IpcOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(loggers);
        _client = new ReconnectingPipeClient(pipeName, Hello.ForCurrentProcess(ProcessRole.Ui, version),
            loggers.CreateLogger<ReconnectingPipeClient>(), options, HandleRequestAsync);
        _client.StateChanged += (_, state) => Dispatcher.UIThread.Post(() => OnLinkChanged(state));
        _subscriptions =
        [
            _client.Subscribe<StateChanged>(OnStateChanged),
            _client.Subscribe<JobProgress>(OnJobProgress),
            _client.Subscribe<JobEnded>(ended => Dispatcher.UIThread.Post(() => JobEnded?.Invoke(this, ended))),
            _client.Subscribe<Notify>(notify => Dispatcher.UIThread.Post(() => Noticed?.Invoke(this, notify.Notice))),
            _client.Subscribe<Navigate>(navigate => Dispatcher.UIThread.Post(() => NavigationRequested?.Invoke(this, navigate.Target))),
            _client.Subscribe<BringToFront>(_ => Dispatcher.UIThread.Post(() => BringToFrontRequested?.Invoke(this, EventArgs.Empty))),
        ];
        _client.Start();
    }

    public event EventHandler<IReadOnlyList<JobView>>? JobsProgressed;

    public event EventHandler<JobEnded>? JobEnded;

    public event EventHandler<NoticeView>? Noticed;

    public event EventHandler<NavigationTarget>? NavigationRequested;

    public event EventHandler? BringToFrontRequested;

    [ObservableProperty]
    public partial AgentLink Link { get; private set; }

    // Built on the first read after a state or a report: reports come ten times a second, and a change raised for each
    // would make every page lay itself out again.
    public AppStateView State => _state ??= WithNewestJobs(_received);

    [ObservableProperty]
    public partial bool HasState { get; private set; }

    public Func<ShutdownReason, Task<bool>>? ShutdownHandler { get; set; }

    public Task<Message?> RequestAsync(Message request, CancellationToken cancellationToken = default) =>
        _client.RequestAsync(request, cancellationToken: cancellationToken);

    public void ReportActivity(bool isActive)
    {
        _isActive = isActive;
        _client.Post(new WindowActivity(isActive));
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var subscription in _subscriptions)
        {
            subscription.Dispose();
        }

        await _client.DisposeAsync().ConfigureAwait(false);
    }

    private void OnLinkChanged(ConnectionState state)
    {
        Link = state switch
        {
            ConnectionState.Connected => AgentLink.Connected,
            ConnectionState.Incompatible => AgentLink.Incompatible,
            _ => AgentLink.Connecting,
        };
        // A new connection starts with the window counted as not in front.
        if (state == ConnectionState.Connected && _isActive)
        {
            _client.Post(new WindowActivity(true));
        }
    }

    private void OnStateChanged(StateChanged changed)
    {
        lock (_gate)
        {
            _pendingState = changed.State;
        }

        QueueFlush();
    }

    private void OnJobProgress(JobProgress progress)
    {
        lock (_gate)
        {
            foreach (var job in progress.Jobs)
            {
                if (!_pendingProgress.TryGetValue(job.Id, out var known) || known.Sequence < job.Sequence)
                {
                    _pendingProgress[job.Id] = job;
                }
            }
        }

        QueueFlush();
    }

    private void QueueFlush()
    {
        lock (_gate)
        {
            if (_flushQueued)
            {
                return;
            }

            _flushQueued = true;
        }

        Dispatcher.UIThread.Post(Flush);
    }

    // The state goes first and the reports are measured against it, so whichever order the two came in, a report never
    // brings back a job the state no longer lists, and a state never takes a job back behind a report already shown.
    private void Flush()
    {
        AppStateView? state;
        List<JobView> reports;
        lock (_gate)
        {
            state = _pendingState;
            reports = [.. _pendingProgress.Values];
            _pendingState = null;
            _pendingProgress.Clear();
            _flushQueued = false;
        }

        if (state is not null)
        {
            Track(state);
            _received = state;
            _state = null;
            OnPropertyChanged(nameof(State));
            HasState = true;
        }

        List<JobView> moved = [.. reports.Where(job => _jobs.GetValueOrDefault(job.Id) is { } known && known.Sequence < job.Sequence)];
        foreach (var job in moved)
        {
            _jobs[job.Id] = job;
        }

        if (moved.Count > 0)
        {
            _state = null;
            JobsProgressed?.Invoke(this, moved);
        }
    }

    // The agent's state carries each job as its last step left it, e.g. a download with nothing received yet.
    private void Track(AppStateView state)
    {
        var shown = _jobs.ToDictionary();
        _jobs.Clear();
        foreach (var job in state.Jobs)
        {
            _jobs[job.Id] = shown.TryGetValue(job.Id, out var known) && known.Sequence > job.Sequence ? known : job;
        }
    }

    private AppStateView WithNewestJobs(AppStateView state)
    {
        JobView Newest(JobView job) => _jobs.GetValueOrDefault(job.Id, job);
        return state.Jobs.All(job => Newest(job) == job) ? state : state with
        {
            Jobs = [.. state.Jobs.Select(Newest)],
            Games = [.. state.Games.Select(game => game.Job is { } job ? game with { Job = Newest(job) } : game)],
        };
    }

    private async ValueTask<Message?> HandleRequestAsync(Message request, CancellationToken cancellationToken)
    {
        if (request is not ShutdownRequested shutdown)
        {
            throw new RequestFailedException(ErrorCode.Unsupported);
        }

        var accepted = await Dispatcher.UIThread.InvokeAsync(() => ShutdownHandler?.Invoke(shutdown.Reason) ?? Task.FromResult(true))
            .WaitAsync(cancellationToken).ConfigureAwait(false);
        return new ShutdownAnswer(accepted);
    }
}
