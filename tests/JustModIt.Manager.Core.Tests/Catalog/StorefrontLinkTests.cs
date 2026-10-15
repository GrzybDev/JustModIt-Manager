using JustModIt.Manager.Core.Catalog;
using JustModIt.Manager.Core.Tests.Catalog.TestSupport;

namespace JustModIt.Manager.Core.Tests.Catalog;

public sealed class StorefrontLinkTests
{
    [Theory]
    [InlineData("steam", """{"appid": 620}""", "steam:620")]
    [InlineData("steam", """{"appid": "620"}""", "steam:620")]
    [InlineData("steam", """{"appid": "0620"}""", "steam:620")]
    [InlineData("gog", """{"gog_id": 18446744073709551615}""", "gog:18446744073709551615")]
    [InlineData("gog", """{"gog_id": "1207658924"}""", "gog:1207658924")]
    [InlineData("steam", """{"appid": 0}""", null)]
    [InlineData("steam", """{"appid": -5}""", null)]
    [InlineData("steam", """{"appid": 6.5}""", null)]
    [InlineData("steam", """{"appid": "abc"}""", null)]
    [InlineData("steam", """{"gog_id": 620}""", null)]
    [InlineData("gog", """{"appid": 620}""", null)]
    [InlineData("", """{"appid": 620}""", null)]
    [InlineData("", "{}", null)]
    [InlineData("future-store", """{"id": 620}""", null)]
    [InlineData("steam", "[]", null)]
    [InlineData("epic", """{"namespace": "5b1f0c2e9d4a47c8b3e6a0f1d2c3b4a5"}""", "epic:5b1f0c2e9d4a47c8b3e6a0f1d2c3b4a5")]
    [InlineData("epic", """{"namespace": "lantern"}""", "epic:lantern")]
    [InlineData("epic", """{"namespace": "Lantern_isle-2"}""", "epic:Lantern_isle-2")]
    [InlineData("epic", """{"namespace": 620}""", null)]
    [InlineData("epic", """{"namespace": ""}""", null)]
    [InlineData("epic", """{"namespace": "-lantern"}""", null)]
    [InlineData("epic", """{"namespace": "lantern isle"}""", null)]
    [InlineData("epic", """{"namespace": "a1234567890123456789012345678901234567890123456789012345678901234"}""", null)]
    [InlineData("epic", """{"appid": 620}""", null)]
    public void Reads_the_store_id_leniently(string provider, string data, string? expected)
    {
        var link = Link(provider, data);

        link.GameKey?.ToString().Should().Be(expected);
        if (expected is null)
        {
            link.GameKey.Should().BeNull();
        }
    }

    [Fact]
    public void Reads_languages_and_the_build_in_the_stores_own_terms()
    {
        var steam = Link("steam", """{"appid": 1, "languages": [0, 12, "x", null], "buildid": "900"}""");
        var gog = Link("gog", """{"gog_id": 1, "languages": ["en", "pl"], "buildid": 901}""");
        var epic = Link("epic", """{"namespace": "lantern", "languages": ["en", "pl", "pt-BR"]}""");
        var none = Link("steam", """{"appid": 1, "languages": "english"}""");

        steam.Languages.Should().Equal("0", "12", "x");
        steam.BuildId.Should().Be("900");
        gog.Languages.Should().Equal("en", "pl");
        gog.BuildId.Should().Be("901");
        epic.Languages.Should().Equal("en", "pl", "pt-BR");
        epic.BuildId.Should().BeEmpty();
        none.Languages.Should().BeEmpty();
        none.BuildId.Should().BeEmpty();
    }

    private static StorefrontLink Link(string provider, string data) => new()
    {
        Uuid = Guid.NewGuid(),
        Platform = new StorefrontPlatform { Uuid = Guid.NewGuid(), Name = "Store", Provider = provider },
        AdditionalData = CatalogueFixtures.Element(data),
    };
}
