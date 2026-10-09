using System.Collections.Concurrent;
using JustModIt.Manager.Core.IO;
using Microsoft.Extensions.Logging;

namespace JustModIt.Manager.Core.Settings;

/// <summary>
/// Holds the settings in memory and saves them on a background writer, so a change never waits for the disk: toggling
/// a setting from the UI must not freeze it on a slow drive or under an antivirus scan.
/// </summary>
public sealed partial class SettingsService : ISettingsService, IAsyncDisposable
{
    /// <summary>Merges a burst of changes into one write while keeping what a crash can lose small.</summary>
    public static readonly TimeSpan SaveDelay = TimeSpan.FromMilliseconds(300);

    private readonly LatestDocumentWriter<AppSettings> _writer;
    private readonly ILogger<SettingsService> _logger;
    private readonly Lock _gate = new();
    private readonly ConcurrentQueue<SettingsChangedEventArgs> _undelivered = new();
    private AppSettings _current;
    private int _delivering;

    private SettingsService(IDocumentStore<AppSettings> store, AppSettings settings, TimeProvider time,
        ILogger<SettingsService> logger)
    {
        _writer = new LatestDocumentWriter<AppSettings>(store, "settings", logger, SaveDelay, time);
        _logger = logger;
        _current = settings;
    }

    public event EventHandler<SettingsChangedEventArgs>? Changed;

    public AppSettings Current => Volatile.Read(ref _current);

    /// <summary>Starts with the stored settings, or with the defaults when none can be read.</summary>
    /// <param name="store">Where the settings are kept.</param>
    /// <param name="time">The clock for the save delay.</param>
    /// <param name="logger">Logs failed saves and failed listeners.</param>
    /// <param name="cancellationToken">Cancels the load.</param>
    public static async Task<SettingsService> LoadAsync(IDocumentStore<AppSettings> store, TimeProvider time,
        ILogger<SettingsService> logger, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(logger);
        var settings = await store.LoadAsync(cancellationToken).ConfigureAwait(false);
        return new SettingsService(store, settings ?? AppSettings.Default, time, logger);
    }

    /// <summary>Starts with the settings in the app's data folder, or with the defaults when none can be read.</summary>
    /// <param name="locations">The app's folders, which hold the settings file.</param>
    /// <param name="time">The clock for the save delay.</param>
    /// <param name="loggers">Creates the loggers of the service and its store.</param>
    /// <param name="cancellationToken">Cancels the load.</param>
    public static Task<SettingsService> LoadAsync(AppDataLocations locations, TimeProvider time, ILoggerFactory loggers,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(locations);
        ArgumentNullException.ThrowIfNull(loggers);
        var store = new JsonDocumentStore<AppSettings>(locations.SettingsFile, SettingsFormat.Instance, time,
            loggers.CreateLogger<JsonDocumentStore<AppSettings>>());
        return LoadAsync(store, time, loggers.CreateLogger<SettingsService>(), cancellationToken);
    }

    public AppSettings Update(Func<AppSettings, AppSettings> change)
    {
        ArgumentNullException.ThrowIfNull(change);
        AppSettings updated;
        lock (_gate)
        {
            var previous = _current;
            updated = change(previous) ?? throw new InvalidOperationException("A settings change returned no settings.");
            if (updated.Equals(previous))
            {
                return previous;
            }

            Volatile.Write(ref _current, updated);
            // Queued under the lock, so notifications and saves follow the order of the changes.
            _undelivered.Enqueue(new SettingsChangedEventArgs(previous, updated));
            _writer.Save(updated);
        }

        Deliver();
        return updated;
    }

    public ValueTask DisposeAsync() => _writer.DisposeAsync();

    private void Deliver()
    {
        // One thread delivers at a time, so listeners hear of the changes in order and never concurrently.
        while (!_undelivered.IsEmpty && Interlocked.CompareExchange(ref _delivering, 1, 0) == 0)
        {
            try
            {
                while (_undelivered.TryDequeue(out var change))
                {
                    Notify(Changed, handler => ((EventHandler<SettingsChangedEventArgs>)handler)(this, change));
                }
            }
            finally
            {
                Volatile.Write(ref _delivering, 0);
            }
        }
    }

    private void Notify(Delegate? handlers, Action<Delegate> invoke)
    {
        foreach (var handler in handlers?.GetInvocationList() ?? [])
        {
            try
            {
                invoke(handler);
            }
            catch (Exception exception)
            {
                LogListenerFailed(exception);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "A settings listener failed")]
    private partial void LogListenerFailed(Exception exception);
}
