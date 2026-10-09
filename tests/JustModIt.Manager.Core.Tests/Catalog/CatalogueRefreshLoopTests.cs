using JustModIt.Manager.Core.Catalog;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.Tests.Catalog.TestSupport;
using JustModIt.Manager.Core.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;

namespace JustModIt.Manager.Core.Tests.Catalog;

public sealed class CatalogueRefreshLoopTests : IDisposable
{
    private const string FirstPage = "/games/v1/?page=1&page_size=48";

    private readonly TempDirectory _directory = new();
    private readonly FakeSite _site = new();

    [Theory]
    [InlineData(CatalogueState.Fresh, null, 6 * 60)]
    [InlineData(CatalogueState.Stale, null, 15)]
    [InlineData(CatalogueState.Failed, null, 15)]
    [InlineData(CatalogueState.Stale, 5, 15)]
    [InlineData(CatalogueState.Stale, 120, 120)]
    public void Waits_six_hours_after_a_success_and_a_quarter_of_an_hour_after_a_failure(CatalogueState state, int? retryAfterMinutes,
        int expectedMinutes)
    {
        var snapshot = CatalogueSnapshot.Empty with
        {
            State = state,
            RetryAfter = retryAfterMinutes is { } minutes ? TimeSpan.FromMinutes(minutes) : null,
        };

        for (var attempt = 0; attempt < 20; attempt++)
        {
            CatalogueRefreshLoop.NextDelay(snapshot).TotalMinutes.Should().BeInRange(expectedMinutes * 0.9, expectedMinutes * 1.1);
        }
    }

    [Fact]
    public async Task Refreshes_after_the_start_delay_on_request_and_on_schedule()
    {
        // The loop waits on a clock of its own, so moving it never runs out the time limit of a request in flight.
        var loopTime = new CountingTimeProvider(_site.Time.GetUtcNow());
        var (service, loop) = Create(loopTime);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var running = Task.CompletedTask;

        await loopTime.WakeAsync(() => running = loop.RunAsync(TimeSpan.FromMinutes(2), stop.Token), "the loop waits to start");
        _site.CountRequests(FirstPage).Should().Be(0);
        await loopTime.AdvanceAndWaitAsync(TimeSpan.FromMinutes(2));
        service.Current.State.Should().Be(CatalogueState.Fresh);
        _site.CountRequests(FirstPage).Should().Be(1);

        await loopTime.WakeAsync(loop.RequestRefresh, "a requested refresh runs at once");
        _site.CountRequests(FirstPage).Should().Be(2);

        await loopTime.AdvanceAndWaitAsync(CatalogueRefreshLoop.Interval * 1.1);
        _site.CountRequests(FirstPage).Should().Be(3);

        await stop.CancelAsync();
        var stopped = async () => await running;
        await stopped.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task Tries_again_a_quarter_of_an_hour_after_a_failure()
    {
        _site.IsOffline = true;
        var (service, loop) = Create();
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var running = loop.RunAsync(TimeSpan.Zero, stop.Token);

        await Eventually.HoldsAsync(() => service.Current.State == CatalogueState.Failed, "the first refresh fails",
            () => _site.Time.Advance(TimeSpan.FromSeconds(1)));
        _site.IsOffline = false;
        var failedAt = _site.Time.GetUtcNow();
        await Eventually.HoldsAsync(() => service.Current.State == CatalogueState.Fresh, "the retry succeeds",
            () => _site.Time.Advance(TimeSpan.FromMinutes(1)));

        (_site.Time.GetUtcNow() - failedAt).Should().BeGreaterThanOrEqualTo(TimeSpan.FromMinutes(13));
        await stop.CancelAsync();
        var stopped = async () => await running;
        await stopped.Should().ThrowAsync<OperationCanceledException>();
    }

    public void Dispose()
    {
        _site.Dispose();
        _directory.Dispose();
    }

    private (CatalogueService Service, CatalogueRefreshLoop Loop) Create(TimeProvider? loopTime = null)
    {
        var cache = new CatalogueCache(_directory.Path, _site.Time, NullLogger<CatalogueCache>.Instance);
        var pages = new ProjectPageCache(_site.Api, cache, _site.Time, NullLogger<ProjectPageCache>.Instance);
        var service = new CatalogueService(new CatalogueCrawler(_site.Api, _site.Time), new CrawlIndexSource(pages, _site.Time), pages,
            cache, NullLogger<CatalogueService>.Instance);
        var loop = new CatalogueRefreshLoop(service, () => new IndexRequest([new GameKey(StoreId.Steam, "1000010")], []),
            loopTime ?? _site.Time, NullLogger<CatalogueRefreshLoop>.Instance);
        return (service, loop);
    }
}
