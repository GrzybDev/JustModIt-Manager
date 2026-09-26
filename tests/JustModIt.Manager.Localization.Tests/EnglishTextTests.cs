using System.Globalization;
using Jeffijoe.MessageFormat;
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
    public void Numbers_do_not_depend_on_the_thread_culture()
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("pl-PL");
        try
        {
            Text.Format(Strings.GameCount, ("count", 1)).Should().Be("1 game");
            Text.Format(Strings.GameCount, ("count", 1.5)).Should().Be("1.5 games");
            Text.Format("{done} of {total, number}", ("done", 1.5), ("total", 12345)).Should().Be("1.5 of 12,345");
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void Plural_forms_match_the_library_rules_for_whole_numbers()
    {
        var library = new MessageFormatter(useCache: false, Text.Culture);

        for (var count = 0; count < 1000; count++)
        {
            var arguments = new Dictionary<string, object?> { ["count"] = count };
            Text.Format(Strings.GameCount, ("count", count)).Should().Be(library.FormatMessage(Strings.GameCount, arguments));
        }
    }

    [Fact]
    public void Comparer_orders_by_the_english_alphabet_with_numbers_by_value()
    {
        string[] titles = ["Zeta", "saga 10", "Éclair", "Saga 2", "eclair", "Alpha"];

        titles.Order(Text.Comparer).Should().Equal("Alpha", "eclair", "Éclair", "Saga 2", "saga 10", "Zeta");
    }
}
