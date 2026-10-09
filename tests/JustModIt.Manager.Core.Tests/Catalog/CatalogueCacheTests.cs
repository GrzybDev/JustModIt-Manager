using System.Text;
using System.Text.Json.Nodes;
using JustModIt.Manager.Core.Catalog;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.Tests.Catalog.TestSupport;
using JustModIt.Manager.Core.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;

namespace JustModIt.Manager.Core.Tests.Catalog;

public sealed class CatalogueCacheTests : IDisposable
{
    private static readonly DateTimeOffset FetchedAt = new(2026, 9, 1, 12, 0, 0, TimeSpan.FromHours(2));

    private readonly TempDirectory _directory = new();
    private readonly FakeSite _site = new();
    private readonly CatalogueCache _cache;

    public CatalogueCacheTests() =>
        _cache = new CatalogueCache(_directory.Path, TimeProvider.System, NullLogger<CatalogueCache>.Instance);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Reads_nothing_before_anything_was_saved()
    {
        (await _cache.LoadListingAsync(Token)).Should().BeNull();
        (await _cache.LoadIndexAsync(Token)).Should().BeNull();
        (await _cache.LoadSiteAsync(Token)).Should().BeNull();
        (await _cache.LoadPageAsync(Guid.NewGuid(), Token)).Should().BeNull();
    }

    [Fact]
    public async Task Round_trips_the_listing()
    {
        var listing = await _site.CrawlAsync();

        await _cache.SaveListingAsync(listing, Token);
        var loaded = (await _cache.LoadListingAsync(Token))!;

        loaded.FetchedAt.Should().Be(listing.FetchedAt);
        loaded.Games.Select(listed => listed.Game.Slug).Should().Equal(listing.Games.Select(listed => listed.Game.Slug));
        loaded.Games[0].Projects.Should().HaveCount(2);
        loaded.Find(new GameKey(StoreId.Gog, "9007199254740993")).Should().ContainSingle();
        loaded.Reference.Should().BeEquivalentTo(listing.Reference, options => options.Excluding(reference => reference.Teams));
        loaded.Reference.Teams.Select(team => team.Slug).Should().Equal("lantern-works", "paper-moth");
        loaded.Search(new CatalogueQuery { Text = "lantern tides" }, StringComparer.Ordinal).Should().ContainSingle();
    }

    [Fact]
    public async Task Round_trips_the_index_and_the_site()
    {
        var index = new InstallableIndex(FetchedAt,
            [IndexEntry.From(CatalogueFixtures.Project("starlight-harbor")), IndexEntry.From(CatalogueFixtures.Project("silent-orchard"))]);
        var site = new SiteConfiguration
        {
            Name = "Example Mods",
            Language = "en",
            IsUnderMaintenance = true,
            ReleaseFiles = new ReleaseFileRules
            {
                MaxBytes = 1024,
                Formats = [new ReleaseFileFormat { Extension = ".examplemod", RequiresManager = true }],
            },
        };

        await _cache.SaveIndexAsync(index, Token);
        await _cache.SaveSiteAsync(site, FetchedAt, Token);
        var loaded = (await _cache.LoadIndexAsync(Token))!;

        loaded.BuiltAt.Should().Be(FetchedAt);
        loaded.Entries.Should().HaveCount(2);
        var entry = loaded.Find(new GameKey(StoreId.Steam, "1000010")).Should().ContainSingle().Subject;
        entry.PublicRelease!.Files.Select(file => file.Format).Should().Equal(
            PackageFormat.Zip, PackageFormat.Zip, PackageFormat.Manifest, PackageFormat.Manifest, PackageFormat.Zip);
        entry.PublicRelease.Files[4].OperatingSystems.Should().Equal(OperatingSystemKind.Unknown);
        entry.Status!.AllowsPublicReleasesFlag.Should().BeNull("a flag the backend did not send stays unknown");
        ReleaseSelector.Select(entry, new GameKey(StoreId.Steam, "1000010"), OperatingSystemKind.Windows).Default!.Package!.Uuid
            .Should().Be(Guid.Parse("a0000000-0000-4000-8000-000000000002"));
        (await _cache.LoadSiteAsync(Token)).Should().BeEquivalentTo(site);
    }

    [Fact]
    public async Task Keeps_a_page_as_the_site_sent_it()
    {
        var page = await _site.Api.GetProjectPageAsync("starlight-harbor", "lantern-works", Token);

        await _cache.SavePageAsync(page, FetchedAt, Token);
        var loaded = (await _cache.LoadPageAsync(page.Page.Uuid, Token))!;

        loaded.FetchedAt.Should().Be(FetchedAt);
        loaded.Document.Page.Name.Should().Be("Starlight Harbor");
        JsonNode.DeepEquals(JsonNode.Parse(loaded.Document.Json.Span), JsonNode.Parse(page.Json.Span)).Should().BeTrue();
        Encoding.UTF8.GetString(loaded.Document.Json.Span).Should().Contain("checksum_of_the_future");
    }

    [Fact]
    public async Task Overwrites_an_earlier_copy()
    {
        var page = await _site.Api.GetProjectPageAsync("ember-road", "paper-moth", Token);
        await _cache.SavePageAsync(page, FetchedAt, Token);

        await _cache.SavePageAsync(page, FetchedAt.AddHours(1), Token);

        (await _cache.LoadPageAsync(page.Page.Uuid, Token))!.FetchedAt.Should().Be(FetchedAt.AddHours(1));
        _directory.Files().Should().ContainSingle();
    }

    [Theory]
    [InlineData("")]
    [InlineData("{\"version\": 1, \"fetched_at\": \"2026-09-01T12:00:00+02:00\", \"data\": {\"entries\": [")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{\"version\": 2, \"fetched_at\": \"2026-09-01T12:00:00+02:00\", \"data\": {\"entries\": []}}")]
    [InlineData("{\"fetched_at\": \"2026-09-01T12:00:00+02:00\", \"data\": {\"entries\": []}}")]
    [InlineData("{\"version\": 1, \"fetched_at\": \"yesterday\", \"data\": {\"entries\": []}}")]
    [InlineData("{\"version\": 1, \"fetched_at\": \"2026-09-01T12:00:00+02:00\"}")]
    [InlineData("{\"version\": 1, \"fetched_at\": \"2026-09-01T12:00:00+02:00\", \"data\": null}")]
    [InlineData("{\"version\": 1, \"fetched_at\": \"2026-09-01T12:00:00+02:00\", \"data\": {\"entries\": [{\"project_slug\": \"x\"}]}}")]
    public async Task Reads_a_damaged_or_foreign_file_as_absent_and_heals_on_the_next_save(string content)
    {
        _directory.Write("index.json", content);

        (await _cache.LoadIndexAsync(Token)).Should().BeNull();

        await _cache.SaveIndexAsync(new InstallableIndex(FetchedAt, []), Token);
        (await _cache.LoadIndexAsync(Token))!.BuiltAt.Should().Be(FetchedAt);
    }

    [Fact]
    public async Task Reads_a_page_saved_under_another_project_as_absent()
    {
        var page = await _site.Api.GetProjectPageAsync("ember-road", "paper-moth", Token);
        await _cache.SavePageAsync(page, FetchedAt, Token);
        var other = Guid.NewGuid();
        File.Move(_directory.Combine(Path.Combine("projects", $"{page.Page.Uuid:D}.json")),
            _directory.Combine(Path.Combine("projects", $"{other:D}.json")));

        (await _cache.LoadPageAsync(other, Token)).Should().BeNull();
    }

    public void Dispose()
    {
        _site.Dispose();
        _directory.Dispose();
    }
}
