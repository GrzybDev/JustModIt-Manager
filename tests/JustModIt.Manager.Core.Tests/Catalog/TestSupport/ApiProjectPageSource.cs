using JustModIt.Manager.Core.Catalog;

namespace JustModIt.Manager.Core.Tests.Catalog.TestSupport;

internal sealed class ApiProjectPageSource(ICatalogueApi api) : IProjectPageSource
{
    public async Task<ProjectPageDocument?> GetPageAsync(ProjectLocator project, DateTimeOffset? listedUpdatedAt,
        CancellationToken cancellationToken)
    {
        try
        {
            return await api.GetProjectPageAsync(project.GameSlug, project.TeamSlug, cancellationToken).ConfigureAwait(false);
        }
        catch (CatalogueException exception) when (exception.Reason == CatalogueErrorReason.NotFound)
        {
            return null;
        }
    }
}
