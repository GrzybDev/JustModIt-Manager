using System.Globalization;
using Jeffijoe.MessageFormat;
using Jeffijoe.MessageFormat.Formatting;
using Jeffijoe.MessageFormat.Parsing;
using JustModIt.Manager.Localization.Tests.TestSupport;

namespace JustModIt.Manager.Localization.Tests;

[Collection(nameof(TestTexts))]
public sealed class TextTests
{
    public TextTests() => Text.Use(TestTexts.Polish);

    [Theory]
    [InlineData(0, "0 tytułów")]
    [InlineData(1, "1 tytuł")]
    [InlineData(2, "2 tytuły")]
    [InlineData(4, "4 tytuły")]
    [InlineData(5, "5 tytułów")]
    [InlineData(11, "11 tytułów")]
    [InlineData(12, "12 tytułów")]
    [InlineData(14, "14 tytułów")]
    [InlineData(21, "21 tytułów")]
    [InlineData(22, "22 tytuły")]
    [InlineData(25, "25 tytułów")]
    [InlineData(101, "101 tytułów")]
    [InlineData(102, "102 tytuły")]
    [InlineData(112, "112 tytułów")]
    [InlineData(122, "122 tytuły")]
    [InlineData(1234, "1\u00A0234 tytuły")]
    [InlineData(12345, "12\u00A0345 tytułów")]
    public void Counts_pick_the_polish_plural_form(long count, string expected)
    {
        Text.Format(Strings.GameCount, ("count", count)).Should().Be(expected);
    }

    [Fact]
    public void Fractions_use_the_other_form()
    {
        Text.Format(Strings.GameCount, ("count", 1.5)).Should().Be("1,5 tytułu");
    }

    [Fact]
    public void Numbers_do_not_depend_on_the_thread_culture()
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
        try
        {
            Text.Format(Strings.GameCount, ("count", 1.5)).Should().Be("1,5 tytułu");
            Text.Format(Strings.GameCount, ("count", 12345)).Should().Be("12\u00A0345 tytułów");
            Text.Format("{done} of {total, number}", ("done", 1.5), ("total", 12345)).Should().Be("1,5 of 12\u00A0345");
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void Arguments_print_numbers_as_the_language_writes_them_and_other_values_as_passed()
    {
        var text = Text.Format(
            "{files} files, {bytes, number} bytes in {folder}, {share, number, percent}",
            ("files", 1234),
            ("bytes", 12345L),
            ("folder", "Data"),
            ("share", 0.5));

        text.Should().Be("1\u00A0234 files, 12\u00A0345 bytes in Data, 50%");
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
    public void Plural_forms_reach_the_pattern_cache_once_per_form_not_per_number()
    {
        var plural = new LocalizedPluralFormatter();
        var request = new FormatterRequest(
            new Literal(0, 0, 0, 0, string.Empty), "count", "plural", "one {# gra} few {# gry} many {# gier} other {# gry}");
        var nested = new RecordingFormatter();

        for (var count = 0; count < 100; count++)
        {
            plural.Format(Text.Culture, request, new Dictionary<string, object?> { ["count"] = count }, count, nested);
        }

        nested.Patterns.Should().HaveCount(3, "0 to 99 use only the one, few and many forms");
    }

    [Fact]
    public void Named_arguments_fill_the_pattern()
    {
        var text = Text.Format("{name} has {count, plural, one {# file} other {# files}}", ("name", "Sample"), ("count", 3));

        text.Should().Be("Sample has 3 files");
    }

    [Theory]
    [InlineData(0, "none")]
    [InlineData(1, "just one")]
    [InlineData(7, "7 more")]
    public void Exact_values_win_over_plural_forms(int count, string expected)
    {
        var text = Text.Format("{count, plural, =0 {none} =1 {just one} other {# more}}", ("count", count));

        text.Should().Be(expected);
    }

    [Theory]
    [InlineData(1, "only the author")]
    [InlineData(2, "the author and one other")]
    [InlineData(6, "the author and 5 others")]
    public void Offset_shifts_the_number_but_not_exact_values(int count, string expected)
    {
        var pattern = "{count, plural, offset:1 =1 {only the author} one {the author and one other} other {the author and # others}}";

        Text.Format(pattern, ("count", count)).Should().Be(expected);
    }

    [Fact]
    public void Quoted_number_signs_and_apostrophes_stay_literal()
    {
        var text = Text.Format("{count, plural, other {'#'{count} is # and it''s '{'#'}'}}", ("count", 12345));

        text.Should().Be("#12\u00A0345 is 12\u00A0345 and it's {12\u00A0345}");
    }

    [Fact]
    public void Nested_arguments_are_formatted_inside_a_plural_form()
    {
        var pattern = "{count, plural, other {# {kind, select, file {files} other {items}} in {folder}}}";

        Text.Format(pattern, ("count", 3), ("kind", "file"), ("folder", "Data")).Should().Be("3 files in Data");
    }

    [Fact]
    public void Nested_plural_numbers_its_own_forms()
    {
        var pattern = "{files, plural, other {# files in {folders, plural, one {# folder} other {# folders}}}}";

        Text.Format(pattern, ("files", 12345), ("folders", 1)).Should().Be("12\u00A0345 files in 1 folder");
    }

    [Fact]
    public void Missing_argument_is_an_error()
    {
        var format = () => Text.Format(Strings.GameCount);

        format.Should().Throw<MessageFormatterException>();
    }

    [Fact]
    public void Pattern_without_other_form_is_an_error()
    {
        var format = () => Text.Format("{count, plural, one {# file}}", ("count", 5));

        format.Should().Throw<MessageFormatterException>();
    }

    [Fact]
    public void Formatting_is_safe_from_many_threads()
    {
        var library = new MessageFormatter(useCache: false, Text.Culture);
        // The library groups no digits, so the counts stay below the first group.
        var counts = Enumerable.Range(0, 10_000).Select(count => count % 1000).ToArray();
        var texts = new string[counts.Length];

        Parallel.For(0, counts.Length, i => texts[i] = Text.Format(Strings.GameCount, ("count", counts[i])));

        texts.Should().Equal(counts.Select(count => library.FormatMessage(Strings.GameCount, new Dictionary<string, object?> { ["count"] = count })));
    }

    [Fact]
    public void Comparer_orders_polish_letters_after_their_base_letters()
    {
        string[] titles = ["Żuraw", "Zamek", "Łąka", "Las", "Śnieżyca", "Sad", "Tor", "ćma", "Cisza", "Ąb", "Ab"];

        titles.Order(Text.Comparer).Should().Equal("Ab", "Ąb", "Cisza", "ćma", "Las", "Łąka", "Sad", "Śnieżyca", "Tor", "Zamek", "Żuraw");
    }

    [Fact]
    public void Comparer_orders_numbers_by_value_and_ignores_case()
    {
        string[] titles = ["Saga 10", "saga 2", "Saga 1"];

        titles.Order(Text.Comparer).Should().Equal("Saga 1", "saga 2", "Saga 10");
        Text.Comparer.Equals("ŁĄKA", "łąka").Should().BeTrue();
    }

    private sealed class RecordingFormatter : IMessageFormatter
    {
        public HashSet<string> Patterns { get; } = new(StringComparer.Ordinal);

        public CustomValueFormatter? CustomValueFormatter => null;

        public string FormatMessage(string pattern, IReadOnlyDictionary<string, object?> args, CultureInfo? culture = null)
        {
            Patterns.Add(pattern);
            return pattern;
        }
    }
}
