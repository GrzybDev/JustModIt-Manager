using System.Text.Json;
using JustModIt.Manager.Core.Catalog;
using JustModIt.Manager.Core.Library;
using JustModIt.Manager.Core.State;
using JustModIt.Manager.Protocol.Messages;
using JustModIt.Manager.Protocol.Transport;
using JustModIt.Manager.Protocol.Views;

namespace JustModIt.Manager.Agent.Ipc;

/// <summary>What the window reads beyond the state: project pages, the catalogue and detection details.</summary>
public sealed class AgentQueries(CatalogueService catalogue, ILibraryService library, StateCoordinator coordinator)
{
    /// <summary>A slice larger than this is cut, so no answer comes near the frame limit.</summary>
    public const int MaxCataloguePage = 200;

    /// <summary>Answers one of the window's queries.</summary>
    /// <param name="request">A query for a project page, the catalogue, the detection report or leftover backups.</param>
    /// <param name="cancellationToken">Cancelled when the window's connection closes.</param>
    public async ValueTask<Message?> HandleAsync(Message request, CancellationToken cancellationToken) => request switch
    {
        GetProjectPage page => await GetProjectPageAsync(page, cancellationToken).ConfigureAwait(false),
        GetCataloguePage page => await GetCataloguePageAsync(page, cancellationToken).ConfigureAwait(false),
        GetCatalogueFilters => new CatalogueFiltersResult(
            await catalogue.GetListingAsync(cancellationToken).ConfigureAwait(false) is { } listing
                ? CatalogueFiltersView.From(listing.Reference)
                : new CatalogueFiltersView()),
        GetDetectionReport => new DetectionReportResult(DetectionReportView.From(DetectionReport.From(library.Snapshot))),
        GetLeftoverBackups => new LeftoverBackups(
            [.. (await coordinator.FindLeftoverBackupsAsync(cancellationToken).ConfigureAwait(false)).Select(LeftoverBackupView.From)]),
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

        using var json = JsonDocument.Parse(page.Document.Json);
        return new ProjectPageResult
        {
            Page = json.RootElement.Clone(),
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

        var found = listing.Search(request.Query);
        var slice = found.Skip(Math.Max(0, request.Offset)).Take(Math.Clamp(request.Count, 0, MaxCataloguePage));
        return new CataloguePageResult(found.Count, [.. slice.Select(CatalogueGameView.From)], listing.FetchedAt);
    }
}
