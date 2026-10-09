namespace JustModIt.Manager.Core.Catalog;

/// <summary>
/// Builds the index from the site's public pages: the projects the listing links to the scope's games, plus the
/// installed ones, each with its project page for the release it offers.
/// </summary>
/// <param name="pages">Gives each project's page, reusing a saved one while current.</param>
/// <param name="time">Dates the index.</param>
public sealed class CrawlIndexSource(IProjectPageSource pages, TimeProvider time) : IIndexSource
{
    // A few pages at a time: a first run for a large library stays quick without hammering the site.
    private const int ParallelPages = 4;

    public async Task<InstallableIndex> BuildAsync(IndexScope scope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);
        var wanted = new Dictionary<Guid, (ProjectLocator Locator, DateTimeOffset? ListedUpdatedAt)>();
        foreach (var listed in scope.Games.SelectMany(scope.Listing.Find))
        {
            wanted.TryAdd(listed.Project.Uuid, (listed.Locator, listed.Project.UpdatedAt));
        }

        foreach (var installed in scope.InstalledProjects)
        {
            // The listing knows the current slugs; the games list leaves abandoned projects out, so those are asked for
            // where the site last showed them.
            wanted.TryAdd(installed.ProjectUuid, scope.Listing.FindProject(installed.ProjectUuid) is { } listed
                ? (listed.Locator, listed.Project.UpdatedAt)
                : (installed, null));
        }

        var requests = wanted.Values.ToArray();
        var entries = new IndexEntry?[requests.Length];
        await Parallel.ForAsync(0, requests.Length,
            new ParallelOptions { MaxDegreeOfParallelism = ParallelPages, CancellationToken = cancellationToken },
            async (index, token) =>
            {
                var (locator, listedUpdatedAt) = requests[index];
                var page = await pages.GetPageAsync(locator, listedUpdatedAt, token).ConfigureAwait(false);
                // The slugs may now lead to another project, e.g. one the team made after deleting the installed one.
                entries[index] = page?.Page.Uuid == locator.ProjectUuid ? IndexEntry.From(page.Page) : null;
            }).ConfigureAwait(false);
        return new InstallableIndex(time.GetUtcNow(), entries.OfType<IndexEntry>());
    }
}
