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
    [InlineData("future-store", """{"id": 620}""", null)]
    [InlineData("steam", "[]", null)]
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
        var none = Link("steam", """{"appid": 1, "languages": "english"}""");

        steam.Languages.Should().Equal("0", "12", "x");
        steam.BuildId.Should().Be("900");
        gog.Languages.Should().Equal("en", "pl");
        gog.BuildId.Should().Be("901");
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
