using JustModIt.Manager.Core.Catalog;
using JustModIt.Manager.Core.Games;

namespace JustModIt.Manager.LiveTests;

// Read-only requests only: asking the site for a download address would count a download.
public sealed class CatalogueLiveTests : IDisposable
{
    private readonly CatalogueApiClient _api = new(LiveApp.ApiOptions, TimeProvider.System);

    [Fact]
    public async Task Reads_the_whole_catalogue_and_what_the_site_says_about_itself()
    {
        LiveTest.SkipUnlessEnabled();

        var listing = await new CatalogueCrawler(_api, TimeProvider.System).CrawlAsync(LiveTest.Token);

        listing.Games.Should().NotBeEmpty();
        listing.Games.Should().OnlyContain(listed => listed.Projects.Count > 0);
        listing.Reference.Statuses.Should().NotBeEmpty();
        listing.Reference.StorefrontPlatforms.Should().Contain(platform => platform.Store == StoreId.Steam);
        listing.Reference.Site.Name.Should().NotBeEmpty();
    }

    public void Dispose() => _api.Dispose();
}
