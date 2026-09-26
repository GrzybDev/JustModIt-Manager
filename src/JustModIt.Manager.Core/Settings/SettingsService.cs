using System.Collections.Concurrent;
using System.Threading.Channels;
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

    private readonly IDocumentStore<AppSettings> _store;
    private readonly TimeProvider _time;
    private readonly ILogger<SettingsService> _logger;
    private readonly Lock _gate = new();
    private readonly ConcurrentQueue<SettingsChangedEventArgs> _undelivered = new();

    // Null asks for a save after the delay, a completion source for one right away.
    private readonly Channel<TaskCompletionSource?> _requests = Channel.CreateUnbounded<TaskCompletionSource?>();
    private readonly Task _writer;
    private AppSettings _current;
    private AppSettings _saved;
    private int _delivering;

    private SettingsService(IDocumentStore<AppSettings> store, AppSettings settings, TimeProvider time,
        ILogger<SettingsService> logger)
    {
        _store = store;
        _time = time;
        _logger = logger;
        _current = settings;
        _saved = settings;
        _writer = RunWriterAsync();
    }

    public event EventHandler<SettingsChangedEventArgs>? Changed;

    public event EventHandler? SaveFailed;

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
            // Queued under the lock, so notifications follow the order of the changes.
            _undelivered.Enqueue(new SettingsChangedEventArgs(previous, updated));
        }

        _requests.Writer.TryWrite(null);
        Deliver();
        return updated;
    }

    public Task FlushAsync(CancellationToken cancellationToken)
    {
        var saved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        // Once disposal began, the writer saves everything before it ends.
        return _requests.Writer.TryWrite(saved) ? saved.Task.WaitAsync(cancellationToken) : _writer.WaitAsync(cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        _requests.Writer.TryComplete();
        await _writer.ConfigureAwait(false);
    }

    private async Task RunWriterAsync()
    {
        var reader = _requests.Reader;
        var flushes = new List<TaskCompletionSource>();
        while (await reader.WaitToReadAsync().ConfigureAwait(false))
        {
            Take(reader, flushes);
            if (flushes.Count == 0)
            {
                await WaitForMoreChangesAsync(reader, flushes).ConfigureAwait(false);
            }

            var failure = await SaveLatestAsync().ConfigureAwait(false);
            foreach (var flush in flushes)
            {
                if (failure is null)
                {
                    flush.TrySetResult();
                }
                else
                {
                    flush.TrySetException(failure);
                }
            }

            flushes.Clear();
        }

        await SaveLatestAsync().ConfigureAwait(false);
    }

    private async Task WaitForMoreChangesAsync(ChannelReader<TaskCompletionSource?> reader, List<TaskCompletionSource> flushes)
    {
        using var delay = new CancellationTokenSource();
        var elapsed = Task.Delay(SaveDelay, _time, delay.Token);
        while (flushes.Count == 0)
        {
            var arrived = reader.WaitToReadAsync().AsTask();
            // A completed channel means the service is being disposed, which saves at once.
            if (await Task.WhenAny(elapsed, arrived).ConfigureAwait(false) == elapsed || !await arrived.ConfigureAwait(false))
            {
                break;
            }

            Take(reader, flushes);
        }

        await delay.CancelAsync().ConfigureAwait(false);
    }

    private static void Take(ChannelReader<TaskCompletionSource?> reader, List<TaskCompletionSource> flushes)
    {
        while (reader.TryRead(out var request))
        {
            if (request is not null)
            {
                flushes.Add(request);
            }
        }
    }

    private async Task<Exception?> SaveLatestAsync()
    {
        var latest = Current;
        if (latest.Equals(_saved))
        {
            return null;
        }

        try
        {
            await _store.SaveAsync(latest, CancellationToken.None).ConfigureAwait(false);
            _saved = latest;
            return null;
        }
        // The writer must outlive any failure, or later changes would never be saved.
        catch (Exception exception)
        {
            LogSaveFailed(exception);
            Notify(SaveFailed, handler => ((EventHandler)handler)(this, EventArgs.Empty));
            return exception;
        }
    }

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

    [LoggerMessage(Level = LogLevel.Warning, Message = "Settings could not be saved; they stay in effect and are saved with the next change")]
    private partial void LogSaveFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "A settings listener failed")]
    private partial void LogListenerFailed(Exception exception);
}
