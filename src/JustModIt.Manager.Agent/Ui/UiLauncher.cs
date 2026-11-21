using JustModIt.Manager.Agent.Ipc;
using JustModIt.Manager.Core.Platform;
using JustModIt.Manager.Protocol.Activation;
using JustModIt.Manager.Protocol.Messages;
using JustModIt.Manager.Protocol.Transport;
using JustModIt.Manager.Protocol.Views;
using Microsoft.Extensions.Logging;

namespace JustModIt.Manager.Agent.Ui;

/// <summary>
/// The window: brings the running one to the front, or starts its process, and keeps it up to date; the window's own
/// lock keeps it to one process, so a start while another is on its way only makes the new process hand over and end, and
/// where launches become the window, none is started while another process holds that lock; in a store's game session,
/// which shows only the programs the store starts, the agent starts none.
/// </summary>
public sealed partial class UiLauncher : IDisposable
{
    // How long a started window has to connect before another click starts it again.
    private static readonly TimeSpan StartGrace = TimeSpan.FromSeconds(15);

    // A window that agreed to close gets this long to go before the agent carries on without it.
    private static readonly TimeSpan CloseTimeout = TimeSpan.FromSeconds(30);

    private readonly IDesktopShell _shell;
    private readonly string _executable;
    private readonly string _uiLock;
    private readonly bool _launchesBecomeWindows;
    private readonly IForegroundHandoff? _foreground;
    private readonly StatePublisher _publisher;
    private readonly AgentStatus _status;
    private readonly TimeProvider _time;
    private readonly ILogger<UiLauncher> _logger;

    // Taking the window and sending to it share this lock, so a new window gets every snapshot from the newest on.
    private readonly Lock _gate = new();
    private NavigationTarget? _pendingTarget;
    private bool _pendingTargetWaited;
    private PipeConnection? _window;
    private PipeConnection? _closing;
    private DateTimeOffset? _startedAt;
    private bool _isActive;
    private int _toldStoreSession;

    // The page the window got as it connected, which goes to the window it becomes when it starts anew in the agent's version.
    private Navigate? _handedTarget;

    // The window said it closes to start anew, so its going is no close of the player's.
    private PipeConnection? _restarting;

    /// <summary>Creates the launcher, which forwards every snapshot, job progress and job end to the window from then on.</summary>
    /// <param name="shell">Starts the window's process.</param>
    /// <param name="options">
    /// The executable the window runs, the lock its process holds, and whether launches become the window.
    /// </param>
    /// <param name="publisher">The snapshots, job progress and job ends to forward.</param>
    /// <param name="status">Tells how the session brings the window back.</param>
    /// <param name="time">Times how long a started window has to connect.</param>
    /// <param name="logger">Receives the window's starts, connections and closes.</param>
    /// <param name="foreground">Lets the window take the foreground, where the system asks for that; null elsewhere.</param>
    public UiLauncher(IDesktopShell shell, AgentOptions options, StatePublisher publisher, AgentStatus status, TimeProvider time,
        ILogger<UiLauncher> logger, IForegroundHandoff? foreground = null)
    {
        _shell = shell;
        _executable = options.ExecutablePath;
        _uiLock = options.Names.UiLock;
        _launchesBecomeWindows = options.LaunchesBecomeWindows;
        _foreground = foreground;
        _publisher = publisher;
        _status = status;
        _time = time;
        _logger = logger;
        _publisher.Published += OnPublished;
        _publisher.JobsProgressed += OnJobsProgressed;
        _publisher.JobEnded += OnJobEnded;
    }

    /// <summary>
    /// The player closed the window; not raised when the agent asked it to close, nor for a window that said it starts anew in
    /// the agent's version.
    /// </summary>
    public event EventHandler? Closed;

    /// <summary>The connection of the running window; null while none runs.</summary>
    public PipeConnection? Window => Volatile.Read(ref _window);

    /// <summary>The window is in front, so notices belong in it rather than in the tray.</summary>
    public bool IsActive
    {
        get
        {
            lock (_gate)
            {
                return _window is not null && _isActive;
            }
        }
    }

    /// <summary>A window runs, or was started and has not connected yet.</summary>
    public bool IsOpenOrStarting
    {
        get
        {
            lock (_gate)
            {
                return _window is not null || (_startedAt is { } started && _time.GetUtcNow() - started <= StartGrace);
            }
        }
    }

    /// <summary>Shows the window, starting it when none runs.</summary>
    /// <param name="target">Where the window should go; null keeps the page it shows.</param>
    public void Show(NavigationTarget? target = null)
    {
        PipeConnection? window;
        var start = false;
        lock (_gate)
        {
            window = _window;
            if (window is null)
            {
                _pendingTarget = target ?? _pendingTarget;
                _pendingTargetWaited &= target is null;
                var now = _time.GetUtcNow();
                start = _startedAt is not { } started || now - started > StartGrace;
                if (start)
                {
                    _startedAt = now;
                }
            }
        }

        if (window is not null)
        {
            _foreground?.Allow(window.Peer.ProcessId);
            if (target is not null)
            {
                window.Post(new Navigate(target));
            }

            window.Post(new BringToFront());
        }
        else if (start)
        {
            StartWindow();
        }
    }

    /// <summary>Asks the window to close and waits until it has.</summary>
    /// <param name="reason">Why the agent asks: an exit or an update.</param>
    /// <param name="cancellationToken">Stops waiting for the window.</param>
    /// <returns>False when the window refused, e.g. to keep the player's unsaved work; true once none runs.</returns>
    public async Task<bool> CloseAsync(ShutdownReason reason, CancellationToken cancellationToken)
    {
        PipeConnection? window;
        lock (_gate)
        {
            window = _window;
            _closing = window;
        }

        if (window is null)
        {
            return true;
        }

        try
        {
            if (await window.RequestAsync(new ShutdownRequested(reason), CloseTimeout, cancellationToken).ConfigureAwait(false)
                is ShutdownAnswer { Accepted: false })
            {
                lock (_gate)
                {
                    _closing = null;
                }

                LogCloseRefused(reason);
                return false;
            }

            await window.Completion.WaitAsync(CloseTimeout, cancellationToken).ConfigureAwait(false);
        }
        // A window that does not answer, or no longer listens, must not keep the agent from exiting or updating.
        catch (Exception exception) when (exception is TimeoutException or ConnectionClosedException or RequestFailedException)
        {
            LogCloseUnanswered(exception.GetType().Name);
        }

        return true;
    }

    public void Dispose()
    {
        _publisher.Published -= OnPublished;
        _publisher.JobsProgressed -= OnJobsProgressed;
        _publisher.JobEnded -= OnJobEnded;
    }

    /// <summary>Takes a new window, which gets the newest snapshot at once, then the page that waited for it.</summary>
    /// <param name="window">The window's new connection.</param>
    internal void OnConnected(PipeConnection window)
    {
        LogConnected(window.Peer.ProcessId);
        _foreground?.Allow(window.Peer.ProcessId);
        lock (_gate)
        {
            _window = window;
            _startedAt = null;
            _isActive = false;
            window.Post(new StateChanged(_publisher.Latest));
            _handedTarget = _pendingTarget is { } target ? new Navigate(target) { Waited = _pendingTargetWaited } : null;
            _pendingTarget = null;
            if (_handedTarget is not null)
            {
                window.Post(_handedTarget);
            }
        }
    }

    /// <summary>Forgets a window that closed, raising <see cref="Closed"/> when the player closed it.</summary>
    /// <param name="window">The connection that closed; one other than the current window's is ignored.</param>
    internal void OnDisconnected(PipeConnection window)
    {
        bool closedByPlayer;
        lock (_gate)
        {
            if (_window != window)
            {
                return;
            }

            _window = null;
            _isActive = false;
            closedByPlayer = _closing != window && _restarting != window;
            _closing = null;
            _restarting = null;
        }

        LogDisconnected(window.Peer.ProcessId);
        if (closedByPlayer)
        {
            Closed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Takes the window's word that it closes to start anew in the agent's version.</summary>
    /// <param name="window">The window's connection; one other than the current window's is ignored.</param>
    internal void OnRestarting(PipeConnection window)
    {
        lock (_gate)
        {
            if (_window != window)
            {
                return;
            }

            _restarting = window;
            if (_handedTarget is { } handed && _pendingTarget is null)
            {
                _pendingTarget = handed.Target;
                _pendingTargetWaited = handed.Waited;
            }
        }
    }

    /// <summary>Records whether the window is in front.</summary>
    /// <param name="window">The window's connection; one other than the current window's is ignored.</param>
    /// <param name="isActive">Whether it is in front.</param>
    internal void OnActivity(PipeConnection window, bool isActive)
    {
        lock (_gate)
        {
            if (_window == window)
            {
                _isActive = isActive;
            }
        }
    }

    private void StartWindow()
    {
        // The session would not show the window, which would then hold the window's lock against the launch from the store's
        // library that the session does show.
        if (_status.Activity.WindowReturn == WindowReturn.StoreLibrary)
        {
            lock (_gate)
            {
                _startedAt = null;
                // The window that the store's library opens says that the page waited for it.
                _pendingTargetWaited = _pendingTarget is not null;
            }

            if (Interlocked.Exchange(ref _toldStoreSession, 1) == 0)
            {
                LogNoWindowInStoreSession();
            }

            return;
        }

        // A launch that becomes the window holds its lock before it connects, and then gets the page that waits for it. A
        // window that is closing holds it too, so the next activation looks again.
        if (_launchesBecomeWindows && InstanceLock.IsHeld(_uiLock))
        {
            lock (_gate)
            {
                _startedAt = null;
            }

            LogWindowOnItsWay();
            return;
        }

        if (_shell.Run(_executable, [LaunchArguments.UiSwitch]) is { } processId)
        {
            _foreground?.Allow(processId);
            LogStarted(processId);
            return;
        }

        lock (_gate)
        {
            _startedAt = null;
        }

        LogStartFailed();
    }

    private void OnPublished(object? sender, AppStateView state) => SendToWindow(new StateChanged(state));

    private void OnJobEnded(object? sender, JobEnded ended) => SendToWindow(ended);

    // The publisher gathers reports and sends them at its own pace, so one may come after the state that ended its job or
    // moved it on. The newest state, read under the lock that orders what the window gets, tells which are still news.
    private void OnJobsProgressed(object? sender, IReadOnlyList<JobView> jobs)
    {
        lock (_gate)
        {
            var listed = _publisher.Latest.Jobs;
            List<JobView> news = [.. jobs.Where(job => listed.Any(known => known.Id == job.Id && known.Sequence < job.Sequence))];
            if (news.Count > 0)
            {
                _window?.Post(new JobProgress(news));
            }
        }
    }

    private void SendToWindow(Message message)
    {
        lock (_gate)
        {
            _window?.Post(message);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Started the window as process {ProcessId}")]
    private partial void LogStarted(int processId);

    [LoggerMessage(Level = LogLevel.Information, Message = "A window's process is on its way, so no other is started")]
    private partial void LogWindowOnItsWay();

    [LoggerMessage(Level = LogLevel.Information,
        Message = "The session shows only the programs its store starts, so the window opens from the store's library alone")]
    private partial void LogNoWindowInStoreSession();

    [LoggerMessage(Level = LogLevel.Error, Message = "The window could not be started")]
    private partial void LogStartFailed();

    [LoggerMessage(Level = LogLevel.Information, Message = "The window {ProcessId} connected")]
    private partial void LogConnected(int processId);

    [LoggerMessage(Level = LogLevel.Information, Message = "The window {ProcessId} closed")]
    private partial void LogDisconnected(int processId);

    [LoggerMessage(Level = LogLevel.Information, Message = "The window refused to close for {Reason}")]
    private partial void LogCloseRefused(ShutdownReason reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The window did not close as asked ({Error}); carrying on without it")]
    private partial void LogCloseUnanswered(string error);
}
