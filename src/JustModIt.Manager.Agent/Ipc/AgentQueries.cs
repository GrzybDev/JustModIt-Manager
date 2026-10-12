using System.Text.Json;
using JustModIt.Manager.Agent.Storage;
using JustModIt.Manager.Core.Catalog;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.Installation;
using JustModIt.Manager.Core.Library;
using JustModIt.Manager.Core.Settings;
using JustModIt.Manager.Localization;
using JustModIt.Manager.Protocol.Messages;
using JustModIt.Manager.Protocol.Transport;
using JustModIt.Manager.Protocol.Views;

namespace JustModIt.Manager.Agent.Ipc;

/// <summary>What the window reads beyond the state: project pages, the catalogue, detection details and the app's own data.</summary>
/// <param name="catalogue">Reads project pages and the catalogue's listing.</param>
/// <param name="library">The snapshot the detection report comes from.</param>
/// <param name="engine">Finds leftover backups.</param>
/// <param name="settings">Tells whether the player confirmed being an adult.</param>
/// <param name="storage">Measures the app's own folders.</param>
/// <param name="time">Tells whether that confirmation still holds.</param>
public sealed class AgentQueries(CatalogueService catalogue, ILibraryService library, IInstallEngine engine,
    ISettingsService settings, AppStorage storage, TimeProvider time)
{
    /// <summary>A slice larger than this is cut, so no answer comes near the frame limit.</summary>
    public const int MaxCataloguePage = 200;

    /// <summary>Answers one of the window's queries.</summary>
    /// <param name="request">The query.</param>
    /// <param name="cancellationToken">Cancelled when the window's connection closes.</param>
    public async ValueTask<Message?> HandleAsync(Query request, CancellationToken cancellationToken) => request switch
    {
        GetProjectPage page => await GetProjectPageAsync(page, cancellationToken).ConfigureAwait(false),
        GetCataloguePage page => await GetCataloguePageAsync(page, cancellationToken).ConfigureAwait(false),
        GetCatalogueGame game => await GetCatalogueGameAsync(game, cancellationToken).ConfigureAwait(false),
        GetCatalogueFilters => await catalogue.GetListingAsync(cancellationToken).ConfigureAwait(false) is { } listing
            ? new CatalogueFiltersResult(CatalogueFiltersView.From(listing.Reference)) { Facets = listing.Facets(new CatalogueQuery()) }
            : new CatalogueFiltersResult(new CatalogueFiltersView()),
        GetDetectionReport => new DetectionReportResult(DetectionReportView.From(DetectionReport.From(library.Snapshot),
            (await catalogue.GetListingAsync(cancellationToken).ConfigureAwait(false))?.Reference.StorefrontPlatforms ?? [])),
        GetLeftoverBackups => new LeftoverBackups(await engine.FindLeftoverBackupsAsync(cancellationToken).ConfigureAwait(false)),
        GetAppDataUsage => storage.Measure(),
        _ => throw new RequestFailedException(ErrorCode.Unsupported),
    };

    private async Task<Message> GetProjectPageAsync(GetProjectPage request, CancellationToken cancellationToken)
    {
        CataloguePage? page;
        try
        {
            page = await catalogue.GetProjectPageAsync(new ProjectLocator(request.GameSlug, request.TeamSlug, request.ProjectId),
                PageFreshness.Current, cancellationToken).ConfigureAwait(false);
        }
        catch (CatalogueException exception)
        {
            return new ProjectPageResult { Error = exception.Reason };
        }

        if (page is null)
        {
            return new ProjectPageResult { IsGone = true };
        }

        return new ProjectPageResult
        {
            Page = JsonElement.Parse(page.Document.Json.Span),
            FetchedAt = page.FetchedAt,
            IsStale = page.IsStale,
            IsGone = page.IsGone,
            Error = page.RefreshError?.Reason,
        };
    }

    private async Task<Message> GetCataloguePageAsync(GetCataloguePage request, CancellationToken cancellationToken)
    {
        if (await catalogue.GetListingAsync(cancellationToken).ConfigureAwait(false) is not { } listing)
        {
            return new CataloguePageResult(0, []);
        }

        var query = request.Query with
        {
            IncludeAdult = request.Query.IncludeAdult && settings.Current.IsAdultContentConfirmed(time.GetUtcNow()),
        };
        var detected = Detected(listing);
        var detectedGames = detected.Keys.ToHashSet();
        var within = request.DetectedOnly ? detectedGames : null;
        var found = listing.Search(query, Text.Comparer, within);
        var slice = found.Skip(Math.Max(0, request.Offset)).Take(Math.Clamp(request.Count, 0, MaxCataloguePage));
        return new CataloguePageResult(found.Count, [.. slice.Select(listed => CatalogueGameView.From(listed with
        {
            Projects = [.. CatalogueListing.CardProjects(listed, query)],
        }) with
        {
            Detected = detected.GetValueOrDefault(listed.Game.Uuid, []),
        })], listing.FetchedAt)
        {
            Facets = listing.Facets(query, within),
            Detected = listing.Count(query, detectedGames),
            Unfiltered = listing.Count(new CatalogueQuery { Text = query.Text }),
        };
    }

    private async Task<Message> GetCatalogueGameAsync(GetCatalogueGame request, CancellationToken cancellationToken)
    {
        var listing = await catalogue.GetListingAsync(cancellationToken).ConfigureAwait(false);
        return new CatalogueGameResult(listing?.FindGame(request.GameSlug) is { } listed
            ? CatalogueGameView.From(listed) with { Detected = Detected(listing).GetValueOrDefault(listed.Game.Uuid, []) }
            : null);
    }

    // The catalogue's games that the stores found on this computer, by the store ids their projects link to.
    private Dictionary<Guid, GameKey[]> Detected(CatalogueListing listing) =>
        library.Snapshot.Games.Keys
            .SelectMany(key => listing.Find(key).Select(listed => (listed.Game.Uuid, Key: key)).Distinct())
            .GroupBy(match => match.Uuid)
            .ToDictionary(group => group.Key,
                group => group.Select(match => match.Key).OrderBy(key => key.ToString(), StringComparer.Ordinal).ToArray());
}
