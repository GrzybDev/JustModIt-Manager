using System.Text.Json;
using JustModIt.Manager.Core.Catalog;
using JustModIt.Manager.Core.Installation;
using JustModIt.Manager.Protocol.Views;

namespace JustModIt.Manager.Protocol.Messages;

/// <summary>A request the agent answers by reading, never by changing anything.</summary>
public abstract record Query : Message;

/// <summary>Asks for a project page, reused while current; answered with <see cref="ProjectPageResult"/>.</summary>
/// <param name="GameSlug">The game's slug in the site's addresses.</param>
/// <param name="TeamSlug">The team's slug in the site's addresses.</param>
/// <param name="ProjectId">The project, whose saved page is reused.</param>
public sealed record GetProjectPage(string GameSlug, string TeamSlug, Guid ProjectId) : Query;

/// <summary>The answer to <see cref="GetProjectPage"/>.</summary>
public sealed record ProjectPageResult : Message
{
    /// <summary>
    /// The page exactly as the site sent it, for <see cref="ProjectPageDocument.Parse"/>; null when the site does not have
    /// it and it was never saved.
    /// </summary>
    public JsonElement? Page { get; init; }

    public DateTimeOffset? FetchedAt { get; init; }

    /// <summary>A fresh copy could not be had, see <see cref="Error"/>, so this is the one saved earlier.</summary>
    public bool IsStale { get; init; }

    /// <summary>The site no longer has the page.</summary>
    public bool IsGone { get; init; }

    /// <summary>Why the site could not be read; with no page, why there is none.</summary>
    public CatalogueErrorReason? Error { get; init; }
}

/// <summary>Asks for a slice of the catalogue; answered with <see cref="CataloguePageResult"/>.</summary>
/// <param name="Query">The search, filters and order the games must match.</param>
/// <param name="Offset">How many matching games to skip.</param>
/// <param name="Count">How many games to return at most; the agent caps it.</param>
/// <param name="DetectedOnly">Only the games found on this computer.</param>
public sealed record GetCataloguePage(CatalogueQuery Query, int Offset, int Count, bool DetectedOnly = false) : Query;

/// <summary>The answer to <see cref="GetCataloguePage"/>.</summary>
/// <param name="Total">How many games match, of which <paramref name="Games"/> are the slice asked for.</param>
/// <param name="Games">The slice, in the query's order.</param>
/// <param name="FetchedAt">When the catalogue was read from the site; null when it never was, and the slice is empty.</param>
public sealed record CataloguePageResult(int Total, IReadOnlyList<CatalogueGameView> Games, DateTimeOffset? FetchedAt = null)
    : Message
{
    /// <summary>What each filter offers with the others applied, among the games found on this computer when only they are asked for.</summary>
    public CatalogueFacets Facets { get; init; } = new();

    /// <summary>How many of the games that fit the query, found on this computer or not, were found on it.</summary>
    public int Detected { get; init; }

    /// <summary>How many games the query's text alone finds, without any filter.</summary>
    public int Unfiltered { get; init; }
}

/// <summary>Asks for one game of the catalogue with all its listed projects; answered with <see cref="CatalogueGameResult"/>.</summary>
/// <param name="GameSlug">The game's slug in the site's addresses.</param>
public sealed record GetCatalogueGame(string GameSlug) : Query;

/// <summary>The answer to <see cref="GetCatalogueGame"/>.</summary>
/// <param name="Game">The game; null when the catalogue does not list it, or was never read.</param>
public sealed record CatalogueGameResult(CatalogueGameView? Game = null) : Message;

/// <summary>Asks for the options of the catalogue's filters; answered with <see cref="CatalogueFiltersResult"/>.</summary>
public sealed record GetCatalogueFilters : Query;

/// <summary>The answer to <see cref="GetCatalogueFilters"/>.</summary>
/// <param name="Filters">The options of every filter.</param>
public sealed record CatalogueFiltersResult(CatalogueFiltersView Filters) : Message
{
    /// <summary>What each filter offers in the whole catalogue, which tells the filters that could narrow it at all.</summary>
    public CatalogueFacets Facets { get; init; } = new();
}

/// <summary>Asks what each store integration found; answered with <see cref="DetectionReportResult"/>.</summary>
public sealed record GetDetectionReport : Query;

/// <summary>The answer to <see cref="GetDetectionReport"/>.</summary>
/// <param name="Report">What each store integration found.</param>
public sealed record DetectionReportResult(DetectionReportView Report) : Message;

/// <summary>
/// Asks for every game the stores reported, also those the player's games leave out, as the package creator offers
/// them; answered with <see cref="DetectedGames"/>.
/// </summary>
public sealed record GetDetectedGames : Query;

/// <summary>The answer to <see cref="GetDetectedGames"/>.</summary>
/// <param name="Games">The games of the library's last scan, by name.</param>
public sealed record DetectedGames(IReadOnlyList<DetectedGameEntry> Games) : Message;

/// <summary>
/// Asks for the backups that installs forgotten with their game left behind, with their size; answered with
/// <see cref="LeftoverBackups"/>.
/// </summary>
public sealed record GetLeftoverBackups : Query;

/// <summary>The answer to <see cref="GetLeftoverBackups"/>.</summary>
/// <param name="Backups">The leftover backups with their size.</param>
public sealed record LeftoverBackups(IReadOnlyList<LeftoverBackup> Backups) : Message;

/// <summary>Asks how much space the app's own data takes; answered with <see cref="AppDataUsage"/>.</summary>
public sealed record GetAppDataUsage : Query;

/// <summary>The answer to <see cref="GetAppDataUsage"/>, in bytes.</summary>
/// <param name="DataFolder">The app's data folder, whose drive the window names.</param>
/// <param name="Total">Everything the app keeps of its own, backups in its data folder included.</param>
/// <param name="Downloads">The downloaded packages, partial ones included.</param>
/// <param name="ImagesAndCatalogue">The window's images and the saved catalogue with its project pages.</param>
/// <param name="Logs">The logs.</param>
public sealed record AppDataUsage(string DataFolder, long Total, long Downloads, long ImagesAndCatalogue, long Logs) : Message;
