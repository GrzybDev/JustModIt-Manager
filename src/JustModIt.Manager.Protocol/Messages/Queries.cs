using System.Text.Json;
using JustModIt.Manager.Core.Catalog;
using JustModIt.Manager.Protocol.Views;

namespace JustModIt.Manager.Protocol.Messages;

/// <summary>Asks for a project page, reused while current; answered with <see cref="ProjectPageResult"/>.</summary>
/// <param name="GameSlug">The game's slug in the site's addresses.</param>
/// <param name="TeamSlug">The team's slug in the site's addresses.</param>
/// <param name="ProjectId">The project, whose saved page is reused.</param>
public sealed record GetProjectPage(string GameSlug, string TeamSlug, Guid ProjectId) : Message;

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
public sealed record GetCataloguePage(CatalogueQuery Query, int Offset, int Count) : Message;

/// <summary>The answer to <see cref="GetCataloguePage"/>.</summary>
/// <param name="Total">How many games match, of which <paramref name="Games"/> are the slice asked for.</param>
/// <param name="Games">The slice, in the query's order.</param>
/// <param name="FetchedAt">When the catalogue was read from the site; null when it never was, and the slice is empty.</param>
public sealed record CataloguePageResult(int Total, IReadOnlyList<CatalogueGameView> Games, DateTimeOffset? FetchedAt = null)
    : Message;

/// <summary>Asks for the options of the catalogue's filters; answered with <see cref="CatalogueFiltersResult"/>.</summary>
public sealed record GetCatalogueFilters : Message;

/// <summary>The answer to <see cref="GetCatalogueFilters"/>.</summary>
/// <param name="Filters">The options of every filter.</param>
public sealed record CatalogueFiltersResult(CatalogueFiltersView Filters) : Message;

/// <summary>Asks what each store integration found; answered with <see cref="DetectionReportResult"/>.</summary>
public sealed record GetDetectionReport : Message;

/// <summary>The answer to <see cref="GetDetectionReport"/>.</summary>
/// <param name="Report">What each store integration found.</param>
public sealed record DetectionReportResult(DetectionReportView Report) : Message;

/// <summary>Backups whose game its store confirms is gone, with their size; answered with <see cref="LeftoverBackups"/>.</summary>
public sealed record GetLeftoverBackups : Message;

/// <summary>The answer to <see cref="GetLeftoverBackups"/>.</summary>
/// <param name="Backups">The leftover backups with their size.</param>
public sealed record LeftoverBackups(IReadOnlyList<LeftoverBackupView> Backups) : Message;
