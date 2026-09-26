using Microsoft.Extensions.Logging;

namespace JustModIt.Manager.Core.IO;

internal static partial class DocumentStoreLog
{
    /// <summary>Logs a document that could not be read and counts as absent.</summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="document">The document's name.</param>
    /// <param name="exception">What failed.</param>
    [LoggerMessage(Level = LogLevel.Warning, Message = "{Document} could not be read, so it is treated as absent")]
    public static partial void DocumentUnreadable(this ILogger logger, string document, Exception exception);

    /// <summary>Logs an unreadable document moved aside.</summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="document">The document's name.</param>
    /// <param name="backup">The file name it was kept under.</param>
    [LoggerMessage(Level = LogLevel.Information, Message = "{Document} was kept as {Backup}")]
    public static partial void DocumentMovedAside(this ILogger logger, string document, string backup);

    /// <summary>Logs an unreadable document that the next save will replace.</summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="document">The document's name.</param>
    /// <param name="exception">What failed.</param>
    [LoggerMessage(Level = LogLevel.Warning, Message = "{Document} could not be moved aside, so the next save replaces it")]
    public static partial void DocumentNotMovedAside(this ILogger logger, string document, Exception exception);
}
