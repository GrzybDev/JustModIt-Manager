using System.Text;
using System.Text.Json;
using JustModIt.Manager.Core.Catalog;
using JustModIt.Manager.Creator;
using JustModIt.Manager.Ui.Pages.Game;
using JustModIt.Manager.Ui.Pages.Library;

namespace JustModIt.Manager.Ui.Pages.Creator;

/// <summary>What the site's project of a game suggests for the author's next package.</summary>
/// <param name="Project">The project's name.</param>
/// <param name="Release">The version of its last release on the site; empty when it has none.</param>
/// <param name="NextVersion">The version that follows that release; empty when it has none.</param>
/// <param name="Authors">The people its credits name, in their order.</param>
internal sealed record ProjectHints(string Project, string Release, string NextVersion, IReadOnlyList<string> Authors)
{
    /// <summary>Reads the hints off a project page as the site sent it; null when it is not a project page.</summary>
    /// <param name="page">The page's JSON, from the agent.</param>
    public static ProjectHints? Read(JsonElement page)
    {
        ProjectPage project;
        try
        {
            project = ProjectPageDocument.Parse(Encoding.UTF8.GetBytes(page.GetRawText())).Page;
        }
        catch (JsonException)
        {
            return null;
        }

        var release = project.PublicRelease;
        var version = release is null ? "" : GameWording.Version(release.Name, release.Version);
        return new ProjectHints(project.Name, version, release is null ? "" : PackageVersions.Next(version, release.Version),
            [.. project.Credits.OrderBy(credit => credit.Position).Select(ProjectDetails.CreditName).Where(name => name.Length > 0).Distinct()]);
    }
}
