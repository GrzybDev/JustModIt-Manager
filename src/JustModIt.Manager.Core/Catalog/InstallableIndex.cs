using System.Collections.Frozen;
using System.Text.Json.Serialization;
using JustModIt.Manager.Core.Games;

namespace JustModIt.Manager.Core.Catalog;

/// <summary>
/// A project with what deciding about an install needs, its storefront links and the release the site offers now,
/// leaving descriptions, credits and the gallery to the project page, which is loaded when a game page opens.
/// </summary>
public sealed record IndexEntry
{
    public required GameSummary Game { get; init; }

    public required Guid ProjectUuid { get; init; }

    public required string ProjectSlug { get; init; }

    public required string ProjectName { get; init; }

    public required TeamReference Team { get; init; }

    public ProjectStatus? Status { get; init; }

    public IReadOnlyList<ProjectType> Types { get; init => field = value ?? []; } = [];

    public IReadOnlyList<StorefrontLink> StorefrontLinks { get; init => field = value ?? []; } = [];

    /// <summary>The release an anonymous visitor may download; null when there is none.</summary>
    public Release? PublicRelease { get; init; }

    /// <inheritdoc cref="ProjectPage.IsOutdated"/>
    public bool IsOutdated { get; init; }

    /// <inheritdoc cref="ProjectSummary.UpdatedAt"/>
    public DateTimeOffset? UpdatedAt { get; init; }

    [JsonIgnore]
    public ProjectLocator Locator => new(Game.Slug, Team.Slug, ProjectUuid);

    /// <summary>The project's links to the game in its store, in the order the team gave them.</summary>
    /// <param name="game">The game's store and store id.</param>
    public IReadOnlyList<StorefrontLink> LinksFor(GameKey game) =>
        [.. StorefrontLinks.Where(link => link.GameKey == game).OrderBy(link => link.Position)];

    /// <summary>What the download endpoint needs to name one of the project's release files.</summary>
    /// <param name="release">A release of this project.</param>
    /// <param name="file">One of the release's files.</param>
    public ReleaseFileLocator LocateFile(Release release, ReleaseFile file)
    {
        ArgumentNullException.ThrowIfNull(release);
        ArgumentNullException.ThrowIfNull(file);
        return new ReleaseFileLocator(Team.Slug, ProjectSlug, release.Uuid, file.Uuid);
    }

    /// <summary>The entry for a project page.</summary>
    /// <param name="page">The page as the site sent it.</param>
    public static IndexEntry From(ProjectPage page)
    {
        ArgumentNullException.ThrowIfNull(page);
        return new IndexEntry
        {
            Game = page.Game,
            ProjectUuid = page.Uuid,
            ProjectSlug = page.Slug,
            ProjectName = page.Name,
            Team = page.Team.ToReference(),
            Status = page.Status,
            Types = page.Types,
            StorefrontLinks = page.StorefrontLinks,
            PublicRelease = page.PublicRelease,
            IsOutdated = page.IsOutdated,
            UpdatedAt = page.UpdatedAt,
        };
    }
}

/// <summary>The projects that detected or installed games may use, looked up by the games' store ids.</summary>
public sealed class InstallableIndex
{
    private readonly FrozenDictionary<GameKey, IndexEntry[]> _byGame;
    private readonly FrozenDictionary<Guid, IndexEntry> _byProject;

    /// <summary>Indexes the entries by their games' store ids and by project.</summary>
    /// <param name="builtAt">When the index was built.</param>
    /// <param name="entries">The projects; a repeated project keeps its first entry.</param>
    public InstallableIndex(DateTimeOffset builtAt, IEnumerable<IndexEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        BuiltAt = builtAt;
        Entries = [.. entries.DistinctBy(entry => entry.ProjectUuid)];
        _byProject = Entries.ToFrozenDictionary(entry => entry.ProjectUuid);
        _byGame = Entries
            .SelectMany(entry => entry.StorefrontLinks.Select(link => link.GameKey).OfType<GameKey>().Distinct()
                .Select(key => (Key: key, Entry: entry)))
            .GroupBy(match => match.Key)
            .ToFrozenDictionary(group => group.Key, group => group.Select(match => match.Entry).ToArray());
    }

    public static InstallableIndex Empty { get; } = new(DateTimeOffset.MinValue, []);

    public DateTimeOffset BuiltAt { get; }

    public IReadOnlyList<IndexEntry> Entries { get; }

    /// <summary>Every project with a storefront link to the game, whether or not it has anything to install.</summary>
    /// <param name="game">The game's store and store id.</param>
    public IReadOnlyList<IndexEntry> Find(GameKey game) => _byGame.TryGetValue(game, out var entries) ? entries : [];

    /// <summary>A project's entry; null when the index does not have it.</summary>
    /// <param name="projectUuid">The project's id.</param>
    public IndexEntry? FindProject(Guid projectUuid) => _byProject.GetValueOrDefault(projectUuid);

    /// <summary>
    /// Where the site last showed the project, so an install keeps working after its game, team or project was renamed;
    /// old slugs lead there only until another project takes them.
    /// </summary>
    /// <param name="project">The project as its install recorded it.</param>
    public ProjectLocator Locate(ProjectLocator project) => FindProject(project.ProjectUuid)?.Locator ?? project;
}
