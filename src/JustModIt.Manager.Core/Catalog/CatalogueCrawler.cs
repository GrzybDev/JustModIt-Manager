using System.Runtime.CompilerServices;

namespace JustModIt.Manager.Core.Catalog;

/// <summary>
/// Reads the whole catalogue page by page, with the project list of every game that has several projects, and the
/// site's reference lists.
/// </summary>
/// <param name="api">The site's API.</param>
/// <param name="time">Dates the listing.</param>
public sealed class CatalogueCrawler(ICatalogueApi api, TimeProvider time)
{
    // A safety stop in case the list's pages never end; a catalogue of this size would take hours to page through.
    private const int MaxPages = 2000;

    /// <summary>Reads the whole catalogue as the site lists it now.</summary>
    /// <param name="cancellationToken">Cancels the crawl.</param>
    /// <exception cref="CatalogueException">A page could not be read; nothing partial is returned.</exception>
    public async Task<CatalogueListing> CrawlAsync(CancellationToken cancellationToken)
    {
        // The list counts downloads only when ordered by them, and that order changes with every download, so a game
        // could slip past the pages; the games come from the stable order, and a second pass only adds the counts it sees.
        var downloads = new Dictionary<Guid, long?>();
        await foreach (var game in ReadAllAsync(api.GetGameDownloadsAsync, cancellationToken).ConfigureAwait(false))
        {
            downloads[game.Uuid] = game.TotalDownloads;
        }

        var games = new List<ListedGame>();
        await foreach (var game in ReadAllAsync(api.GetGamesAsync, cancellationToken).ConfigureAwait(false))
        {
            var projects = await GetProjectsAsync(game, cancellationToken).ConfigureAwait(false);
            if (projects.Count > 0)
            {
                var summary = game.ToSummary() with { TotalDownloads = downloads.GetValueOrDefault(game.Uuid) ?? game.TotalDownloads };
                games.Add(new ListedGame(summary, projects));
            }
        }

        var reference = await ReadReferenceAsync(cancellationToken).ConfigureAwait(false);
        return new CatalogueListing(time.GetUtcNow(), games, reference);
    }

    private async Task<CatalogueReference> ReadReferenceAsync(CancellationToken cancellationToken)
    {
        var teams = new List<Team>();
        try
        {
            await foreach (var team in ReadAllAsync(api.GetTeamsAsync, cancellationToken).ConfigureAwait(false))
            {
                teams.Add(team);
            }
        }
        catch (CatalogueException exception) when (exception.Reason == CatalogueErrorReason.NotFound)
        {
        }

        return new CatalogueReference
        {
            Statuses = await Optional(api.GetStatusesAsync, cancellationToken).ConfigureAwait(false),
            Types = await Optional(api.GetProjectTypesAsync, cancellationToken).ConfigureAwait(false),
            Genres = await Optional(api.GetGenresAsync, cancellationToken).ConfigureAwait(false),
            Platforms = await Optional(api.GetGamePlatformsAsync, cancellationToken).ConfigureAwait(false),
            StorefrontPlatforms = await Optional(api.GetStorefrontPlatformsAsync, cancellationToken).ConfigureAwait(false),
            Teams = teams,
            Site = await Optional(api.GetSiteConfigurationAsync, new SiteConfiguration(), cancellationToken).ConfigureAwait(false),
        };
    }

    private async Task<IReadOnlyList<ProjectSummary>> GetProjectsAsync(CatalogueGame game, CancellationToken cancellationToken)
    {
        // Not the project count: a backend that sends neither would otherwise hide every game.
        if (game.SoloProject is { } solo)
        {
            return [solo];
        }

        try
        {
            return await api.GetGameProjectsAsync(game.Slug, cancellationToken).ConfigureAwait(false);
        }
        catch (CatalogueException exception) when (exception.Reason == CatalogueErrorReason.NotFound)
        {
            // The game was removed or hidden since the page listed it.
            return [];
        }
    }

    private static async IAsyncEnumerable<T> ReadAllAsync<T>(Func<int, CancellationToken, Task<ApiPage<T>>> read,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        for (var number = 1; number <= MaxPages; number++)
        {
            ApiPage<T> page;
            try
            {
                page = await read(number, cancellationToken).ConfigureAwait(false);
            }
            catch (CatalogueException exception) when (number > 1 && exception.Reason == CatalogueErrorReason.NotFound)
            {
                // The list shrank since the previous page named this one as next.
                yield break;
            }

            foreach (var item in page.Results)
            {
                yield return item;
            }

            if (page.Next.Length == 0 || page.Results.Count == 0)
            {
                yield break;
            }
        }
    }

    private static Task<IReadOnlyList<T>> Optional<T>(Func<CancellationToken, Task<IReadOnlyList<T>>> read,
        CancellationToken cancellationToken) =>
        Optional(read, [], cancellationToken);

    // Reference data the backend does not offer yet only leaves a filter empty.
    private static async Task<T> Optional<T>(Func<CancellationToken, Task<T>> read, T missing, CancellationToken cancellationToken)
    {
        try
        {
            return await read(cancellationToken).ConfigureAwait(false);
        }
        catch (CatalogueException exception) when (exception.Reason == CatalogueErrorReason.NotFound)
        {
            return missing;
        }
    }
}
