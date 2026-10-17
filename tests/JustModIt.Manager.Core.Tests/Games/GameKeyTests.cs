using JustModIt.Manager.Core.Games;

namespace JustModIt.Manager.Core.Tests.Games;

public sealed class GameKeyTests
{
    [Fact]
    public void A_store_id_is_lower_case() => new StoreId("GOG").Should().Be(StoreId.Gog);

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("steam:x")]
    [InlineData("epic games")]
    public void A_store_id_is_letters_digits_and_hyphens(string text)
    {
        StoreId.TryParse(text, out _).Should().BeFalse();
        var act = () => new StoreId(text);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void A_key_round_trips_through_its_text()
    {
        var key = new GameKey(StoreId.Gog, "54321098765432109");

        key.ToString().Should().Be("gog:54321098765432109");
        GameKey.TryParse(key.ToString(), out var parsed).Should().BeTrue();
        parsed.Should().Be(key);
    }

    [Fact]
    public void Only_the_first_separator_splits_a_key()
    {
        GameKey.TryParse("heroic-epic:namespace:item", out var key).Should().BeTrue();

        key.Store.Should().Be(new StoreId("heroic-epic"));
        key.StoreGameId.Should().Be("namespace:item");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("steam")]
    [InlineData("steam:")]
    [InlineData(":1000")]
    [InlineData("steam: ")]
    public void Malformed_keys_are_rejected(string? text) => GameKey.TryParse(text, out _).Should().BeFalse();
}
