using JustModIt.Manager.Core.Catalog;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.Tests.Catalog.TestSupport;
using JustModIt.Manager.Core.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;

namespace JustModIt.Manager.Core.Tests.Catalog;

public sealed class CatalogueServiceTests : IDisposable
{
    private static readonly GameKey Starlight = new(StoreId.Steam, "1000010");
    private static readonly GameKey Clockwork = new(StoreId.Steam, "1000030");
    private static readonly IndexRequest StarlightOnly = new([Starlight], []);

    private readonly TempDirectory _directory = new();
    private readonly FakeSite _site = new();
    private readonly List<CatalogueSnapshot> _announced = [];

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Refreshes_the_catalogue_and_saves_it_for_offline_use()
    {
        var service = Create();

        var snapshot = await service.RefreshAsync(StarlightOnly, Token);

        snapshot.State.Should().Be(CatalogueState.Fresh);
        snapshot.FetchedAt.Should().Be(_site.Time.GetUtcNow());
        snapshot.Site.Name.Should().Be("Example Mods");
        snapshot.Index.Find(Starlight).Should().ContainSingle();
        _announced.Select(announced => announced.State).Should().Equal(CatalogueState.Refreshing, CatalogueState.Fresh);
        service.Current.Should().BeSameAs(snapshot);
        _directory.Files().Should().BeEquivalentTo(
            "listing.json", "index.json", "site.json", Path.Combine("projects", "50d8a47a-0000-4000-8000-000000000003.json"));
    }

    [Fact]
    public async Task Starts_from_the_saved_copy_without_asking_the_site()
    {
        await Create().RefreshAsync(StarlightOnly, Token);
        var requests = _site.Handler.Requests.Count;
        _announced.Clear();

        var service = Create();
        var snapshot = await service.LoadCachedAsync(Token);

        _site.Handler.Requests.Should().HaveCount(requests);
        snapshot.State.Should().Be(CatalogueState.Cached);
        snapshot.Index.Find(Starlight).Should().ContainSingle();
        snapshot.Site.Name.Should().Be("Example Mods");
        snapshot.FetchedAt.Should().Be(_site.Time.GetUtcNow());
        (await service.GetListingAsync(Token))!.Games.Should().HaveCount(4);
        _site.Handler.Requests.Should().HaveCount(requests);
    }

    [Fact]
    public async Task Stays_empty_when_nothing_was_saved()
    {
        var service = Create();

        var snapshot = await service.LoadCachedAsync(Token);

        snapshot.Should().Be(CatalogueSnapshot.Empty);
        (await service.GetListingAsync(Token)).Should().BeNull();
    }

    [Fact]
    public async Task Keeps_serving_the_last_good_copy_while_the_site_cannot_be_reached()
    {
        await Create().RefreshAsync(StarlightOnly, Token);
        var service = Create();
        await service.LoadCachedAsync(Token);
        _site.IsOffline = true;

        var snapshot = await CompleteAsync(service.RefreshAsync(StarlightOnly, Token));

        snapshot.State.Should().Be(CatalogueState.Stale);
        snapshot.Failure.Should().Be(CatalogueErrorReason.Offline);
        snapshot.Index.Find(Starlight).Should().ContainSingle();

        _site.IsOffline = false;
        var recovered = await service.RefreshAsync(StarlightOnly, Token);
        recovered.State.Should().Be(CatalogueState.Fresh);
        recovered.Failure.Should().BeNull();
    }

    [Fact]
    public async Task Fails_without_a_saved_copy_and_passes_on_how_long_the_site_asked_to_wait()
    {
        _site.Routes["/games/v1/?page=1"] = () =>
        {
            var response = FakeHttpHandler.Json("""{"detail": "Slow down."}""", System.Net.HttpStatusCode.TooManyRequests);
            response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromHours(1));
            return response;
        };
        var service = Create();

        var snapshot = await service.RefreshAsync(StarlightOnly, Token);

        snapshot.State.Should().Be(CatalogueState.Failed);
        snapshot.Failure.Should().Be(CatalogueErrorReason.RateLimited);
        snapshot.RetryAfter.Should().Be(TimeSpan.FromHours(1));
        _directory.Files().Should().BeEmpty();
    }

    [Fact]
    public async Task Keeps_the_last_index_when_a_new_one_cannot_be_built()
    {
        var service = Create();
        var first = await service.RefreshAsync(StarlightOnly, Token);
        _site.Time.Advance(TimeSpan.FromHours(1));
        _site.Routes["/games/v1/clockwork-tides/projects/paper-moth/"] = () =>
            FakeHttpHandler.Text(System.Net.HttpStatusCode.BadGateway, "<html/>", "text/html");

        var snapshot = await CompleteAsync(service.RefreshAsync(new IndexRequest([Starlight, Clockwork], []), Token));

        snapshot.State.Should().Be(CatalogueState.Stale);
        snapshot.Failure.Should().Be(CatalogueErrorReason.ServerError);
        snapshot.FetchedAt.Should().Be(first.FetchedAt);
        snapshot.Index.Should().BeSameAs(first.Index);
    }

    [Fact]
    public async Task Builds_the_index_for_new_games_from_the_catalogue_read_last()
    {
        var service = Create();
        await service.RefreshAsync(StarlightOnly, Token);
        var crawls = _site.CountRequests("/games/v1/?page=1");

        var snapshot = await service.RebuildIndexAsync(new IndexRequest([Starlight, Clockwork], []), Token);

        _site.CountRequests("/games/v1/?page=1").Should().Be(crawls);
        _site.CountRequests("/games/v1/starlight-harbor/projects/lantern-works/").Should().Be(1, "its saved page is current");
        snapshot.Index.Find(Clockwork).Should().HaveCount(2);
        snapshot.State.Should().Be(CatalogueState.Fresh);
    }

    [Fact]
    public async Task Keeps_telling_how_current_the_catalogue_is_when_the_index_is_built_from_the_one_read_last()
    {
        await Create().RefreshAsync(StarlightOnly, Token);
        var service = Create();
        await service.LoadCachedAsync(Token);

        var cached = await service.RebuildIndexAsync(StarlightOnly, Token);
        _site.IsOffline = true;
        var failed = await CompleteAsync(service.RefreshAsync(StarlightOnly, Token));
        var rebuilt = await service.RebuildIndexAsync(StarlightOnly, Token);

        cached.State.Should().Be(CatalogueState.Cached);
        failed.State.Should().Be(CatalogueState.Stale);
        rebuilt.State.Should().Be(CatalogueState.Stale, "the saved pages serve offline, yet the last refresh failed");
        rebuilt.Failure.Should().Be(CatalogueErrorReason.Offline);
        rebuilt.Index.Find(Starlight).Should().ContainSingle();
    }

    [Fact]
    public async Task Tells_listeners_outside_the_queue_so_that_they_may_queue_more()
    {
        var service = Create();
        Task? queuedByListener = null;
        var queuedAtOnce = false;
        service.Changed += (_, snapshot) =>
        {
            if (snapshot.State != CatalogueState.Refreshing)
            {
                return;
            }

            var queued = new ManualResetEventSlim();
            ThreadPool.QueueUserWorkItem(_ =>
            {
                queuedByListener = service.LoadCachedAsync(Token);
                queued.Set();
            });
            queuedAtOnce = queued.Wait(TimeSpan.FromSeconds(5), Token);
        };

        await service.RefreshAsync(StarlightOnly, Token);

        queuedAtOnce.Should().BeTrue("another thread may queue work while a listener runs");
        await queuedByListener!;
    }

    [Fact]
    public async Task Puts_the_state_back_when_a_refresh_fails_unexpectedly()
    {
        var service = Create();
        await service.RefreshAsync(StarlightOnly, Token);
        _site.Routes["/games/v1/?page=1"] = () => throw new InvalidOperationException("A bug.");

        var refresh = async () => await service.RefreshAsync(StarlightOnly, Token);

        await refresh.Should().ThrowAsync<InvalidOperationException>();
        service.Current.State.Should().Be(CatalogueState.Fresh);
    }

    [Fact]
    public async Task Reuses_a_saved_page_for_a_game_page_and_asks_again_before_a_job()
    {
        var service = Create();
        await service.RefreshAsync(StarlightOnly, Token);
        var locator = service.Current.Index.Find(Starlight)[0].Locator;

        var shown = await service.GetProjectPageAsync(locator, PageFreshness.Current, Token);
        _site.CountRequests("/games/v1/starlight-harbor/projects/lantern-works/").Should().Be(1);
        var forJob = await service.GetProjectPageAsync(locator, PageFreshness.Fresh, Token);

        _site.CountRequests("/games/v1/starlight-harbor/projects/lantern-works/").Should().Be(2);
        shown!.Page.Uuid.Should().Be(forJob!.Page.Uuid);
    }

    [Fact]
    public async Task Reuses_a_saved_page_of_a_project_only_the_listing_knows()
    {
        var service = Create();
        await service.RefreshAsync(StarlightOnly, Token);
        var locator = new ProjectLocator("ember-road", "paper-moth", Guid.Parse("50d8a47a-0000-4000-8000-000000000002"));

        await service.GetProjectPageAsync(locator, PageFreshness.Current, Token);
        await service.GetProjectPageAsync(locator, PageFreshness.Current, Token);

        _site.CountRequests("/games/v1/ember-road/projects/paper-moth/").Should().Be(1);
    }

    [Fact]
    public async Task Puts_the_state_back_when_a_refresh_is_cancelled()
    {
        var service = Create();
        await service.RefreshAsync(StarlightOnly, Token);
        _site.Routes["/games/v1/?page=1"] = () => FakeHttpHandler.Stalled();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);

        var refresh = service.RefreshAsync(StarlightOnly, cancellation.Token);
        await Eventually.HoldsAsync(() => service.Current.State == CatalogueState.Refreshing, "the refresh starts");
        await cancellation.CancelAsync();

        var stopped = async () => await refresh;
        await stopped.Should().ThrowAsync<OperationCanceledException>();
        service.Current.State.Should().Be(CatalogueState.Fresh);
    }

    [Fact]
    public async Task Survives_a_listener_that_fails()
    {
        var service = Create();
        service.Changed += (_, _) => throw new InvalidOperationException("Listener bug.");

        var snapshot = await service.RefreshAsync(StarlightOnly, Token);

        snapshot.State.Should().Be(CatalogueState.Fresh);
    }

    public void Dispose()
    {
        _site.Dispose();
        _directory.Dispose();
    }

    private CatalogueService Create()
    {
        var cache = new CatalogueCache(_directory.Path, _site.Time, NullLogger<CatalogueCache>.Instance);
        var pages = new ProjectPageCache(_site.Api, cache, _site.Time, NullLogger<ProjectPageCache>.Instance);
        var service = new CatalogueService(new CatalogueCrawler(_site.Api, _site.Time), new CrawlIndexSource(pages, _site.Time), pages,
            cache, NullLogger<CatalogueService>.Instance);
        service.Changed += (_, snapshot) => _announced.Add(snapshot);
        return service;
    }

    private async Task<T> CompleteAsync<T>(Task<T> task)
    {
        await _site.Time.RunUntilCompletedAsync(task, TimeSpan.FromSeconds(1));
        return await task;
    }
}
