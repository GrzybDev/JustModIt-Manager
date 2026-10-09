using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.IO;

namespace JustModIt.Manager.Core.Library.Epic;

/// <summary>What the Epic Games Launcher or Legendary recorded about an install.</summary>
/// <param name="GameId">The base game's catalog namespace, which DLC shares and which keys the game.</param>
/// <param name="InstallPath">The folder the launcher installed into, as it recorded it.</param>
/// <param name="LibraryId">The <see cref="LibraryLocation.Id"/> of the source that holds the record.</param>
public sealed record EpicInstallRecord(string GameId, string InstallPath, string LibraryId) : IInstallRecord
{
    /// <summary>The launcher's id of the installed app, which starts it.</summary>
    public required string AppName { get; init; }

    public string? CatalogItemId { get; init; }

    public string? Name { get; init; }

    public string? BuildId { get; init; }

    public bool IsDlc { get; init; }

    public OperatingSystemKind Platform { get; init; }

    public GameLaunch? Launch { get; init; }

    /// <summary>What the launcher reports doing with the game, such as updating it; null when it reports nothing.</summary>
    public GameInstallState? Activity { get; init; }

    /// <summary>When the launcher last wrote what it records about the game.</summary>
    public DateTimeOffset? ActivityAt { get; init; }

    /// <summary>
    /// Keeps the games among <paramref name="records"/>, each with what its launcher reports doing with the DLC installed
    /// into its folder, since that writes the game's folder too.
    /// </summary>
    /// <param name="records">Every record of one source, games and DLC.</param>
    internal static List<EpicInstallRecord> FoldDlc(IReadOnlyList<EpicInstallRecord> records) =>
    [
        .. records.Where(record => !record.IsDlc).Select(game =>
        {
            List<EpicInstallRecord> dlc =
                [.. records.Where(record => record.IsDlc && PathComparison.AreSame(record.InstallPath, game.InstallPath))];
            return game with
            {
                Activity = game.Activity ?? dlc.Select(record => record.Activity).FirstOrDefault(activity => activity is not null),
                ActivityAt = dlc.Select(record => record.ActivityAt).Append(game.ActivityAt).Max(),
            };
        }),
    ];
}
