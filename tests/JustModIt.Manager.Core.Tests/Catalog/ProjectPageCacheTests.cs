using System.Net;
using System.Text.Json.Nodes;
using JustModIt.Manager.Core.Catalog;
using JustModIt.Manager.Core.Tests.Catalog.TestSupport;
using JustModIt.Manager.Core.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;

namespace JustModIt.Manager.Core.Tests.Catalog;

public sealed class ProjectPageCacheTests : IDisposable
{
    private const string PagePath = "/games/v1/starlight-harbor/projects/lantern-works/";
    private static readonly ProjectLocator Starlight = new("starlight-harbor", "lantern-works", Guid.Parse("50e0e0e0-0000-4000-8000-000000000003"));
    private static readonly DateTimeOffset PageUpdatedAt = DateTimeOffset.Parse("2026-08-07T19:05:03.414102+02:00", System.Globalization.CultureInfo.InvariantCulture);

    private readonly TempDirectory _directory = new();
    private readonly FakeSite _site = new();
    private readonly CatalogueCache _cache;
    private readonly ProjectPageCache _pages;

    public ProjectPageCacheTests()
    {
        _cache = new CatalogueCache(_directory.Path, _site.Time, NullLogger<CatalogueCache>.Instance);
        _pages = new ProjectPageCache(_site.Api, _cache, _site.Time, NullLogger<ProjectPageCache>.Instance);
    }

    [Fact]
    public async Task Serves_the_saved_page_through_a_burst_of_checks_while_the_catalogue_shows_no_change()
    {
        var first = await GetAsync(PageFreshness.Current, PageUpdatedAt);
        _site.Time.Advance(TimeSpan.FromMinutes(30));
        var second = await GetAsync(PageFreshness.Current, PageUpdatedAt);

        _site.CountRequests(PagePath).Should().Be(1);
        second!.FetchedAt.Should().Be(first!.FetchedAt);
        second.IsStale.Should().BeFalse();
        second.Page.Name.Should().Be("Starlight Harbor");
    }

    [Fact]
    public async Task Asks_again_once_the_project_changed_and_at_every_scheduled_refresh()
    {
        await GetAsync(PageFreshness.Current, PageUpdatedAt);

        await GetAsync(PageFreshness.Current, PageUpdatedAt.AddMinutes(1));
        _site.CountRequests(PagePath).Should().Be(2);

        // The earliest the schedule's jitter brings the next refresh, which must see a release hidden meanwhile.
        _site.Time.Advance(CatalogueRefreshLoop.Interval * 0.9);
        await GetAsync(PageFreshness.Current, PageUpdatedAt);
        _site.CountRequests(PagePath).Should().Be(3);
    }

    [Fact]
    public async Task Asks_again_for_a_page_saved_while_the_clock_was_ahead()
    {
        var document = ProjectPageDocument.Parse(System.Text.Encoding.UTF8.GetBytes(CatalogueFixtures.Read("project-starlight-harbor.json")));
        await _cache.SavePageAsync(document, _site.Time.GetUtcNow().AddDays(3), TestContext.Current.CancellationToken);

        var page = await GetAsync(PageFreshness.Current, PageUpdatedAt);

        _site.CountRequests(PagePath).Should().Be(1);
        page!.FetchedAt.Should().Be(_site.Time.GetUtcNow());
    }

    [Fact]
    public async Task Always_asks_for_a_page_the_catalogue_does_not_list_and_before_a_job()
    {
        await GetAsync(PageFreshness.Current, null);
        await GetAsync(PageFreshness.Current, null);
        await GetAsync(PageFreshness.Fresh, PageUpdatedAt);

        _site.CountRequests(PagePath).Should().Be(3);
    }

    [Fact]
    public async Task Serves_the_saved_page_while_the_site_cannot_be_reached()
    {
        var saved = await GetAsync(PageFreshness.Current, PageUpdatedAt);
        _site.IsOffline = true;

        var page = await CompleteAsync(GetAsync(PageFreshness.Fresh, PageUpdatedAt));

        page!.FetchedAt.Should().Be(saved!.FetchedAt);
        page.IsStale.Should().BeTrue();
        page.RefreshError!.Reason.Should().Be(CatalogueErrorReason.Offline);
        page.IsGone.Should().BeFalse();
    }

    [Fact]
    public async Task Fails_while_offline_when_nothing_was_saved()
    {
        _site.IsOffline = true;

        var act = () => CompleteAsync(GetAsync(PageFreshness.Current, PageUpdatedAt));

        (await act.Should().ThrowAsync<CatalogueException>()).Which.Reason.Should().Be(CatalogueErrorReason.Offline);
    }

    [Fact]
    public async Task Keeps_showing_a_saved_page_that_the_site_no_longer_has()
    {
        await GetAsync(PageFreshness.Current, PageUpdatedAt);
        _site.Routes.Remove(PagePath);

        var page = await GetAsync(PageFreshness.Fresh, PageUpdatedAt);

        page!.IsGone.Should().BeTrue();
        page.RefreshError!.Reason.Should().Be(CatalogueErrorReason.NotFound);
        page.Page.Name.Should().Be("Starlight Harbor");
        (await _pages.GetPageAsync(Starlight, null, TestContext.Current.CancellationToken)).Should().BeNull(
            "an index leaves out what the site no longer has");
    }

    [Fact]
    public async Task Reports_nothing_for_a_page_that_never_existed()
    {
        _site.Routes.Remove(PagePath);

        (await GetAsync(PageFreshness.Current, null)).Should().BeNull();
    }

    [Fact]
    public async Task Treats_slugs_that_now_lead_to_another_project_as_gone()
    {
        var replaced = JsonNode.Parse(CatalogueFixtures.Read("project-starlight-harbor.json"))!;
        replaced["uuid"] = "50e0e0e0-0000-4000-8000-000000000099";
        _site.Routes[PagePath] = () => FakeHttpHandler.Json(replaced.ToJsonString());

        (await GetAsync(PageFreshness.Current, null)).Should().BeNull();
        _directory.Files().Should().BeEmpty();
    }

    [Fact]
    public async Task Still_serves_a_fresh_page_when_it_cannot_be_saved()
    {
        _directory.Write("projects", "a file where the folder should be");

        var page = await GetAsync(PageFreshness.Current, PageUpdatedAt);

        page!.Page.Name.Should().Be("Starlight Harbor");
        _site.Routes[PagePath] = () => FakeHttpHandler.Text(HttpStatusCode.NotFound, "", "text/html");
        (await GetAsync(PageFreshness.Current, PageUpdatedAt)).Should().BeNull();
    }

    public void Dispose()
    {
        _site.Dispose();
        _directory.Dispose();
    }

    private Task<CataloguePage?> GetAsync(PageFreshness freshness, DateTimeOffset? listedUpdatedAt) =>
        _pages.GetAsync(Starlight, freshness, listedUpdatedAt, TestContext.Current.CancellationToken);

    // Moves the clock through the retry handler's waits; a failure surfaces when the task is awaited.
    private async Task<T> CompleteAsync<T>(Task<T> task)
    {
        try
        {
            await _site.Time.RunUntilCompletedAsync(task, TimeSpan.FromSeconds(1));
        }
        catch (CatalogueException)
        {
        }

        return await task;
    }
}
