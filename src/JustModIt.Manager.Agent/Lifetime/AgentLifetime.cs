using JustModIt.Manager.Agent.Ui;
using JustModIt.Manager.Core.Jobs;
using JustModIt.Manager.Protocol.Messages;
using Microsoft.Extensions.Logging;

namespace JustModIt.Manager.Agent.Lifetime;

/// <summary>
/// Decides when the agent exits: the window is asked first, since it may keep the player's unsaved work, then running
/// jobs are either cancelled, which undoes what they changed, or waited for while no new ones start.
/// </summary>
public sealed partial class AgentLifetime : IDisposable
{
    private readonly UiLauncher _ui;
    private readonly JobRunner _jobs;
    private readonly AgentStatus _status;
    private readonly ILogger<AgentLifetime> _logger;
    private readonly TaskCompletionSource _exited = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly CancellationTokenSource _stopping = new();
    private int _exiting;
    private int _leaving;

    /// <summary>Creates the lifetime; the agent runs until <see cref="Exited"/> completes.</summary>
    /// <param name="ui">Asks the window to close.</param>
    /// <param name="jobs">The running jobs, cancelled or waited for.</param>
    /// <param name="status">Shows that an exit waits for the jobs.</param>
    /// <param name="logger">Receives the exit.</param>
    public AgentLifetime(UiLauncher ui, JobRunner jobs, AgentStatus status, ILogger<AgentLifetime> logger)
    {
        ArgumentNullException.ThrowIfNull(ui);
        ArgumentNullException.ThrowIfNull(jobs);
        ArgumentNullException.ThrowIfNull(status);
        ArgumentNullException.ThrowIfNull(logger);
        _ui = ui;
        _jobs = jobs;
        _status = status;
        _logger = logger;
    }

    /// <summary>Completes once the agent should stop.</summary>
    public Task Exited => _exited.Task;

    /// <summary>An exit is decided, even if it still waits for jobs.</summary>
    public bool IsExiting => Volatile.Read(ref _exiting) != 0;

    /// <summary>
    /// The agent is past waiting for jobs and leaves; a later launch waits for this process to end and then takes over,
    /// rather than being handed to an agent that can no longer show the window.
    /// </summary>
    public bool IsLeaving => Volatile.Read(ref _leaving) != 0;

    /// <summary>Exits from the tray: at once when nothing runs, otherwise the window asks the player how.</summary>
    public void ExitFromTray()
    {
        var running = _jobs.Active.Count;
        if (running == 0)
        {
            _ = RequestExitAsync(ExitMode.Now);
        }
        else
        {
            _ui.Show(null, new ExitConfirmationRequested(running));
        }
    }

    /// <summary>Exits once the window agrees to close.</summary>
    /// <param name="mode">
    /// Whether running jobs are cancelled or waited for; <see cref="ExitMode.Now"/> also cuts short an exit that waits.
    /// </param>
    /// <returns>False when the window refused to close, and the agent keeps running, or an exit was decided already.</returns>
    public async Task<bool> RequestExitAsync(ExitMode mode)
    {
        if (!IsExiting && !await _ui.CloseAsync(ShutdownReason.Exit, _stopping.Token).ConfigureAwait(false))
        {
            return false;
        }

        if (Interlocked.Exchange(ref _exiting, 1) != 0)
        {
            if (mode == ExitMode.Now)
            {
                _jobs.Shutdown(cancelPlayerJobs: true);
            }

            return false;
        }

        LogExiting(mode, _jobs.Active.Count);
        if (mode == ExitMode.AfterJobs && _jobs.IsBusy)
        {
            _status.ChangeActivity(activity => activity with { IsExitPending = true });
            _jobs.Shutdown(cancelPlayerJobs: false);
            await _jobs.WhenIdleAsync(_stopping.Token).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            Volatile.Write(ref _leaving, 1);
            // The player may have opened the window meanwhile; one that refuses now keeps running on its own, and with it
            // the player's unsaved work.
            await ((Task)_ui.CloseAsync(ShutdownReason.Exit, _stopping.Token)).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }
        else
        {
            Volatile.Write(ref _leaving, 1);
            _jobs.Shutdown(cancelPlayerJobs: true);
        }

        _exited.TrySetResult();
        return true;
    }

    /// <summary>Exits so that an update can replace the app; whoever calls it has already dealt with the window.</summary>
    public void ExitForUpdate()
    {
        Interlocked.Exchange(ref _exiting, 1);
        Volatile.Write(ref _leaving, 1);
        LogExitingForUpdate();
        _jobs.Shutdown(cancelPlayerJobs: true);
        _exited.TrySetResult();
    }

    /// <summary>Stops waiting for anything, as the agent is being torn down.</summary>
    internal void Abandon()
    {
        _stopping.Cancel();
        _exited.TrySetResult();
    }

    public void Dispose() => _stopping.Dispose();

    [LoggerMessage(Level = LogLevel.Information, Message = "Exiting ({Mode}) with {Jobs} jobs running")]
    private partial void LogExiting(ExitMode mode, int jobs);

    [LoggerMessage(Level = LogLevel.Information, Message = "Exiting to apply an update")]
    private partial void LogExitingForUpdate();
}
