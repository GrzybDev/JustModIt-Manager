using JustModIt.Manager.Core.Catalog;
using Microsoft.Extensions.Time.Testing;

namespace JustModIt.Manager.Core.Tests.Catalog.TestSupport;

/// <summary>The invented site behind a real API client, with routes a test may change or remove.</summary>
internal sealed class FakeSite : IDisposable
{
    public FakeSite(FakeTimeProvider? time = null)
    {
        Time = time ?? new FakeTimeProvider(new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero));
        Routes = CatalogueFixtures.Routes();
        Handler = FakeHttpHandler.Routes(Routes);
        Api = new CatalogueApiClient(CatalogueFixtures.Options(), Time, Handler);
    }

    public FakeTimeProvider Time { get; }

    public Dictionary<string, Func<HttpResponseMessage>> Routes { get; }

    public FakeHttpHandler Handler { get; }

    public CatalogueApiClient Api { get; }

    public Task<CatalogueListing> CrawlAsync() => new CatalogueCrawler(Api, Time).CrawlAsync(TestContext.Current.CancellationToken);

    public void Dispose() => Api.Dispose();
}
