using System.Text.Json;
using JustModIt.Manager.Core.Catalog;
using JustModIt.Manager.Creator;
using JustModIt.Manager.Localization;
using JustModIt.Manager.Protocol.Messages;
using JustModIt.Manager.Protocol.Views;
using JustModIt.Manager.Ui.Connection;
using JustModIt.Manager.Ui.Pages.Game;
using JustModIt.Manager.Ui.Pages.Library;

namespace JustModIt.Manager.Ui.Pages.Creator;

/// <summary>What the site's project of a game suggests for the author's next package.</summary>
/// <param name="Project">The project's name.</param>
/// <param name="Release">The version of its last release on the site; empty when it has none.</param>
/// <param name="NextVersion">The version that follows that release; empty when it has none.</param>
/// <param name="Authors">The people its credits name, in their order.</param>
/// <param name="Features">The add-ons of its last release, whose names the author's add-ons take.</param>
/// <param name="Site">Where the project is on the site, with its team's name and cover.</param>
internal sealed record ProjectHints(string Project, string Release, string NextVersion, IReadOnlyList<string> Authors, IReadOnlyList<ReleaseFeature> Features,
    ProjectSite Site)
{
    /// <summary>Reads the hints off a project page as the site sent it; null when it is not a project page.</summary>
    /// <param name="page">The page's JSON, from the agent.</param>
    public static ProjectHints? Read(JsonElement page)
    {
        if (ProjectDetails.Parse(page) is not { } project)
        {
            return null;
        }

        var release = project.PublicRelease;
        var version = release is null ? "" : GameWording.Version(release.Name, release.Version);
        return new ProjectHints(project.Name, version, release is null ? "" : PackageVersions.Next(version, release.Version),
            [.. project.Credits.OrderBy(credit => credit.Position).Select(ProjectDetails.CreditName).Where(name => name.Length > 0).Distinct()], release?.Features ?? [],
            new ProjectSite(project.Uuid, project.Game.Slug, project.Team.Slug, project.Team.Name, project.CoverImage is { Length: > 0 } cover ? cover : project.Game.Cover));
    }

    /// <summary>
    /// Reads the hints of the site's project that a package for a game is surely for, which the creator tells without guessing:
    /// the project installed on the game, else the game's only one.
    /// </summary>
    /// <param name="agent">The agent, which reads the project's page.</param>
    /// <param name="game">The game.</param>
    /// <returns>The hints; null when no project is surely the author's or its page cannot be read.</returns>
    public static async Task<ProjectHints?> AskAsync(IAgent agent, DetectedGameEntry game)
    {
        var installed = agent.State.Games.FirstOrDefault(view => view.Key == game.Key)?.Installs
            .Select(install => install.Project).OfType<ProjectOfferView>().FirstOrDefault();
        var project = installed is not null ? new SiteProjectView(installed.ProjectId, installed.GameSlug, installed.Team.Slug)
            : game.Projects is [var only] ? only
            : null;
        return project is not null && await AgentRequests.AskQuietlyAsync(agent, new GetProjectPage(project.GameSlug, project.TeamSlug, project.ProjectId)).ConfigureAwait(true)
            is ProjectPageResult { Page: { } page } ? await Task.Run(() => Read(page)).ConfigureAwait(true) : null;
    }

    /// <summary>Which release offers an add-on of this name, as the author's add-on says it.</summary>
    /// <param name="name">The add-on's name.</param>
    /// <returns>The words; empty when the site's release offers no add-on of the name.</returns>
    public string FeatureFrom(string name) => Features.Any(feature => feature.Name == name)
        ? Text.Format(Strings.Creator_FeatureFrom, ("version", GameWording.ShortVersion(Release)))
        : "";
}
