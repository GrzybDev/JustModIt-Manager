using System.Globalization;
using JustModIt.Manager.Core.Catalog;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.Tests.Catalog.TestSupport;

namespace JustModIt.Manager.Core.Tests.Catalog;

public sealed class CatalogueListingTests : IDisposable
{
    private static readonly Guid Adventure = Guid.Parse("d21de9bd-0000-4000-8000-000000000001");
    private static readonly Guid Testing = Guid.Parse("968ad28b-0000-4000-8000-000000000001");
    private static readonly Guid Voice = Guid.Parse("fae573dd-0000-4000-8000-000000000001");
    private static readonly Guid PaperMoth = Guid.Parse("53c4c7cd-0000-4000-8000-000000000002");
    private static readonly Guid Gog = Guid.Parse("bad65a0d-0000-4000-8000-000000000001");

    private static readonly StringComparer Names = StringComparer.Create(CultureInfo.GetCultureInfo("pl-PL"), ignoreCase: true);

    private readonly FakeSite _site = new();

    [Fact]
    public async Task Finds_a_project_by_each_of_its_store_ids()
    {
        var listing = await _site.CrawlAsync();

        var bySteam = listing.Find(new GameKey(StoreId.Steam, "1000010"));
        var byGog = listing.Find(new GameKey(StoreId.Gog, "9007199254740993"));

        bySteam.Should().ContainSingle().Which.Locator.Should().Be(
            new ProjectLocator("starlight-harbor", "lantern-works", Guid.Parse("50d8a47a-0000-4000-8000-000000000003")));
        byGog.Should().ContainSingle().Which.Should().Be(bySteam[0]);
        listing.Find(new GameKey(StoreId.Gog, "9007199254740992")).Should().BeEmpty();
    }

    [Fact]
    public async Task Finds_every_project_made_for_a_game()
    {
        var listing = await _site.CrawlAsync();

        var projects = listing.Find(new GameKey(StoreId.Steam, "1000030"));

        projects.Select(listed => listed.Project.Team.Slug).Should().Equal("paper-moth", "lantern-works");
        projects.Should().OnlyContain(listed => listed.Game.Slug == "clockwork-tides");
    }

    [Theory]
    [InlineData("starlight", "starlight-harbor")]
    [InlineData("HARBOR star", "starlight-harbor")]
    [InlineData("Émber röad", "ember-road")]
    [InlineData("paper moth", "clockwork-tides,ember-road")]
    [InlineData("tides lantern", "clockwork-tides")]
    [InlineData("", "clockwork-tides,ember-road,silent-orchard,starlight-harbor")]
    [InlineData("orchard harbor", "")]
    public async Task Searches_names_of_games_projects_and_teams_ignoring_case_and_diacritics(string text, string expected)
    {
        var listing = await _site.CrawlAsync();

        var found = listing.Search(new CatalogueQuery { Text = text, IncludeAdult = true, Order = CatalogueOrder.Alphabetical }, Names);

        string.Join(',', found.Select(listed => listed.Game.Slug)).Should().Be(expected);
    }

    [Fact]
    public async Task Filters_by_game_and_by_project_facts()
    {
        var listing = await _site.CrawlAsync();

        Slugs(listing.Search(new CatalogueQuery { GenreUuid = Adventure }, Names)).Should().Equal("clockwork-tides", "starlight-harbor");
        Slugs(listing.Search(new CatalogueQuery { StatusUuid = Testing }, Names)).Should().Equal("clockwork-tides");
        Slugs(listing.Search(new CatalogueQuery { TypeUuid = Voice }, Names)).Should().Equal("starlight-harbor");
        Slugs(listing.Search(new CatalogueQuery { StorefrontUuid = Gog }, Names)).Should().Equal("starlight-harbor");
        Slugs(listing.Search(new CatalogueQuery { TeamUuid = PaperMoth, GenreUuid = Adventure }, Names)).Should().Equal("clockwork-tides");
    }

    [Fact]
    public void Status_type_and_store_must_fit_one_listed_project_while_the_team_may_have_made_another()
    {
        var steam = new StorefrontPlatform { Uuid = Guid.NewGuid(), Name = "Steam" };
        var gog = new StorefrontPlatform { Uuid = Guid.NewGuid(), Name = "GOG" };
        var completed = new ProjectStatus { Uuid = Guid.NewGuid(), Name = "Completed" };
        var abandoned = new ProjectStatus { Uuid = Guid.NewGuid(), Name = "Abandoned", IsAbandoned = true };
        var lantern = new TeamReference { Uuid = Guid.NewGuid(), Slug = "lantern", Name = "Lantern" };
        var moth = new TeamReference { Uuid = Guid.NewGuid(), Slug = "moth", Name = "Moth" };
        var listing = new CatalogueListing(DateTimeOffset.UnixEpoch,
        [
            Listed("Mixed", Project(status: completed, team: lantern, store: steam), Project(team: moth, store: gog)),
            Listed("Left behind", Project(status: completed, team: moth, store: steam), Project(status: abandoned, team: lantern, store: gog)),
        ], new CatalogueReference());

        GameNames(listing, new CatalogueQuery { StatusUuid = completed.Uuid, StorefrontUuid = gog.Uuid }).Should().BeEmpty();
        GameNames(listing, new CatalogueQuery { StorefrontUuid = gog.Uuid }).Should().Equal("Mixed");
        GameNames(listing, new CatalogueQuery { TeamUuid = lantern.Uuid }).Should().Equal("Mixed");
        GameNames(listing, new CatalogueQuery { StatusUuid = completed.Uuid, TeamUuid = moth.Uuid, Order = CatalogueOrder.Alphabetical })
            .Should().Equal("Left behind", "Mixed");
    }

    [Fact]
    public async Task Leaves_adult_games_out_unless_asked_for()
    {
        var listing = await _site.CrawlAsync();

        Slugs(listing.Search(new CatalogueQuery(), Names)).Should().Equal("clockwork-tides", "starlight-harbor", "silent-orchard");
        Slugs(listing.Search(new CatalogueQuery { IncludeAdult = true }, Names)).Should().Contain("ember-road");
    }

    [Fact]
    public async Task Orders_by_the_newest_release_by_downloads_or_by_name()
    {
        var listing = await _site.CrawlAsync();

        Slugs(listing.Search(new CatalogueQuery { IncludeAdult = true }, Names))
            .Should().Equal("ember-road", "clockwork-tides", "starlight-harbor", "silent-orchard");
        Slugs(listing.Search(new CatalogueQuery { IncludeAdult = true, Order = CatalogueOrder.Popular }, Names))
            .Should().Equal("starlight-harbor", "ember-road", "clockwork-tides", "silent-orchard");
        Slugs(listing.Search(new CatalogueQuery { IncludeAdult = true, Order = CatalogueOrder.Alphabetical }, Names))
            .Should().Equal("clockwork-tides", "ember-road", "silent-orchard", "starlight-harbor");
    }

    [Fact]
    public void Orders_games_without_a_release_by_their_newest_listed_project_then_by_the_language_alphabet()
    {
        var abandoned = new ProjectStatus { Uuid = Guid.NewGuid(), Name = "Abandoned", IsAbandoned = true };
        var listing = new CatalogueListing(DateTimeOffset.UnixEpoch,
        [
            Listed("Mgła", Project(new DateTimeOffset(2026, 8, 10, 0, 0, 0, TimeSpan.Zero))),
            Listed("Lato", Project()),
            Listed("Łąka", Project(new DateTimeOffset(2026, 8, 10, 0, 0, 0, TimeSpan.Zero)),
                Project(new DateTimeOffset(2026, 8, 20, 0, 0, 0, TimeSpan.Zero), abandoned)),
            Listed("Kra", Project(new DateTimeOffset(2026, 8, 12, 0, 0, 0, TimeSpan.Zero))),
        ], new CatalogueReference());

        GameNames(listing, new CatalogueQuery()).Should().Equal("Kra", "Łąka", "Mgła", "Lato");
    }

    [Theory]
    [InlineData("Starlight  Harbor: Deluxe!", "starlight harbor deluxe")]
    [InlineData("Łódź Café", "lodz cafe")]
    [InlineData("  --  ", "")]
    [InlineData("Saga 2", "saga 2")]
    public void Folds_text_for_searching(string text, string folded) => SearchText.Fold(text).Should().Be(folded);

    public void Dispose() => _site.Dispose();

    private static IEnumerable<string> Slugs(IEnumerable<ListedGame> games) => games.Select(listed => listed.Game.Slug);

    private static ListedGame Listed(string name, params ProjectSummary[] projects) =>
        new(new GameSummary { Uuid = Guid.NewGuid(), Slug = name, Name = name }, projects);

    private static IEnumerable<string> GameNames(CatalogueListing listing, CatalogueQuery query) =>
        listing.Search(query, Names).Select(listed => listed.Game.Name);

    private static ProjectSummary Project(DateTimeOffset? firstPublishedAt = null, ProjectStatus? status = null,
        TeamReference? team = null, StorefrontPlatform? store = null) => new()
    {
        Uuid = Guid.NewGuid(),
        Slug = "example-project",
        Name = "Example Project",
        Team = team ?? new TeamReference { Uuid = Guid.NewGuid(), Slug = "example-team", Name = "Example Team" },
        Status = status,
        StorefrontLinks = store is null ? [] : [new StorefrontLink { Uuid = Guid.NewGuid(), Platform = store }],
        FirstPublishedAt = firstPublishedAt,
    };
}
