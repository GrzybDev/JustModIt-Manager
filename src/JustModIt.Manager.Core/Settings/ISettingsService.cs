namespace JustModIt.Manager.Core.Settings;

public interface ISettingsService
{
    /// <summary>The settings in effect now.</summary>
    AppSettings Current { get; }

    /// <summary>
    /// Raised once per real change, in order and outside any lock, usually on the thread that made the change; a change
    /// made concurrently or from a handler may be reported after its <see cref="Update"/> returned.
    /// </summary>
    event EventHandler<SettingsChangedEventArgs>? Changed;

    /// <summary>
    /// Raised on the background writer when saving failed, while the settings stay in effect and saving is tried again
    /// with the next change or flush.
    /// </summary>
    event EventHandler? SaveFailed;

    /// <summary>
    /// Applies the change in memory at once and saves it in the background; a change that leaves the settings equal does
    /// nothing.
    /// </summary>
    /// <param name="change">A quick, pure function: it runs under the service's lock.</param>
    /// <returns>The settings in effect after the change.</returns>
    AppSettings Update(Func<AppSettings, AppSettings> change);

    /// <summary>Saves pending changes now instead of after the usual delay; fails when that save fails.</summary>
    /// <param name="cancellationToken">Stops waiting for the save, which still happens.</param>
    Task FlushAsync(CancellationToken cancellationToken);
}

/// <summary>The settings before and after a change.</summary>
/// <param name="previous">The settings before the change.</param>
/// <param name="current">The settings after it.</param>
public sealed class SettingsChangedEventArgs(AppSettings previous, AppSettings current) : EventArgs
{
    public AppSettings Previous { get; } = previous;

    public AppSettings Current { get; } = current;
}
