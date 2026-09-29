using JustModIt.Manager.Core.Catalog;
using JustModIt.Manager.Core.Games;

namespace JustModIt.Manager.LiveTests;

// Read-only requests only: asking the site for a download address would count a download.
public sealed class CatalogueLiveTests : IDisposable
{
    private readonly CatalogueApiClient _api = new(
        new CatalogueApiOptions(new Uri(ProductConfiguration.ApiUrl), new Uri(ProductConfiguration.SiteUrl), ProductConfiguration.AppId,
            "0.0.0-live"),
        TimeProvider.System);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("2592160", true)]
    [InlineData("474960", true)]
    [InlineData("939850", true)]
    [InlineData("3994800", false)]
    public async Task Offers_what_the_site_lets_a_steam_game_install(string appId, bool installable)
    {
        LiveTest.SkipUnlessEnabled();
        var game = new GameKey(StoreId.Steam, appId);
        var listing = await new CatalogueCrawler(_api, TimeProvider.System).CrawlAsync(Token);

        var index = await new CrawlIndexSource(new ApiProjectPageSource(_api), TimeProvider.System)
            .BuildAsync(new IndexScope([game], [], listing), Token);
        var offer = GameOffer.For(index, game, OperatingSystemKind.Windows, TargetLanguage.FromCode(ProductConfiguration.TargetLanguage));

        offer.Projects.Should().NotBeEmpty("the site has a project for the game");
        offer.Installable.Should().HaveCount(installable ? 1 : 0);
        if (installable)
        {
            offer.Installable[0].Selection.Default!.Package!.Format.Should().NotBeNull();
        }
    }

    [Fact]
    public async Task Reads_the_whole_catalogue_and_what_the_site_says_about_itself()
    {
        LiveTest.SkipUnlessEnabled();

        var listing = await new CatalogueCrawler(_api, TimeProvider.System).CrawlAsync(Token);

        listing.Games.Should().NotBeEmpty();
        listing.Games.Should().OnlyContain(listed => listed.Projects.Count > 0);
        listing.Reference.Statuses.Should().NotBeEmpty();
        listing.Reference.StorefrontPlatforms.Should().Contain(platform => platform.Store == StoreId.Steam);
        listing.Reference.Site.Name.Should().NotBeEmpty();
    }

    public void Dispose() => _api.Dispose();
}
