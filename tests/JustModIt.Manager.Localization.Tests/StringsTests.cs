using System.Resources;
using System.Text;
using Jeffijoe.MessageFormat.Parsing;
using JustModIt.Manager.Localization.Tests.TestSupport;

namespace JustModIt.Manager.Localization.Tests;

[Collection(nameof(TestTexts))]
public sealed class StringsTests
{
    private const string OverriddenGameCount = "{count, plural, one {# tytuł} few {# tytuły} many {# tytułów} other {# tytułu}}";

    [Fact]
    public void Strings_read_the_catalog_in_use()
    {
        Text.Use(TextCatalog.Base);
        Strings.GameCount.Should().Be("{count, plural, one {# game} other {# games}}");

        Text.Use(TestTexts.Polish);
        Strings.GameCount.Should().Be(OverriddenGameCount);
    }

    [Fact]
    public void The_build_puts_the_app_overrides_over_the_translation_and_the_translation_over_the_base()
    {
        var translation = TestTexts.Translations().Single(translation => translation.Language == "pl").Texts;

        TestTexts.Polish.Culture.Name.Should().Be("pl-PL");
        TestTexts.Polish.Texts.Keys.Should().BeEquivalentTo(TextCatalog.Base.Texts.Keys);
        TestTexts.Polish.Texts.Should().AllSatisfy(text =>
            text.Value.Should().Be(text.Key == nameof(Strings.GameCount) ? OverriddenGameCount : translation[text.Key]));
        translation[nameof(Strings.GameCount)].Should().NotBe(OverriddenGameCount);
    }

    [Fact]
    public void A_text_missing_from_a_catalog_falls_back_to_the_english_base()
    {
        Text.Use(new TextCatalog("pl", []));

        Strings.GameCount.Should().Be(TextCatalog.Base[nameof(Strings.GameCount)]);
    }

    [Fact]
    public void An_empty_language_is_english()
    {
        new TextCatalog(string.Empty, []).Culture.Name.Should().Be("en");
        TextCatalog.Base.Culture.Name.Should().Be("en");
    }

    [Fact]
    public void An_assembly_without_merged_texts_has_no_catalog()
    {
        var load = () => TextCatalog.Load("en", typeof(TextCatalog).Assembly);

        load.Should().Throw<MissingManifestResourceException>();
    }

    [Fact]
    public void Every_translation_has_exactly_the_texts_of_the_base()
    {
        TestTexts.Translations().Should().NotBeEmpty().And.AllSatisfy(translation =>
            translation.Texts.Keys.Should().BeEquivalentTo(TextCatalog.Base.Texts.Keys, $"{translation.Language} translates every text"));
    }

    [Fact]
    public void Every_text_of_every_language_has_text()
    {
        AllTexts().Should().NotBeEmpty().And.AllSatisfy(text => text.Value.Should().NotBeNullOrWhiteSpace(text.Name));
    }

    [Fact]
    public void Every_text_of_every_language_is_a_well_formed_message()
    {
        var parser = new PatternParser();

        AllTexts().Should().AllSatisfy(text =>
        {
            var parse = () => parser.Parse(new StringBuilder(text.Value));
            parse.Should().NotThrow($"{text.Name} is formatted as an ICU message");
        });
    }

    private static IEnumerable<(string Name, string Value)> AllTexts() =>
        TestTexts.Translations().Select(translation => translation.Texts)
            .Concat(TestTexts.AppOverrides().Select(app => app.Texts))
            .Append(TextCatalog.Base.Texts)
            .Append(TestTexts.Polish.Texts)
            .SelectMany(texts => texts.Select(text => (text.Key, text.Value)));
}
