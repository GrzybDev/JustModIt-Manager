using JustModIt.Manager.Core.Games;

namespace JustModIt.Manager.Core.Library.Gog;

/// <summary>What a GOG launcher or installer recorded about an install.</summary>
/// <param name="GameId">A GOG product id, kept as text because GOG ids exceed 2^53.</param>
/// <param name="InstallPath">The folder the launcher or installer installed into, as it recorded it.</param>
/// <param name="LibraryId">The <see cref="LibraryLocation.Id"/> of the source that holds the record.</param>
public sealed record GogInstallRecord(string GameId, string InstallPath, string LibraryId) : IInstallRecord
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
