using JustModIt.Manager.Protocol.Activation;
using JustModIt.Manager.Protocol.Messages;

namespace JustModIt.Manager.Protocol.Tests.Activation;

public sealed class AppLinksTests
{
    private readonly AppLinks _links = new("example-app");

    public static TheoryData<string, NavigationTarget> Destinations => new()
    {
        { "example-app://library", new LibraryTarget() },
        { "EXAMPLE-APP://LIBRARY/", new LibraryTarget() },
        { "example-app://game/steam/480", new GameTarget("steam", "480") },
        { "example-app://game/gog/1000000001/?from=notification#top", new GameTarget("gog", "1000000001") },
        { "example-app://game/example%20store/id%3D7", new GameTarget("example store", "id=7") },
        { "example-app://catalogue", new CatalogueTarget() },
        { "example-app://catalogue/example-game", new CatalogueTarget("example-game") },
        { "example-app://catalogue/example-%C4%85%C4%87%C5%BA", new CatalogueTarget("example-ąćź") },
        { "example-app://catalogue/example-ąćź", new CatalogueTarget("example-ąćź") },
        { "example-app://catalogue/example-game/example-team/example-project", new CatalogueTarget("example-game", "example-team", "example-project") },
        { "example-app://catalogue/example-game/example-team/example-project/", new CatalogueTarget("example-game", "example-team", "example-project") },
        { "example-app://settings", new SettingsTarget() },
        { "example-app://creator", new CreatorTarget() },
    };

    [Theory]
    [MemberData(nameof(Destinations))]
    public void Link_leads_to_its_destination(string link, NavigationTarget expected)
    {
        _links.Parse(link).Should().Be(expected);
    }

    [Theory]
    [InlineData("example-app://game")]
    [InlineData("example-app://game/steam")]
    [InlineData("example-app://game/steam/480/extra")]
    [InlineData("example-app://game/steam/%2E%2E")]
    [InlineData("example-app://game/steam/a%2Fb")]
    [InlineData("example-app://game/steam/a%5Cb")]
    [InlineData("example-app://game/steam/%20%20")]
    [InlineData("example-app://game/steam/a%00b")]
    [InlineData("example-app://game/steam/a%0Ab")]
    public void Broken_game_link_falls_back_to_the_library(string link)
    {
        _links.Parse(link).Should().Be(new LibraryTarget());
    }

    [Theory]
    [InlineData("example-app://catalogue/%2E%2E")]
    [InlineData("example-app://catalogue/a%2Fb")]
    [InlineData("example-app://catalogue/%20%20/example-team/example-project")]
    public void Broken_catalogue_link_opens_the_catalogue(string link)
    {
        _links.Parse(link).Should().Be(new CatalogueTarget());
    }

    [Theory]
    [InlineData("example-app://catalogue/example-game/example-team")]
    [InlineData("example-app://catalogue/example-game/example-team/example-project/extra")]
    [InlineData("example-app://catalogue/example-game/a%00b/example-project")]
    [InlineData("example-app://catalogue/example-game/example-team/a%5Cb")]
    public void Broken_project_link_opens_its_game(string link)
    {
        _links.Parse(link).Should().Be(new CatalogueTarget("example-game"));
    }

    [Fact]
    public void Overlong_segment_is_not_a_destination()
    {
        _links.Parse($"example-app://catalogue/{new string('a', 257)}").Should().Be(new CatalogueTarget());
    }

    [Theory]
    [InlineData("example-app://open")]
    [InlineData("example-app://install/steam/480")]
    [InlineData("example-app://uninstall/steam/480")]
    [InlineData("example-app://exit")]
    [InlineData("example-app:library")]
    [InlineData("example-app://")]
    [InlineData("example-app")]
    [InlineData("other-app://library")]
    [InlineData("https://example.com/library")]
    [InlineData("")]
    public void Anything_else_only_opens_the_window(string link)
    {
        _links.Parse(link).Should().BeNull();
    }

    [Fact]
    public void Overlong_link_only_opens_the_window()
    {
        _links.Parse($"example-app://catalogue/{new string('a', 3000)}").Should().BeNull();
    }

    [Theory]
    [InlineData("example-app://library", true)]
    [InlineData("Example-App:anything", true)]
    [InlineData("example-application://library", false)]
    [InlineData("--background", false)]
    [InlineData("", false)]
    public void Only_links_of_its_own_scheme_are_links(string argument, bool isLink)
    {
        _links.IsLink(argument).Should().Be(isLink);
    }

    [Fact]
    public void Open_link_is_a_link_that_leads_nowhere()
    {
        _links.Open.Should().Be("example-app://open");
        _links.IsLink(_links.Open).Should().BeTrue();
        _links.Parse(_links.Open).Should().BeNull();
    }

    public static TheoryData<NavigationTarget> Targets =>
    [
        new LibraryTarget(),
        new GameTarget("steam", "480"),
        new GameTarget("example store", "id=7&x?#"),
        new CatalogueTarget(),
        new CatalogueTarget("example-ąćź"),
        new CatalogueTarget("example game", "team & co", "project?"),
        new SettingsTarget(),
        new CreatorTarget(),
    ];

    [Theory]
    [MemberData(nameof(Targets))]
    public void Built_link_leads_back_to_its_target(NavigationTarget target)
    {
        var link = _links.For(target);

        _links.IsLink(link).Should().BeTrue();
        _links.Parse(link).Should().Be(target);
    }

    [Fact]
    public void Game_link_escapes_its_segments()
    {
        _links.For(new GameTarget("gog", "1000000001")).Should().Be("example-app://game/gog/1000000001");
        _links.For(new CatalogueTarget("a b")).Should().Be("example-app://catalogue/a%20b");
    }

    [Theory]
    [InlineData("")]
    [InlineData("1app")]
    [InlineData("example app")]
    [InlineData("example-app://")]
    public void Scheme_must_be_a_valid_uri_scheme(string scheme)
    {
        var create = () => new AppLinks(scheme);

        create.Should().Throw<ArgumentException>();
    }
}
