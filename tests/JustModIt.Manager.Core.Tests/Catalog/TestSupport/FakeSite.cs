using System.Net;
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
        Handler = new FakeHttpHandler(request => IsOffline
            ? throw new HttpRequestException(HttpRequestError.NameResolutionError, "No such host is known.")
            : Routes.TryGetValue(request.RequestUri!.PathAndQuery, out var route)
                ? route()
                : FakeHttpHandler.Text(HttpStatusCode.NotFound, "<h1>Not Found</h1>", "text/html"));
        Api = new CatalogueApiClient(CatalogueFixtures.Options(), Time, Handler);
    }

    public FakeTimeProvider Time { get; }

    public Dictionary<string, Func<HttpResponseMessage>> Routes { get; }

    public FakeHttpHandler Handler { get; }

    public CatalogueApiClient Api { get; }

    /// <summary>Every request fails as if the machine had no network.</summary>
    public bool IsOffline { get; set; }

    public Task<CatalogueListing> CrawlAsync() => new CatalogueCrawler(Api, Time).CrawlAsync(TestContext.Current.CancellationToken);

    public int CountRequests(string pathAndQuery) => Handler.Paths.Count(path => path == pathAndQuery);

    public void Dispose() => Api.Dispose();
}
