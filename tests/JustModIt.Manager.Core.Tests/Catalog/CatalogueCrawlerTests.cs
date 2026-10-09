using System.Net;
using System.Text.Json.Nodes;
using JustModIt.Manager.Core.Catalog;
using JustModIt.Manager.Core.Tests.Catalog.TestSupport;
using JustModIt.Manager.Core.Tests.TestSupport;

namespace JustModIt.Manager.Core.Tests.Catalog;

public sealed class CatalogueCrawlerTests : IDisposable
{
    private readonly FakeSite _site = new();

    [Fact]
    public async Task Reads_every_page_the_projects_of_games_with_several_and_the_reference_lists()
    {
        var listing = await _site.CrawlAsync();

        _site.Handler.Paths.Should().BeEquivalentTo(
            "/games/v1/?page=1&page_size=48&ordering=popular", "/games/v1/?page=1&page_size=48", "/games/v1/clockwork-tides/projects/",
            "/games/v1/?page=2&page_size=48", "/teams/v1/?page=1&previews=0",
            "/projects/v1/statuses/", "/projects/v1/types/", "/games/v1/genres/", "/games/v1/platforms/",
            "/projects/v1/storefront-platforms/", "/system/v1/config");
        listing.FetchedAt.Should().Be(_site.Time.GetUtcNow());
        listing.Games.Select(listed => listed.Game.Slug).Should().Equal("clockwork-tides", "ember-road", "silent-orchard", "starlight-harbor");
        listing.Games[0].Projects.Select(project => project.Team.Slug).Should().Equal("paper-moth", "lantern-works");
        listing.Reference.Site.Name.Should().Be("Example Mods");
    }

    [Fact]
    public async Task Takes_the_games_from_the_stable_order_and_only_their_downloads_from_the_popular_one()
    {
        var popular = JsonNode.Parse(CatalogueFixtures.Read("games-downloads.json"))!;
        var results = popular["results"]!.AsArray();
        results.RemoveAt(2);
        results.Add(JsonNode.Parse("""{"uuid": "6f1d0c3e-1b2a-4c5d-8e9f-000000000099", "slug": "unlisted", "total_downloads": 9000}"""));
        _site.Routes["/games/v1/?page=1&page_size=48&ordering=popular"] = () => FakeHttpHandler.Json(popular.ToJsonString());

        var listing = await _site.CrawlAsync();

        listing.Games.Select(listed => (listed.Game.Slug, listed.Game.TotalDownloads)).Should().Equal(
            ("clockwork-tides", null), ("ember-road", 1200), ("silent-orchard", null), ("starlight-harbor", 1500));
    }

    [Fact]
    public async Task Stops_when_a_later_page_has_disappeared()
    {
        _site.Routes.Remove("/games/v1/?page=2&page_size=48");

        var listing = await _site.CrawlAsync();

        listing.Games.Select(listed => listed.Game.Slug).Should().Equal("clockwork-tides", "ember-road");
    }

    [Fact]
    public async Task Leaves_out_a_game_whose_project_list_has_disappeared()
    {
        _site.Routes.Remove("/games/v1/clockwork-tides/projects/");

        var listing = await _site.CrawlAsync();

        listing.Games.Select(listed => listed.Game.Slug).Should().NotContain("clockwork-tides");
    }

    [Fact]
    public async Task Lists_a_game_and_a_project_repeated_by_a_shifting_list_once()
    {
        var first = JsonNode.Parse(CatalogueFixtures.Read("games-page-1.json"))!;
        var second = JsonNode.Parse(CatalogueFixtures.Read("games-page-2.json"))!;
        second["results"]!.AsArray().Add(first["results"]![1]!.DeepClone());
        _site.Routes["/games/v1/?page=2&page_size=48"] = () => FakeHttpHandler.Json(second.ToJsonString());

        var listing = await _site.CrawlAsync();

        listing.Games.Should().HaveCount(4);
        listing.FindProject(Guid.Parse("50d8a47a-0000-4000-8000-000000000002"))!.Game.Slug.Should().Be("ember-road");
    }

    [Fact]
    public async Task Asks_for_the_projects_of_a_game_whose_row_names_neither_its_project_nor_their_count()
    {
        var first = JsonNode.Parse(CatalogueFixtures.Read("games-page-1.json"))!;
        first["results"]![0]!.AsObject().Remove("visible_project_count");
        first["results"]![0]!.AsObject().Remove("solo_project");
        _site.Routes["/games/v1/?page=1&page_size=48"] = () => FakeHttpHandler.Json(first.ToJsonString());

        var listing = await _site.CrawlAsync();

        listing.Games[0].Projects.Select(project => project.Team.Slug).Should().Equal("paper-moth", "lantern-works");
    }

    [Fact]
    public async Task Leaves_reference_lists_empty_that_the_backend_does_not_offer()
    {
        _site.Routes.Remove("/projects/v1/statuses/");
        _site.Routes.Remove("/teams/v1/?page=1&previews=0");
        _site.Routes.Remove("/system/v1/config");

        var listing = await _site.CrawlAsync();

        listing.Reference.Statuses.Should().BeEmpty();
        listing.Reference.Teams.Should().BeEmpty();
        listing.Reference.Site.Should().Be(new SiteConfiguration());
        listing.Games.Should().HaveCount(4);
    }

    [Fact]
    public async Task Fails_as_a_whole_when_a_page_cannot_be_read()
    {
        _site.Routes["/games/v1/?page=2&page_size=48"] = () => FakeHttpHandler.Text(HttpStatusCode.BadGateway, "<html/>", "text/html");

        var crawl = _site.CrawlAsync();
        var act = async () => await _site.Time.RunUntilCompletedAsync(crawl, TimeSpan.FromSeconds(1));

        (await act.Should().ThrowAsync<CatalogueException>()).Which.Reason.Should().Be(CatalogueErrorReason.ServerError);
    }

    public void Dispose() => _site.Dispose();
}
