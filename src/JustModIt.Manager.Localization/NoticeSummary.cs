namespace JustModIt.Manager.Localization;

/// <summary>
/// The automation's summary of one look at the player's games, worded the same in the tray and in the window: how many
/// have a project to install, an update or a repair, each in a sentence of its own.
/// </summary>
public static class NoticeSummary
{
    /// <summary>Words a summary; a game with an update or a repair is counted there and not among those to install.</summary>
    /// <param name="games">Every game the summary is about.</param>
    /// <param name="updates">Those of <paramref name="games"/> with an update.</param>
    /// <param name="repairs">Those of <paramref name="games"/> whose install needs a repair.</param>
    /// <typeparam name="TGame">How the games are told apart.</typeparam>
    public static string Format<TGame>(IReadOnlyCollection<TGame> games, IReadOnlyCollection<TGame> updates,
        IReadOnlyCollection<TGame> repairs)
    {
        ArgumentNullException.ThrowIfNull(games);
        ArgumentNullException.ThrowIfNull(updates);
        ArgumentNullException.ThrowIfNull(repairs);
        (string Pattern, int Count)[] parts =
        [
            (Strings.NoticeAvailableSummary, games.Count(game => !updates.Contains(game) && !repairs.Contains(game))),
            (Strings.NoticeUpdatesSummary, updates.Count),
            (Strings.NoticeRepairsSummary, repairs.Count),
        ];
        return string.Join(" ", parts.Where(part => part.Count > 0).Select(part => Text.Format(part.Pattern, ("count", part.Count))));
    }
}
