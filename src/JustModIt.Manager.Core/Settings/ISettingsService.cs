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
    /// Applies the change in memory at once and saves it in the background; a change that leaves the settings equal does
    /// nothing.
    /// </summary>
    /// <param name="change">A quick, pure function: it runs under the service's lock.</param>
    /// <returns>The settings in effect after the change.</returns>
    AppSettings Update(Func<AppSettings, AppSettings> change);
}

/// <summary>What the settings service offers beyond its interface.</summary>
public static class SettingsServiceExtensions
{
    /// <summary>The id of this installation of the app, made the first time it is asked for.</summary>
    /// <param name="settings">The app's settings, which keep the id.</param>
    public static Guid InstallationId(this ISettingsService settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return settings.Current.InstallationId
            ?? settings.Update(current => current.InstallationId is null ? current with { InstallationId = Guid.NewGuid() } : current)
                .InstallationId!.Value;
    }
}

/// <summary>The settings before and after a change.</summary>
/// <param name="previous">The settings before the change.</param>
/// <param name="current">The settings after it.</param>
public sealed class SettingsChangedEventArgs(AppSettings previous, AppSettings current) : EventArgs
{
    public AppSettings Previous { get; } = previous;

    public AppSettings Current { get; } = current;
}
