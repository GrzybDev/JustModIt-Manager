using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace JustModIt.Manager.Core.IO;

/// <summary>
/// Saves a document on a background writer, one save at a time and only the latest version, so its owner never waits
/// for the disk and two saves never race each other; disposing it saves what is still queued.
/// </summary>
public sealed partial class LatestDocumentWriter<T> : IAsyncDisposable
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

    private readonly Task _writer;

    /// <summary>Starts the background writer.</summary>
    /// <param name="store">Where the document is saved.</param>
    /// <param name="name">What the document is, for the log.</param>
    /// <param name="logger">Logs failed saves.</param>
    public LatestDocumentWriter(IDocumentStore<T> store, string name, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(logger);
        _store = store;
        _name = name;
        _logger = logger;
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
        _pending.Writer.TryComplete();
        await _writer.ConfigureAwait(false);
    }

    private async Task WriteAsync()
    {
        await foreach (var document in _pending.Reader.ReadAllAsync().ConfigureAwait(false))
        {
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
