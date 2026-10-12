namespace JustModIt.Manager.Ui.Pages.Catalogue;

/// <summary>
/// The projects whose page the site no longer has, though the saved catalogue lists them until its next refresh. The
/// catalogue's game pages leave them out for the window's life, so a game page and a gone project's page never lead to
/// each other in turn.
/// </summary>
public sealed class GoneProjects
{
    private readonly HashSet<Guid> _projects = [];

    /// <summary>Whether the site said a project's page is gone.</summary>
    /// <param name="project">The project's id on the site.</param>
    public bool Contains(Guid project) => _projects.Contains(project);

    /// <summary>Remembers that the site no longer has a project's page.</summary>
    /// <param name="project">The project's id on the site.</param>
    public void Add(Guid project) => _projects.Add(project);
}
