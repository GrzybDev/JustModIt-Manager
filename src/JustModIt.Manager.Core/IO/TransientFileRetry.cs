namespace JustModIt.Manager.Core.IO;

/// <summary>
/// Retries an operation on a file the app owns while an antivirus scanner, indexer or backup tool briefly holds it, never
/// on game files, which another program may keep open for long.
/// </summary>
internal static class TransientFileRetry
{
    private static readonly TimeSpan[] Delays =
    [
        TimeSpan.FromMilliseconds(50),
        TimeSpan.FromMilliseconds(100),
        TimeSpan.FromMilliseconds(200),
        TimeSpan.FromMilliseconds(400),
        TimeSpan.FromMilliseconds(800),
        TimeSpan.FromMilliseconds(1600),
    ];

    /// <summary>Runs <paramref name="operation"/>, trying again with growing delays while it fails transiently.</summary>
    /// <param name="operation">An operation on a file the app owns.</param>
    /// <param name="time">Paces the delays.</param>
    /// <param name="cancellationToken">Stops the retries.</param>
    public static Task RunAsync(Action operation, TimeProvider time, CancellationToken cancellationToken) =>
        RunAsync(() =>
        {
            operation();
            return Task.FromResult(true);
        }, time, cancellationToken);

    /// <summary>Runs <paramref name="operation"/>, trying again with growing delays while it fails transiently.</summary>
    /// <param name="operation">An operation on a file the app owns.</param>
    /// <param name="time">Paces the delays.</param>
    /// <param name="cancellationToken">Stops the retries.</param>
    public static async Task<T> RunAsync<T>(Func<Task<T>> operation, TimeProvider time, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(time);

        for (var attempt = 0; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return await operation().ConfigureAwait(false);
            }
            catch (Exception exception) when (attempt < Delays.Length && IsTransient(exception))
            {
                await Task.Delay(Delays[attempt], time, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>Tells whether <paramref name="exception"/> comes from a file that another program holds for a moment.</summary>
    /// <param name="exception">The exception a file operation threw.</param>
    public static bool IsTransient(Exception exception) =>
        exception is UnauthorizedAccessException
        || (exception is IOException io && (IOErrors.IsAccessDenied(io) || IOErrors.IsSharingViolation(io)));
}
