using JustModIt.Manager.Protocol.Views;

namespace JustModIt.Manager.Agent;

/// <summary>
/// What the agent itself is busy with, beyond the engine's state: checks, a pending exit, its own updates and how its
/// previous run ended; it starts with nothing going on and no update found.
/// </summary>
/// <param name="options">The app's options, for its current version and how its previous run ended.</param>
public sealed class AgentStatus(AgentOptions options)
{
    private readonly Lock _gate = new();
    private ActivityView _activity = new();
    private AppUpdateView _update = new() { CurrentVersion = options.Version };
    private UncleanExitView? _uncleanExit = options.PreviousExit is { } exit ? new UncleanExitView(exit.Version, exit.CrashType) : null;

    /// <summary>Raised after every change, outside the lock; handlers must return quickly.</summary>
    public event EventHandler? Changed;

    public ActivityView Activity
    {
        get
        {
            lock (_gate)
            {
                return _activity;
            }
        }
    }

    public AppUpdateView Update
    {
        get
        {
            lock (_gate)
            {
                return _update;
            }
        }
    }

    /// <summary>How the previous run ended when it did not exit cleanly, until the window has told the player.</summary>
    public UncleanExitView? UncleanExit
    {
        get
        {
            lock (_gate)
            {
                return _uncleanExit;
            }
        }
    }

    /// <summary>Forgets how the previous run ended, once the window has told the player.</summary>
    public void ForgetUncleanExit() => Change(ref _uncleanExit, _ => null);

    /// <summary>Changes the activity under the lock, raising <see cref="Changed"/> when it differs.</summary>
    /// <param name="change">Makes the new activity from the current one; it runs under the lock.</param>
    public void ChangeActivity(Func<ActivityView, ActivityView> change) => Change(ref _activity, change);

    /// <summary>Changes the update's status under the lock, raising <see cref="Changed"/> when it differs.</summary>
    /// <param name="change">Makes the new status from the current one; it runs under the lock.</param>
    public void ChangeUpdate(Func<AppUpdateView, AppUpdateView> change) => Change(ref _update, change);

    private void Change<T>(ref T field, Func<T, T> change)
        where T : class?
    {
        ArgumentNullException.ThrowIfNull(change);
        bool changed;
        lock (_gate)
        {
            var updated = change(field);
            changed = !Equals(updated, field);
            field = updated;
        }

        if (changed)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }
}
