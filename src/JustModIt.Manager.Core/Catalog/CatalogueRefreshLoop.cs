using System.Security.Cryptography;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace JustModIt.Manager.Core.Catalog;

/// <summary>
/// Refreshes the catalogue after start, whenever asked, every six hours and a quarter of an hour after a failure, with no
/// other timer, so the app stays idle in the tray.
/// </summary>
/// <param name="service">The catalogue to refresh.</param>
/// <param name="request">Gives the games and installs to index at each refresh.</param>
/// <param name="time">The clock for the waits.</param>
/// <param name="logger">Logs a refresh that failed unexpectedly.</param>
public sealed partial class CatalogueRefreshLoop(
    CatalogueService service,
    Func<IndexRequest> request,
    TimeProvider time,
    ILogger<CatalogueRefreshLoop> logger)
{
    public static readonly TimeSpan Interval = TimeSpan.FromHours(6);
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMinutes(15);

    // Requests made while a refresh runs fold into one refresh after it.
    private readonly Channel<bool> _requests = Channel.CreateBounded<bool>(new BoundedChannelOptions(1)
    {
        FullMode = BoundedChannelFullMode.DropWrite,
    });

    /// <summary>Refreshes as soon as possible, e.g. when the player asks for it.</summary>
    public void RequestRefresh() => _requests.Writer.TryWrite(true);

    /// <summary>Refreshes the catalogue until cancelled.</summary>
    /// <param name="startDelay">
    /// How long to wait before the first refresh, longer for a start with the system, so the app does not compete with
    /// everything else starting.
    /// </param>
    /// <param name="cancellationToken">Stops the loop.</param>
    public async Task RunAsync(TimeSpan startDelay, CancellationToken cancellationToken)
    {
        var delay = startDelay;
        while (true)
        {
            await WaitAsync(delay, cancellationToken).ConfigureAwait(false);
            try
            {
                delay = NextDelay(await service.RefreshAsync(request(), cancellationToken).ConfigureAwait(false));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            // The loop must outlive a bug in one refresh, or the catalogue would never be refreshed again.
            catch (Exception exception)
            {
                LogRefreshCrashed(exception);
                delay = Jitter(RetryDelay);
            }
        }
    }

    /// <summary>Six hours after a success, otherwise a quarter of an hour or as long as the site asked, each ±10%.</summary>
    /// <param name="snapshot">The catalogue after the last refresh.</param>
    internal static TimeSpan NextDelay(CatalogueSnapshot snapshot) =>
        Jitter(snapshot.State == CatalogueState.Fresh
            ? Interval
            : snapshot.RetryAfter is { } asked && asked > RetryDelay ? asked : RetryDelay);

    private static TimeSpan Jitter(TimeSpan delay) => delay * (0.9 + (RandomNumberGenerator.GetInt32(201) / 1000.0));

    private async Task WaitAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        await time.WaitForSignalAsync(token => _requests.Reader.WaitToReadAsync(token).AsTask(), delay, cancellationToken)
            .ConfigureAwait(false);
        while (_requests.Reader.TryRead(out _))
        {
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "A catalogue refresh failed unexpectedly")]
    private partial void LogRefreshCrashed(Exception exception);
}
