using JustModIt.Manager.Core.Catalog;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Localization;
using JustModIt.Manager.Protocol.Messages;
using JustModIt.Manager.Protocol.Views;

namespace JustModIt.Manager.Ui.Tests.TestSupport;

/// <summary>
/// An invented catalogue of a dozen games, and an agent's answers to the window's catalogue questions read from it the way
/// the agent reads its listing.
/// </summary>
public static class TestCatalogue
{
    public static readonly ProjectStatus Completed = Status("Ukończone", "#22C55E", 4, completed: true);
    public static readonly ProjectStatus Proofreading = Status("Korekta", "#F97316", 3);
    public static readonly ProjectStatus Translating = Status("W trakcie tłumaczenia", "#EF4444", 2);
    public static readonly ProjectStatus Announced = Status("Zapowiedziane", "#8B5CF6", 1);
    public static readonly ProjectStatus Abandoned = Status("Porzucone", "#71717A", 0, abandoned: true);

    public static readonly TeamReference Lanterns = Team(TestGames.Lanterns);
    public static readonly TeamReference Owls = Team(TestGames.Owls);
    public static readonly TeamReference Cranes = Team(TestGames.Cranes);
    public static readonly TeamReference Moths = new() { Uuid = Id("moth-works"), Slug = "moth-works", Name = "Moth Works" };

    public static readonly StorefrontPlatform Steam = new() { Uuid = Id("steam"), Name = "Steam", Provider = "steam" };
    public static readonly StorefrontPlatform Gog = new() { Uuid = Id("gog"), Name = "GOG", Provider = "gog" };
    public static readonly ProjectType Dubbing = new() { Uuid = Id("dubbing"), Name = "Dubbing" };
    public static readonly Genre Adventure = new() { Uuid = Id("adventure"), Name = "Przygodowa" };
    public static readonly Genre Action = new() { Uuid = Id("action"), Name = "Akcja" };

    /// <summary>The player's own game, found on this computer and among the player's games.</summary>
    public static readonly GameKey EmberKey = TestGames.SteamKey("4100");

    /// <summary>A game found on this computer that waits for its project, so it is not among the player's games.</summary>
    public static readonly GameKey MeridianKey = TestGames.SteamKey("4700");

    public static CatalogueListing Listing { get; } = new(TestStates.Now.AddMinutes(-4),
    [
        Game("Ember Hollow", 2, 900, [Adventure], Project("Ember Hollow", Lanterns, Completed, Steam)),
        Game("Glass Orchard", 10, 300, [Adventure], Project("Glass Orchard", Owls, Completed, Steam)),
        Game("Saltmarsh: Iron Tide", 15, 1200, [Action], Project("Saltmarsh: Iron Tide", Owls, Completed, Gog)),
        Game("Saltmarsh: Low Tide", 16, 450, [Adventure],
            Project("Saltmarsh: Low Tide", Lanterns, Completed, Steam),
            Project("Saltmarsh: Low Tide", Cranes, Proofreading, Steam, progress: 64)),
        Game("Hollow Crown", 17, 80, [Action], Project("Hollow Crown", Moths, Completed, Steam, dubbing: true)),
        Game("Lantern Coast", 18, 60, [Adventure], Project("Lantern Coast", Lanterns, Completed, Steam)),
        Game("Vesper Lake", null, 20, [Action], Project("Vesper Lake", Cranes, Translating, Steam, progress: 40)),
        Game("Quiet Harbor", null, 15, [Adventure], Project("Quiet Harbor", Lanterns, Proofreading, Gog, changedDaysAgo: 17)),
        Game("Iron Meridian", null, 10, [Adventure], Project("Iron Meridian", Lanterns, Announced, Steam)),
        Game("Dust Archive", null, 5, [Action], Project("Dust Archive", Owls, Abandoned, Steam)),
        Game("Velvet Abyss", 3, 700, [Adventure], Project("Velvet Abyss", Owls, Completed, Steam)) with { Game = AdultGame("Velvet Abyss") },
    ], new CatalogueReference
    {
        Statuses = [Completed, Proofreading, Translating, Announced, Abandoned],
        Types = [new ProjectType { Uuid = Id("translation"), Name = "Spolszczenie", IsDefault = true }, Dubbing],
        Genres = [Adventure, Action],
        StorefrontPlatforms = [Steam, Gog],
        Teams = [Full(Lanterns), Full(Owls), Full(Cranes), Full(Moths)],
    });

    /// <summary>The games found on this computer, by their id on the site.</summary>
    public static IReadOnlyDictionary<Guid, GameKey[]> Detected { get; } = new Dictionary<Guid, GameKey[]>
    {
        [Id("Ember Hollow")] = [EmberKey],
        [Id("Iron Meridian")] = [MeridianKey],
    };

    /// <summary>Answers the catalogue's questions from a listing, and every other request with what <paramref name="other"/> says.</summary>
    public static Func<Message, Message?> Answer(CatalogueListing? listing = null, Func<Message, Message?>? other = null)
    {
        listing ??= Listing;
        return request => request switch
        {
            GetCatalogueFilters => new CatalogueFiltersResult(CatalogueFiltersView.From(listing.Reference)) { Facets = listing.Facets(new CatalogueQuery()) },
            GetCataloguePage page => Page(listing, page),
            _ => other?.Invoke(request),
        };
    }

    /// <summary>A catalogue of many plain games, to scroll through.</summary>
    public static CatalogueListing Many(int count) => new(TestStates.Now,
        [.. Enumerable.Range(1, count).Select(index => Game($"Tide Runner {index:D3}", index, 0, [], Project($"Tide Runner {index:D3}", Lanterns, Completed, Steam)))],
        new CatalogueReference { Statuses = [Completed], Teams = [Full(Lanterns)] });

    /// <summary>The longest slice the agent answers with, as its <c>AgentQueries.MaxCataloguePage</c> cuts it.</summary>
    public const int MaxSlice = 200;

    public static Guid Id(string text) => new(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("catalogue:" + text))[..16]);

    private static CataloguePageResult Page(CatalogueListing listing, GetCataloguePage request)
    {
        var detected = Detected.Keys.Where(id => listing.Games.Any(game => game.Game.Uuid == id)).ToHashSet();
        var within = request.DetectedOnly ? detected : null;
        var found = listing.Search(request.Query, Text.Comparer, within);
        return new CataloguePageResult(found.Count, [.. found.Skip(request.Offset).Take(Math.Min(request.Count, MaxSlice)).Select(listed =>
            CatalogueGameView.From(listed with { Projects = [.. CatalogueListing.CardProjects(listed, request.Query)] }) with
            {
                Detected = Detected.GetValueOrDefault(listed.Game.Uuid, []),
            })], listing.FetchedAt)
        {
            Facets = listing.Facets(request.Query, within),
            Detected = listing.Count(request.Query, detected),
            Unfiltered = listing.Count(new CatalogueQuery { Text = request.Query.Text }),
        };
    }

    private static ListedGame Game(string name, int? releasedDaysAgo, long downloads, Genre[] genres, params ProjectSummary[] projects) => new(
        new GameSummary
        {
            Uuid = Id(name),
            Slug = Slug(name),
            Name = name,
            Cover = TestGames.Cover(Slug(name)),
            Genres = genres,
            LatestReleaseAt = releasedDaysAgo is { } days ? TestStates.Now.AddDays(-days) : null,
            LatestReleaseVersion = releasedDaysAgo is null ? "" : "v1",
            TotalDownloads = downloads,
        },
        projects);

    private static GameSummary AdultGame(string name) => new()
    {
        Uuid = Id(name),
        Slug = Slug(name),
        Name = name,
        Cover = TestGames.Cover(Slug(name)),
        IsAdult = true,
        LatestReleaseAt = TestStates.Now.AddDays(-3),
        TotalDownloads = 700,
    };

    private static ProjectSummary Project(string game, TeamReference team, ProjectStatus status, StorefrontPlatform store, int? progress = null,
        int changedDaysAgo = 0, bool dubbing = false) => new()
    {
        Uuid = Id(game + team.Slug),
        Slug = Slug(game),
        Name = game,
        Team = team,
        Status = status,
        Types = dubbing ? [Dubbing] : [],
        PrimaryProgress = progress is { } percent ? new ProgressStatus { Uuid = Id(game + "progress"), Percentage = percent } : null,
        StorefrontLinks = [new StorefrontLink { Uuid = Id(game + store.Name), Platform = store }],
        FirstPublishedAt = TestStates.Now.AddDays(changedDaysAgo == 0 ? -17 : -31),
        UpdatedAt = TestStates.Now.AddDays(changedDaysAgo == 0 ? -17 : -changedDaysAgo),
    };

    private static ProjectStatus Status(string name, string color, int position, bool completed = false, bool abandoned = false) =>
        new() { Uuid = Id(name), Name = name, Color = color, Position = position, IsCompleted = completed, IsAbandoned = abandoned };

    private static TeamReference Team(TeamView team) => new() { Uuid = team.Id, Slug = team.Slug, Name = team.Name };

    private static Team Full(TeamReference team) => new() { Uuid = team.Uuid, Slug = team.Slug, Name = team.Name, Avatar = team.Avatar };

    private static string Slug(string name) => string.Concat(name.ToLowerInvariant().Select(character => char.IsLetterOrDigit(character) ? character : '-'));
}
