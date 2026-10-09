using JustModIt.Manager.Protocol.Views;

namespace JustModIt.Manager.Agent;

/// <summary>
/// What the agent itself is busy with, beyond the engine's state: checks, a pending exit and its own updates; it starts with
/// nothing going on and no update found.
/// </summary>
/// <param name="options">The app's options, for its current version.</param>
public sealed class AgentStatus(AgentOptions options)
{
    private readonly Lock _gate = new();
    private ActivityView _activity = new();
    private AppUpdateView _update = new() { CurrentVersion = options.Version };

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

    /// <summary>Changes the activity under the lock, raising <see cref="Changed"/> when it differs.</summary>
    /// <param name="change">Makes the new activity from the current one; it runs under the lock.</param>
    public void ChangeActivity(Func<ActivityView, ActivityView> change) => Change(ref _activity, change);

    /// <summary>Changes the update's status under the lock, raising <see cref="Changed"/> when it differs.</summary>
    /// <param name="change">Makes the new status from the current one; it runs under the lock.</param>
    public void ChangeUpdate(Func<AppUpdateView, AppUpdateView> change) => Change(ref _update, change);

    private void Change<T>(ref T field, Func<T, T> change)
        where T : class
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
