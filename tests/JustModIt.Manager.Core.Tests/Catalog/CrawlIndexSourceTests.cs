using System.Net;
using System.Text.Json.Nodes;
using JustModIt.Manager.Core.Catalog;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.Tests.Catalog.TestSupport;
using JustModIt.Manager.Core.Tests.TestSupport;

namespace JustModIt.Manager.Core.Tests.Catalog;

public sealed class CrawlIndexSourceTests : IDisposable
{
    private static readonly GameKey Starlight = new(StoreId.Steam, "1000010");
    private static readonly GameKey StarlightOnGog = new(StoreId.Gog, "9007199254740993");
    private static readonly GameKey Clockwork = new(StoreId.Steam, "1000030");
    private static readonly GameKey Orchard = new(StoreId.Steam, "1000040");
    private static readonly GameKey Grove = new(StoreId.Steam, "1000050");
    private static readonly ProjectLocator InstalledGrove = new("hidden-grove", "lantern-works", Guid.Parse("50d8a47a-0000-4000-8000-000000000005"));

    private readonly FakeSite _site = new();

    [Fact]
    public async Task Reads_the_pages_of_the_projects_made_for_the_games_only()
    {
        var listing = await _site.CrawlAsync();
        var crawled = _site.Handler.Requests.Count;

        var index = await BuildAsync(listing, [Starlight, Clockwork, Orchard, new GameKey(StoreId.Steam, "42")]);

        _site.Handler.Paths.Skip(crawled).Should().BeEquivalentTo(
            "/games/v1/starlight-harbor/projects/lantern-works/", "/games/v1/clockwork-tides/projects/paper-moth/",
            "/games/v1/clockwork-tides/projects/lantern-works/", "/games/v1/silent-orchard/projects/lantern-works/");
        index.Entries.Should().HaveCount(4);
        index.BuiltAt.Should().Be(_site.Time.GetUtcNow());
        index.Find(Clockwork).Select(entry => entry.Team.Slug).Should().Equal("paper-moth", "lantern-works");
        index.Find(StarlightOnGog).Should().ContainSingle().Which.Should().BeSameAs(index.Find(Starlight)[0]);
        index.Find(Orchard).Should().ContainSingle().Which.PublicRelease.Should().BeNull();
        index.Find(new GameKey(StoreId.Steam, "1000020")).Should().BeEmpty();
    }

    [Fact]
    public async Task Keeps_what_deciding_an_install_needs()
    {
        var listing = await _site.CrawlAsync();

        var entry = (await BuildAsync(listing, [Starlight])).Entries.Single();

        entry.Locator.Should().Be(new ProjectLocator("starlight-harbor", "lantern-works", Guid.Parse("50d8a47a-0000-4000-8000-000000000003")));
        entry.ProjectName.Should().Be("Starlight Harbor");
        entry.Team.Name.Should().Be("Lantern Works");
        entry.Game.Cover.Should().Be("https://storage.example.com/games/covers/starlight-harbor.jpeg");
        entry.PublicRelease!.Version.Should().Be(2);
        entry.PublicRelease.Files.Should().HaveCount(5);
        entry.StorefrontLinks.Where(link => link.GameKey == StarlightOnGog).Should().ContainSingle()
            .Which.Uuid.Should().Be(Guid.Parse("006bece2-0000-4000-8000-000000000032"));
        new ReleaseFileLocator(entry.Team.Slug, entry.ProjectSlug, entry.PublicRelease.Uuid, entry.PublicRelease.Files[0].Uuid)
            .Should().Be(new ReleaseFileLocator(
            "lantern-works", "starlight-harbor", Guid.Parse("f3713421-0000-4000-8000-000000000032"),
            Guid.Parse("a0000000-0000-4000-8000-000000000001")));
    }

    [Fact]
    public async Task Resolves_an_installed_project_that_the_catalogue_no_longer_lists()
    {
        var listing = await _site.CrawlAsync();

        var index = await BuildAsync(listing, [Grove], [InstalledGrove]);

        var entry = index.Find(Grove).Should().ContainSingle().Subject;
        entry.Status!.IsAbandoned.Should().BeTrue();
        entry.PublicRelease.Should().NotBeNull();
        index.FindProject(InstalledGrove.ProjectUuid).Should().BeSameAs(entry);
    }

    [Fact]
    public async Task Follows_the_current_slugs_of_an_installed_project()
    {
        var listing = await _site.CrawlAsync();
        var installedUnderOldSlugs = new ProjectLocator("ember-road-old", "paper-moth-old", Guid.Parse("50d8a47a-0000-4000-8000-000000000002"));

        var index = await BuildAsync(listing, [], [installedUnderOldSlugs]);

        index.Entries.Should().ContainSingle().Which.Locator.Should().Be(
            new ProjectLocator("ember-road", "paper-moth", installedUnderOldSlugs.ProjectUuid));
    }

    [Fact]
    public async Task Leaves_out_a_project_whose_page_is_gone_or_leads_to_another_project()
    {
        var listing = await _site.CrawlAsync();
        _site.Routes.Remove("/games/v1/starlight-harbor/projects/lantern-works/");
        var replaced = JsonNode.Parse(CatalogueFixtures.Read("project-hidden-grove.json"))!;
        replaced["uuid"] = "50d8a47a-0000-4000-8000-000000000099";
        _site.Routes["/games/v1/hidden-grove/projects/lantern-works/"] = () => FakeHttpHandler.Json(replaced.ToJsonString());

        var index = await BuildAsync(listing, [Starlight, Grove], [InstalledGrove]);

        index.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task Fails_as_a_whole_when_a_page_cannot_be_read()
    {
        var listing = await _site.CrawlAsync();
        _site.Routes["/games/v1/clockwork-tides/projects/lantern-works/"] = () =>
            FakeHttpHandler.Text(HttpStatusCode.ServiceUnavailable, "<html/>", "text/html");

        var build = BuildAsync(listing, [Starlight, Clockwork]);
        var act = async () => await _site.Time.RunUntilCompletedAsync(build, TimeSpan.FromSeconds(1));

        (await act.Should().ThrowAsync<CatalogueException>()).Which.Reason.Should().Be(CatalogueErrorReason.Maintenance);
    }

    public void Dispose() => _site.Dispose();

    private Task<InstallableIndex> BuildAsync(CatalogueListing listing, GameKey[] games, ProjectLocator[]? installed = null) =>
        new CrawlIndexSource(new ApiProjectPageSource(_site.Api), _site.Time)
            .BuildAsync(new IndexScope(games, installed ?? [], listing), TestContext.Current.CancellationToken);
}
