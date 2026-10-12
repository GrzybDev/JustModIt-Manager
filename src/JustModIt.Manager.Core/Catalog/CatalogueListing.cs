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
    /// <summary>The newest release players can download first, then the project that went public last.</summary>
    Released,

    /// <summary>The most downloaded first.</summary>
    Popular,

    Alphabetical,
}

/// <summary>
/// What the player looks for in the catalogue: the status, type and store filters match a game when one of its listed
/// projects fits them all, while the team filter may match another of its projects.
/// </summary>
public sealed record CatalogueQuery
{
    /// <summary>Words that the game's name, or a project's or team's name, must all contain, ignoring case and diacritics.</summary>
    public string Text { get; init; } = "";

    public Guid? PlatformUuid { get; init; }

    public Guid? GenreUuid { get; init; }

    /// <summary>Also finds games marked as adult content, which only the player's confirmed age allows.</summary>
    public bool IncludeAdult { get; init; }

    public Guid? StatusUuid { get; init; }

    public Guid? TypeUuid { get; init; }

    /// <summary>A store the project links the game to (<see cref="StorefrontPlatform.Uuid"/>).</summary>
    public Guid? StorefrontUuid { get; init; }

    public Guid? TeamUuid { get; init; }

    public CatalogueOrder Order { get; init; }
}

/// <summary>How many games one value of a filter leaves.</summary>
/// <param name="Id">The value, as a <see cref="CatalogueQuery"/> filters by it.</param>
/// <param name="Count">The games it leaves.</param>
public sealed record CatalogueCount(Guid Id, int Count);

/// <summary>What one filter of a query offers, counted as the site's games directory counts it.</summary>
/// <param name="Total">The games that fit the query without this filter.</param>
/// <param name="Options">How many of them each value would leave; values none of them has are left out.</param>
public sealed record CatalogueFacet(int Total, IReadOnlyList<CatalogueCount> Options)
{
    public static CatalogueFacet Empty { get; } = new(0, []);

    /// <summary>How many games a value would leave; 0 for a value none of them has.</summary>
    /// <param name="id">The value.</param>
    public int CountOf(Guid id) => Options.FirstOrDefault(option => option.Id == id)?.Count ?? 0;
}

/// <summary>What each filter of a query offers: every filter counted with the others applied and its own left out.</summary>
public sealed record CatalogueFacets
{
    public CatalogueFacet Statuses { get; init; } = CatalogueFacet.Empty;

    public CatalogueFacet Types { get; init; } = CatalogueFacet.Empty;

    public CatalogueFacet Stores { get; init; } = CatalogueFacet.Empty;

    public CatalogueFacet Teams { get; init; } = CatalogueFacet.Empty;

    public CatalogueFacet Platforms { get; init; } = CatalogueFacet.Empty;

    public CatalogueFacet Genres { get; init; } = CatalogueFacet.Empty;

    /// <summary>The adult games that would fit the query with adult content included.</summary>
    public int Adult { get; init; }
}

/// <summary>
/// The whole catalogue as the site lists it, for browsing and, since store ids live on the projects' storefront links,
/// for finding a detected game's projects.
/// </summary>
public sealed class CatalogueListing
{
    private readonly FrozenDictionary<GameKey, ListedProject[]> _byGameKey;
    private readonly FrozenDictionary<Guid, ListedProject> _byProject;
    private string[][]? _searchTexts;

    /// <summary>Indexes the listed games by project and store id.</summary>
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
        _byGameKey = StoreIdLookup.Build(projects, listed => listed.Project.StorefrontLinks);
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

    /// <summary>The listed games that fit the query, in the order it asks for.</summary>
    /// <param name="query">The text, filters and order the player chose.</param>
    /// <param name="names">Orders game names, in the app's language.</param>
    /// <param name="games">Only these games, e.g. those found on this computer; null for all of them.</param>
    public IReadOnlyList<ListedGame> Search(CatalogueQuery query, IComparer<string> names, IReadOnlySet<Guid>? games = null)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(names);
        var found = Matching(query.Text, games).Where(listed => Fits(listed, query));
        return query.Order switch
        {
            CatalogueOrder.Popular => [.. found.OrderByDescending(listed => listed.Game.TotalDownloads ?? 0)
                .ThenBy(listed => listed.Game.Name, names)],
            CatalogueOrder.Alphabetical => [.. found.OrderBy(listed => listed.Game.Name, names)],
            // Descending puts games without a date last.
            _ => [.. found.OrderByDescending(listed => listed.Game.LatestReleaseAt)
                .ThenByDescending(listed => CardProjects(listed, query).Max(project => project.FirstPublishedAt))
                .ThenBy(listed => listed.Game.Name, names)],
        };
    }

    /// <summary>How many listed games fit the query.</summary>
    /// <param name="query">The text and filters the player chose.</param>
    /// <param name="games">Only these games; null for all of them.</param>
    public int Count(CatalogueQuery query, IReadOnlySet<Guid>? games = null)
    {
        ArgumentNullException.ThrowIfNull(query);
        return Matching(query.Text, games).Count(listed => Fits(listed, query));
    }

    /// <summary>What each filter of the query offers among the games that match its text.</summary>
    /// <param name="query">The text and filters the player chose.</param>
    /// <param name="games">Only these games; null for all of them.</param>
    public CatalogueFacets Facets(CatalogueQuery query, IReadOnlySet<Guid>? games = null)
    {
        ArgumentNullException.ThrowIfNull(query);
        var matching = Matching(query.Text, games).ToList();

        CatalogueFacet Facet(CatalogueQuery without, Func<ListedGame, IEnumerable<Guid>> values)
        {
            var fitting = matching.Where(listed => Fits(listed, without)).ToList();
            return new CatalogueFacet(fitting.Count,
                [.. fitting.SelectMany(listed => values(listed).Distinct()).CountBy(id => id).Select(pair => new CatalogueCount(pair.Key, pair.Value))]);
        }

        var noStatus = query with { StatusUuid = null };
        var noType = query with { TypeUuid = null };
        var noStore = query with { StorefrontUuid = null };
        return new CatalogueFacets
        {
            Statuses = Facet(noStatus, listed => CardProjects(listed, noStatus).Select(project => project.Status?.Uuid).OfType<Guid>()),
            Types = Facet(noType, listed => CardProjects(listed, noType).SelectMany(project => project.Types.Select(type => type.Uuid))),
            Stores = Facet(noStore, listed => CardProjects(listed, noStore)
                .SelectMany(project => project.StorefrontLinks.Select(link => link.Platform?.Uuid).OfType<Guid>())),
            Teams = Facet(query with { TeamUuid = null }, listed => listed.Projects.Where(IsListed).Select(project => project.Team.Uuid)),
            Platforms = Facet(query with { PlatformUuid = null }, listed => listed.Game.Platform is { } platform ? [platform.Uuid] : []),
            Genres = Facet(query with { GenreUuid = null }, listed => listed.Game.Genres.Select(genre => genre.Uuid)),
            Adult = matching.Count(listed => listed.Game.IsAdult && Fits(listed, query with { IncludeAdult = true })),
        };
    }

    /// <summary>The listed projects of a game that fit the query's status, type and store, which its card shows.</summary>
    /// <param name="listed">The game with its projects.</param>
    /// <param name="query">The filters the player chose.</param>
    public static IEnumerable<ProjectSummary> CardProjects(ListedGame listed, CatalogueQuery query)
    {
        ArgumentNullException.ThrowIfNull(listed);
        ArgumentNullException.ThrowIfNull(query);
        return listed.Projects.Where(project => IsListed(project)
            && (query.StatusUuid is not { } status || project.Status?.Uuid == status)
            && (query.TypeUuid is not { } type || project.Types.Any(candidate => candidate.Uuid == type))
            && (query.StorefrontUuid is not { } store || project.StorefrontLinks.Any(link => link.Platform?.Uuid == store)));
    }

    // The catalogue's lists leave abandoned projects out, though a game's own project list keeps them.
    private static bool IsListed(ProjectSummary project) => project.Status?.IsAbandoned != true;

    private IEnumerable<ListedGame> Matching(string text, IReadOnlySet<Guid>? games)
    {
        var words = SearchText.Fold(text).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        // Folded at the first search only, since the agent reads listings back to build the index, which never searches;
        // two searches at once may both fold, which is harmless.
        var texts = _searchTexts ??=
        [
            .. Games.Select(listed => new[] { SearchText.Fold(listed.Game.Name) }
                .Concat(listed.Projects.SelectMany(project => new[] { SearchText.Fold(project.Name), SearchText.Fold(project.Team.Name) }))
                .ToArray()),
        ];
        return Games.Where((listed, index) => (games is null || games.Contains(listed.Game.Uuid))
            && words.All(word => texts[index].Any(candidate => candidate.Contains(word, StringComparison.Ordinal))));
    }

    private static bool Fits(ListedGame listed, CatalogueQuery query)
    {
        var game = listed.Game;
        return (query.PlatformUuid is not { } platform || game.Platform?.Uuid == platform)
            && (query.GenreUuid is not { } genre || game.Genres.Any(candidate => candidate.Uuid == genre))
            && (query.IncludeAdult || !game.IsAdult)
            && CardProjects(listed, query).Any()
            && (query.TeamUuid is not { } team || listed.Projects.Any(project => IsListed(project) && project.Team.Uuid == team));
    }
}
