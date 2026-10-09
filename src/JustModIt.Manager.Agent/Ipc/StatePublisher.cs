using System.Threading.Channels;
using JustModIt.Manager.Core;
using JustModIt.Manager.Core.Catalog;
using JustModIt.Manager.Core.Jobs;
using JustModIt.Manager.Core.Settings;
using JustModIt.Manager.Core.State;
using JustModIt.Manager.Protocol.Views;
using Microsoft.Extensions.Logging;

namespace JustModIt.Manager.Agent.Ipc;

/// <summary>
/// Turns changes of the engine's state, the settings, the catalogue and the agent's status into whole snapshots for the
/// window and the tray, folding those that arrive meanwhile into the next one, and job progress out at most ten times a
/// second.
/// </summary>
public sealed partial class StatePublisher
{
    public static readonly TimeSpan ProgressInterval = TimeSpan.FromMilliseconds(100);

    private readonly StateCoordinator _coordinator;
    private readonly ISettingsService _settings;
    private readonly ICatalogue _catalogue;
    private readonly AgentStatus _status;
    private readonly JobRunner _jobs;
    private readonly TimeProvider _time;
    private readonly ILogger<StatePublisher> _logger;
    private readonly Channel<bool> _changes = Channel.CreateBounded<bool>(new BoundedChannelOptions(1)
    {
        FullMode = BoundedChannelFullMode.DropWrite,
    });

    private readonly Channel<bool> _progressed = Channel.CreateBounded<bool>(new BoundedChannelOptions(1)
    {
        FullMode = BoundedChannelFullMode.DropWrite,
    });

    private readonly Lock _gate = new();
    private readonly Dictionary<Guid, JobSnapshot> _progress = [];
    private AppStateView _latest = new();

    /// <summary>Creates the publisher, which follows its sources once running.</summary>
    /// <param name="coordinator">The engine's state.</param>
    /// <param name="settings">The settings, part of every snapshot.</param>
    /// <param name="catalogue">The catalogue, whose site facts every snapshot carries.</param>
    /// <param name="status">The agent's activity and update status.</param>
    /// <param name="jobs">Reports the jobs' progress.</param>
    /// <param name="time">Spaces the progress reports.</param>
    /// <param name="logger">Receives failed listeners.</param>
    public StatePublisher(StateCoordinator coordinator, ISettingsService settings, ICatalogue catalogue, AgentStatus status, JobRunner jobs,
        TimeProvider time, ILogger<StatePublisher> logger)
    {
        ArgumentNullException.ThrowIfNull(coordinator);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(catalogue);
        ArgumentNullException.ThrowIfNull(status);
        ArgumentNullException.ThrowIfNull(jobs);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(logger);
        _coordinator = coordinator;
        _settings = settings;
        _catalogue = catalogue;
        _status = status;
        _jobs = jobs;
        _time = time;
        _logger = logger;
    }

    /// <summary>Raised on the publisher's loop with every new snapshot, once <see cref="Latest"/> is it; handlers must return quickly.</summary>
    public event EventHandler<AppStateView>? Published;

    /// <summary>Raised with the running jobs whose progress moved since the last time; handlers must return quickly.</summary>
    public event EventHandler<IReadOnlyList<JobView>>? JobsProgressed;

    public AppStateView Latest => Volatile.Read(ref _latest);

    /// <summary>Publishes snapshots and job progress until the agent stops.</summary>
    /// <param name="cancellationToken">Stops publishing.</param>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        _settings.Changed += OnChanged;
        _catalogue.Changed += OnChanged;
        _status.Changed += OnChanged;
        _jobs.Progressed += OnProgressed;
        try
        {
            await Task.WhenAll(WatchCoordinatorAsync(cancellationToken), PublishAsync(cancellationToken), SendProgressAsync(cancellationToken))
                .ConfigureAwait(false);
        }
        finally
        {
            _settings.Changed -= OnChanged;
            _catalogue.Changed -= OnChanged;
            _status.Changed -= OnChanged;
            _jobs.Progressed -= OnProgressed;
        }
    }

    private async Task WatchCoordinatorAsync(CancellationToken cancellationToken)
    {
        var version = -1L;
        while (true)
        {
            version = (await _coordinator.WaitForChangeAsync(version, cancellationToken).ConfigureAwait(false)).Version;
            _changes.Writer.TryWrite(true);
        }
    }

    private async Task PublishAsync(CancellationToken cancellationToken)
    {
        var version = 0L;
        while (true)
        {
            await _changes.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
            var state = AppStateView.From(++version, _coordinator.Current, _settings.Current, _catalogue.Current.Site, _status.Activity,
                _status.Update);
            Volatile.Write(ref _latest, state);
            EventRaising.RaiseEach(Published, this, state, LogListenerFailed);
        }
    }

    private async Task SendProgressAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            await _progressed.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
            List<JobView> jobs;
            lock (_gate)
            {
                jobs = [.. _progress.Values.Select(JobView.From)];
                _progress.Clear();
            }

            EventRaising.RaiseEach<IReadOnlyList<JobView>>(JobsProgressed, this, jobs, LogListenerFailed);
            await Task.Delay(ProgressInterval, _time, cancellationToken).ConfigureAwait(false);
        }
    }

    private void OnChanged<T>(object? sender, T change) => _changes.Writer.TryWrite(true);

    private void OnProgressed(object? sender, JobSnapshot job)
    {
        lock (_gate)
        {
            if (!_progress.TryGetValue(job.Id, out var known) || known.Sequence < job.Sequence)
            {
                _progress[job.Id] = job;
            }
        }

        _progressed.Writer.TryWrite(true);
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "A listener of the app state failed")]
    private partial void LogListenerFailed(Exception exception);
}
