using Microsoft.Extensions.Logging;

namespace JustModIt.Manager.Core.Library.Steam;

// Paths are never logged: they lie in the user's profile or name the user's folders.
internal static partial class SteamLog
{
    /// <summary>
    /// Describes <paramref name="exception"/> without a path: by its message for a parse error, which names only the
    /// problem and the line, and by its type otherwise, since I/O messages quote the full path.
    /// </summary>
    /// <param name="exception">A failure to read one of Steam's files.</param>
    public static string Describe(Exception exception) =>
        exception is FormatException ? exception.Message : exception.GetType().Name;

    /// <summary>Logs a library list that could not be read.</summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="error">What failed, from <see cref="Describe"/>.</param>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Cannot read a Steam library list: {Error}")]
    public static partial void LibraryListUnreadable(this ILogger logger, string error);

    /// <summary>Logs a library entry left out for its path.</summary>
    /// <param name="logger">The logger to write to.</param>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Ignoring a Steam library entry whose path is not absolute")]
    public static partial void LibraryPathInvalid(this ILogger logger);

    /// <summary>Logs a library that could not be listed.</summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="error">What failed, from <see cref="Describe"/>.</param>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Cannot list a Steam library, treating it as offline: {Error}")]
    public static partial void LibraryUnreadable(this ILogger logger, string error);

    /// <summary>Logs a folder that did not answer in time.</summary>
    /// <param name="logger">The logger to write to.</param>
    [LoggerMessage(Level = LogLevel.Warning, Message = "A Steam folder did not answer in time, treating it as offline")]
    public static partial void FolderNotAnswering(this ILogger logger);

    /// <summary>Logs an app whose manifest was left out.</summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="appId">The Steam app id.</param>
    /// <param name="reason">What failed, from <see cref="Describe"/>.</param>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Skipped Steam app {AppId}: {Reason}")]
    public static partial void AppSkipped(this ILogger logger, uint appId, string reason);

    /// <summary>Logs what Steam is doing with an app.</summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="appId">The Steam app id.</param>
    /// <param name="status">The app's state and flags, from <see cref="SteamInstallStatus.Describe"/>.</param>
    [LoggerMessage(Level = LogLevel.Debug, Message = "Steam app {AppId}: {Status}")]
    public static partial void AppStatus(this ILogger logger, uint appId, string status);

    /// <summary>Logs a finished scan.</summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="gameCount">The games found.</param>
    /// <param name="libraryCount">The libraries listed.</param>
    /// <param name="offlineCount">The libraries that are not online.</param>
    [LoggerMessage(Level = LogLevel.Debug, Message = "Steam scan found {GameCount} games in {LibraryCount} libraries, {OfflineCount} offline")]
    public static partial void ScanCompleted(this ILogger logger, int gameCount, int libraryCount, int offlineCount);
}
