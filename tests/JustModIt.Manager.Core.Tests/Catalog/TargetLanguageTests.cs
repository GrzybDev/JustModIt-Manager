using JustModIt.Manager.Core.Catalog;
using JustModIt.Manager.Core.Tests.Catalog.TestSupport;

namespace JustModIt.Manager.Core.Tests.Catalog;

public sealed class TargetLanguageTests
{
    [Theory]
    [InlineData("pl", "steam", """[0, 12]""", true)]
    [InlineData("pl", "steam", """[0, 2]""", false)]
    [InlineData("pl", "steam", """["english", "Polish"]""", true)]
    [InlineData("pl", "gog", """["en", "pl"]""", true)]
    [InlineData("pl", "gog", """["en", "PL-pl"]""", true)]
    [InlineData("pl", "gog", """[12]""", false)]
    [InlineData("pl", "", """[12, "pl"]""", false)]
    [InlineData("pl", "future-store", """["pl"]""", false)]
    [InlineData("pl", "steam", "\"12\"", false)]
    [InlineData("pl", "steam", "null", false)]
    [InlineData("cs", "gog", """["cz"]""", true)]
    [InlineData("ja", "gog", """["jp"]""", true)]
    [InlineData("ja", "steam", """[10]""", true)]
    [InlineData("pt", "gog", """["br"]""", true)]
    [InlineData("pt", "steam", """[22]""", true)]
    [InlineData("es", "gog", """["es_mx"]""", true)]
    [InlineData("es", "steam", """[27]""", true)]
    [InlineData("nb", "steam", """[16]""", true)]
    [InlineData("de", "gog", """["de"]""", true)]
    [InlineData("pt-BR", "steam", """[11]""", true)]
    [InlineData("pl", "epic", """["en", "pl"]""", true)]
    [InlineData("pl", "epic", """["en", "pl-PL"]""", true)]
    [InlineData("pl", "epic", """["en", "pt-BR"]""", false)]
    [InlineData("pl", "epic", """[12]""", false)]
    [InlineData("pt", "epic", """["pt-BR"]""", true)]
    [InlineData("zh", "epic", """["zh-Hant"]""", true)]
    [InlineData("no", "epic", """["nb"]""", true)]
    public void Knows_how_each_store_names_the_language(string code, string provider, string languages, bool offered)
    {
        var link = Link(provider, languages);

        TargetLanguage.FromCode(code).IsOfferedBy(link).Should().Be(offered);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Does_nothing_for_an_app_without_a_target_language(string? code)
    {
        var language = TargetLanguage.FromCode(code);

        language.Should().BeSameAs(TargetLanguage.None);
        language.IsSet.Should().BeFalse();
        language.IsOfferedBy(Link("steam", "[0, 12]")).Should().BeFalse();
        language.IsOfferedBy(Link("gog", """["en", "pl"]""")).Should().BeFalse();
    }

    [Theory]
    [InlineData("pl", "pl-PL", true)]
    [InlineData("pl", "pl", true)]
    [InlineData("PT", "pt-BR", true)]
    [InlineData("es", "es_MX", true)]
    [InlineData("no", "nb-NO", true)]
    [InlineData("pl", "p", false)]
    [InlineData("pl", "en", false)]
    [InlineData("", "pl", false)]
    public void Matches_a_game_language_in_any_region(string code, string tag, bool named) =>
        TargetLanguage.FromCode(code).IsNamedBy(tag).Should().Be(named);

    [Fact]
    public void Reads_the_language_from_a_regional_tag()
    {
        TargetLanguage.FromCode(" PT-br ").Code.Should().Be("pt");
        TargetLanguage.FromCode("nn").Code.Should().Be("no");
    }

    private static StorefrontLink Link(string provider, string languages) => new()
    {
        Uuid = Guid.NewGuid(),
        Platform = new StorefrontPlatform { Uuid = Guid.NewGuid(), Name = "Store", Provider = provider },
        AdditionalData = CatalogueFixtures.Element($$"""{"appid": 1, "gog_id": 1, "languages": {{languages}}}"""),
    };
}
