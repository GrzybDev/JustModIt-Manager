using JustModIt.Manager.Core.Catalog;
using JustModIt.Manager.Core.Installation;

namespace JustModIt.Manager.Core.Jobs;

internal static class InstallSources
{
    /// <summary>The project an install came from; null when the site did not supply it, e.g. for a test install.</summary>
    /// <param name="source">Where the install came from, as its record keeps it.</param>
    public static ProjectLocator? ProjectOf(InstallSource? source) =>
        source is { GameSlug: { } game, TeamSlug: { } team, ProjectUuid: { } project }
            ? new ProjectLocator(game, team, project)
            : null;
}
