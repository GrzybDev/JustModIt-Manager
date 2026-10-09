using JustModIt.Manager.Core.Collections;
using JustModIt.Manager.Core.Games;

namespace JustModIt.Manager.Core.Library.Gog;

/// <summary>What one launcher or installer recorded about an install; the folder itself is checked afterwards.</summary>
/// <param name="GameId">A GOG product id, kept as text because GOG ids exceed 2^53.</param>
/// <param name="InstallPath">The folder the launcher or installer installed into, as it recorded it.</param>
/// <param name="LibraryId">The <see cref="LibraryLocation.Id"/> of the source that holds the record.</param>
public sealed record GogInstallRecord(string GameId, string InstallPath, string LibraryId)
{
    public string? Name { get; init; }

    public string? BuildId { get; init; }

    public bool IsDlc { get; init; }

    /// <summary>The installed language as the source writes it: a tag or an English name.</summary>
    public string? Language { get; init; }

    public OperatingSystemKind Platform { get; init; }

    /// <summary>How the source starts its game; null to start the primary play task of its <c>.info</c> file.</summary>
    public GameLaunch? Launch { get; init; }

    /// <summary>What the launcher reports doing with the game, such as updating it; null when it reports nothing.</summary>
    public GameInstallState? Activity { get; init; }
}

/// <param name="Libraries">The source's own library, none when the source does not exist on this machine.</param>
/// <param name="Records">The installs it records.</param>
/// <param name="Watch">What to watch so that changes to its records trigger the next scan.</param>
public sealed record GogSourceScan(ValueList<LibraryLocation> Libraries, IReadOnlyList<GogInstallRecord> Records, WatchPlan Watch);

/// <summary>A launcher or installer that records GOG installs. Another one (such as Minigalaxy) is one more source.</summary>
public interface IGogInstallSource
{
    /// <summary>
    /// Reads the records through <see cref="LibraryScanContext.Access"/>. A source that exists but cannot be read reports
    /// its library as unreadable.
    /// </summary>
    Task<GogSourceScan> ScanAsync(LibraryScanContext context, CancellationToken cancellationToken);
}
