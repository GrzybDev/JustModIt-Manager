using JustModIt.Manager.Core.Games;
using Microsoft.Extensions.Logging;

namespace JustModIt.Manager.Core.Catalog;

/// <summary>
/// The achievement texts projects published, saved on disk and read from the site again only when the catalogue's index,
/// read since, shows a newer revision, with nothing kept in memory, since only writing them for Steam's interface needs them.
/// </summary>
/// <param name="api">Reads the texts from the site.</param>
/// <param name="cache">Keeps the saved texts.</param>
/// <param name="catalogue">Tells which revision each project published and where the project is on the site.</param>
/// <param name="time">The clock that dates saved texts.</param>
/// <param name="logger">Logs texts that are gone or served from the saved copy.</param>
public sealed partial class AchievementTextsCache(ICatalogueApi api, CatalogueCache cache, ICatalogue catalogue, TimeProvider time,
    ILogger<AchievementTextsCache> logger)
{
    /// <summary>The texts a project published for a Steam copy of a game, as the catalogue's index lists them.</summary>
    /// <param name="project">The project's id.</param>
    /// <param name="game">The Steam copy of the game.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>Null when the project has none for the game, or the site no longer has them.</returns>
    /// <exception cref="CatalogueException">The texts could not be fetched and none were ever saved for the project.</exception>
    public async Task<AchievementTexts?> GetAsync(Guid project, GameKey game, CancellationToken cancellationToken)
    {
        var entry = catalogue.Current.Index.FindProject(project);
        if (entry?.AchievementsFor(game) is not { } listed)
        {
            return null;
        }

        var savedTexts = await cache.LoadAchievementsAsync(project, cancellationToken).ConfigureAwait(false);
        var saved = savedTexts?.Texts.FirstOrDefault(texts => texts.Game == game);
        // Texts read since the catalogue was are at least as new as what it lists, also when the site had none or newer ones.
        if (saved?.Revision >= listed.Revision || savedTexts?.FetchedAt >= catalogue.Current.FetchedAt)
        {
            return saved;
        }

        IReadOnlyList<AchievementTexts> fetched;
        try
        {
            fetched = await api.GetAchievementTextsAsync(entry.Team.Slug, entry.ProjectSlug, cancellationToken).ConfigureAwait(false);
        }
        // Hidden or unpublished since the index was built; saved as none, so a later failure serves none either.
        catch (CatalogueException exception) when (exception.Reason == CatalogueErrorReason.NotFound)
        {
            LogGone(project);
            fetched = [];
        }
        catch (CatalogueException exception) when (savedTexts is not null)
        {
            LogServedSaved(project, exception.Reason, exception);
            return saved;
        }

        await cache.SaveAchievementsAsync(project, fetched, time.GetUtcNow(), cancellationToken).ConfigureAwait(false);
        return fetched.FirstOrDefault(texts => texts.Game == game);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "The achievement texts of project {Project} are gone from the site")]
    private partial void LogGone(Guid project);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "The achievement texts of project {Project} could not be refreshed ({Reason}), so the saved ones serve")]
    private partial void LogServedSaved(Guid project, CatalogueErrorReason reason, Exception exception);
}
