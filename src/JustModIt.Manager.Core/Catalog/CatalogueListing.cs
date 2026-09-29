using System.Collections.Frozen;
using JustModIt.Manager.Core.Games;

namespace JustModIt.Manager.Core.Catalog;

/// <summary>A listed game with its projects.</summary>
/// <param name="Game">The game as the catalogue lists it.</param>
/// <param name="Projects">Its projects, each listed once in the whole catalogue.</param>
public sealed record ListedGame(GameSummary Game, IReadOnlyList<ProjectSummary> Projects);

/// <summary>A listed project with its game, which a project summary does not name.</summary>
/// <param name="Game">The game the project is made for.</param>
/// <param name="Project">The project as the game lists it.</param>
public sealed record ListedProject(GameSummary Game, ProjectSummary Project)
{
    public ProjectLocator Locator => new(Game.Slug, Project.Team.Slug, Project.Uuid);
}

/// <summary>The site's reference lists: what the catalogue's filters offer and what its objects refer to.</summary>
public sealed record CatalogueReference
{
    public IReadOnlyList<ProjectStatus> Statuses { get; init => field = value ?? []; } = [];

    public IReadOnlyList<ProjectType> Types { get; init => field = value ?? []; } = [];

    public IReadOnlyList<Genre> Genres { get; init => field = value ?? []; } = [];

    public IReadOnlyList<GamePlatform> Platforms { get; init => field = value ?? []; } = [];

    public IReadOnlyList<StorefrontPlatform> StorefrontPlatforms { get; init => field = value ?? []; } = [];

    public IReadOnlyList<Team> Teams { get; init => field = value ?? []; } = [];

    public SiteConfiguration Site { get; init => field = value ?? new(); } = new();
}

public enum CatalogueOrder
{
    /// <summary>The site's own order, which is by name.</summary>
    Name,
    Newest,
    Oldest,
}

/// <summary>What the player looks for in the catalogue. Project filters match a game when one of its projects fits them all.</summary>
public sealed record CatalogueQuery
{
    /// <summary>Words that the game's name, or a project's or team's name, must all contain, ignoring case and diacritics.</summary>
    public string Text { get; init; } = "";

    public Guid? PlatformUuid { get; init; }

    public Guid? GenreUuid { get; init; }

    /// <summary>Null finds games whether or not they are marked as adult content.</summary>
    public bool? IsAdult { get; init; }

    public Guid? StatusUuid { get; init; }

    public Guid? TypeUuid { get; init; }

    public Guid? TeamUuid { get; init; }

    public CatalogueOrder Order { get; init; }
}

/// <summary>
/// The whole catalogue as the site lists it, for browsing and, since store ids live on the projects' storefront links,
/// for finding a detected game's projects.
/// </summary>
public sealed class CatalogueListing
{
    private readonly FrozenDictionary<GameKey, ListedProject[]> _byGameKey;
    private readonly FrozenDictionary<Guid, ListedProject> _byProject;
    private readonly string[][] _searchTexts;

    /// <summary>Indexes the listed games by project, store id and search text.</summary>
    /// <param name="fetchedAt">When the site was read.</param>
    /// <param name="games">The games as paged; a game or project that paging returned twice is kept once.</param>
    /// <param name="reference">The site's reference lists.</param>
    public CatalogueListing(DateTimeOffset fetchedAt, IEnumerable<ListedGame> games, CatalogueReference reference)
    {
        ArgumentNullException.ThrowIfNull(games);
        ArgumentNullException.ThrowIfNull(reference);
        FetchedAt = fetchedAt;
        Reference = reference;
        // Paging over a list that changes meanwhile can return a game or a project twice.
        var seenProjects = new HashSet<Guid>();
        Games =
        [
            .. games.DistinctBy(listed => listed.Game.Uuid)
                .Select(listed => listed with { Projects = [.. listed.Projects.Where(project => seenProjects.Add(project.Uuid))] }),
        ];
        var projects = Games.SelectMany(listed => listed.Projects.Select(project => new ListedProject(listed.Game, project))).ToList();
        _byProject = projects.ToFrozenDictionary(listed => listed.Project.Uuid);
        _byGameKey = projects
            .SelectMany(listed => listed.Project.StorefrontLinks
                .Select(link => link.GameKey)
                .OfType<GameKey>()
                .Distinct()
                .Select(key => (Key: key, Listed: listed)))
            .GroupBy(match => match.Key)
            .ToFrozenDictionary(group => group.Key, group => group.Select(match => match.Listed).ToArray());
        _searchTexts =
        [
            .. Games.Select(listed => new[] { SearchText.Fold(listed.Game.Name) }
                .Concat(listed.Projects.SelectMany(project => new[] { SearchText.Fold(project.Name), SearchText.Fold(project.Team.Name) }))
                .ToArray()),
        ];
    }

    public DateTimeOffset FetchedAt { get; }

    public IReadOnlyList<ListedGame> Games { get; }

    public CatalogueReference Reference { get; }

    /// <summary>Every listed project with a storefront link to the game; abandoned ones only when the site lists them.</summary>
    /// <param name="game">The game's store and store id.</param>
    public IReadOnlyList<ListedProject> Find(GameKey game) => _byGameKey.TryGetValue(game, out var projects) ? projects : [];

    /// <summary>A listed project with its game; null when the listing does not have it.</summary>
    /// <param name="projectUuid">The project's id.</param>
    public ListedProject? FindProject(Guid projectUuid) => _byProject.GetValueOrDefault(projectUuid);

    public IReadOnlyList<ListedGame> Search(CatalogueQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        var words = SearchText.Fold(query.Text);
        var found = Games
            .Select((listed, index) => (Listed: listed, Texts: _searchTexts[index]))
            .Where(candidate => words.Length == 0 || SearchText.Matches(words, candidate.Texts))
            .Select(candidate => candidate.Listed)
            .Where(listed => Fits(listed.Game, query) && listed.Projects.Any(project => Fits(project, query)));
        return query.Order switch
        {
            CatalogueOrder.Newest => [.. found.OrderByDescending(listed => listed.Game.CreatedAt)],
            CatalogueOrder.Oldest => [.. found.OrderBy(listed => listed.Game.CreatedAt)],
            _ => [.. found],
        };
    }

    private static bool Fits(GameSummary game, CatalogueQuery query) =>
        (query.PlatformUuid is not { } platform || game.Platform?.Uuid == platform)
        && (query.GenreUuid is not { } genre || game.Genres.Any(candidate => candidate.Uuid == genre))
        && (query.IsAdult is not { } adult || game.IsAdult == adult);

    private static bool Fits(ProjectSummary project, CatalogueQuery query) =>
        (query.StatusUuid is not { } status || project.Status?.Uuid == status)
        && (query.TypeUuid is not { } type || project.Types.Any(candidate => candidate.Uuid == type))
        && (query.TeamUuid is not { } team || project.Team.Uuid == team);
}
