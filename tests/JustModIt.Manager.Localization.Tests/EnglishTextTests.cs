using JustModIt.Manager.Localization.Tests.TestSupport;

namespace JustModIt.Manager.Localization.Tests;

[Collection(nameof(TestTexts))]
public sealed class EnglishTextTests
{
    public EnglishTextTests() => Text.Use(TextCatalog.Base);

    [Theory]
    [InlineData(0, "0 games")]
    [InlineData(1, "1 game")]
    [InlineData(2, "2 games")]
    [InlineData(21, "21 games")]
    [InlineData(1234, "1,234 games")]
    public void Counts_pick_the_english_plural_form(long count, string expected)
    {
        Text.Format(Strings.GameCount, ("count", count)).Should().Be(expected);
    }

    [Fact]
    public void Comparer_orders_by_the_english_alphabet_with_numbers_by_value()
    {
        string[] titles = ["Zeta", "saga 10", "Éclair", "Saga 2", "eclair", "Alpha"];

        titles.Order(Text.Comparer).Should().Equal("Alpha", "eclair", "Éclair", "Saga 2", "saga 10", "Zeta");
    }
}
