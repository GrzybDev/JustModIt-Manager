using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace JustModIt.Manager.Core.IO;

/// <summary>
/// Saves a document on a background writer, one save at a time and only the latest version, so its owner never waits
/// for the disk and two saves never race each other; disposing it saves what is still queued at once.
/// </summary>
internal sealed partial class LatestDocumentWriter<T> : IAsyncDisposable
    where T : class
{
    private readonly IDocumentStore<T> _store;
    private readonly ILogger _logger;
    private readonly string _name;
    private readonly Channel<T> _pending = Channel.CreateBounded<T>(new BoundedChannelOptions(1)
    {
        FullMode = BoundedChannelFullMode.DropOldest,
        SingleReader = true,
    });

    private readonly TimeSpan _delay;
    private readonly TimeProvider _time;
    private readonly CancellationTokenSource _disposing = new();
    private readonly Task _writer;

    /// <summary>Starts the background writer.</summary>
    /// <param name="store">Where the document is saved.</param>
    /// <param name="name">What the document is, for the log.</param>
    /// <param name="logger">Logs failed saves.</param>
    /// <param name="delay">How long a save waits for newer versions, merging a burst of changes into one write.</param>
    /// <param name="time">The clock for the delay; the system clock when null.</param>
    public LatestDocumentWriter(IDocumentStore<T> store, string name, ILogger logger, TimeSpan delay = default,
        TimeProvider? time = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(logger);
        _store = store;
        _name = name;
        _logger = logger;
        _delay = delay;
        _time = time ?? TimeProvider.System;
        _writer = Task.Run(WriteAsync);
    }

    /// <summary>Queues the document to be saved; a version queued earlier and not saved yet is dropped.</summary>
    /// <param name="document">The latest version of the document.</param>
    public void Save(T document)
    {
        ArgumentNullException.ThrowIfNull(document);
        _pending.Writer.TryWrite(document);
    }

    public async ValueTask DisposeAsync()
    {
        if (_pending.Writer.TryComplete())
        {
            await _disposing.CancelAsync().ConfigureAwait(false);
        }

        await _writer.ConfigureAwait(false);
    }

    private async Task WriteAsync()
    {
        var reader = _pending.Reader;
        while (await reader.WaitToReadAsync().ConfigureAwait(false))
        {
            if (_delay > TimeSpan.Zero)
            {
                await Task.Delay(_delay, _time, _disposing.Token).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            }

            if (!reader.TryRead(out var document))
            {
                continue;
            }

            try
            {
                await _store.SaveAsync(document, CancellationToken.None).ConfigureAwait(false);
            }
            // The writer must outlive a full or read-only disk; the next change saves again.
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                LogSaveFailed(_name, exception);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The {Document} could not be saved")]
    private partial void LogSaveFailed(string document, Exception exception);
}
