using JustModIt.Manager.Localization.Tests.TestSupport;

namespace JustModIt.Manager.Localization.Tests;

[Collection(nameof(TestTexts))]
public sealed class EnglishFormatsTests
{
    public EnglishFormatsTests() => Text.Use(TextCatalog.Base);

    [Theory]
    [InlineData(0, "0")]
    [InlineData(999, "999")]
    [InlineData(1234, "1,234")]
    [InlineData(-12345, "-12,345")]
    [InlineData(1234567.891, "1,234,567.89")]
    [InlineData(1.5, "1.5")]
    [InlineData(0.125, "0.13")]
    [InlineData(-0.001, "0")]
    public void Numbers_group_digits_from_four_on_with_a_decimal_point(double value, string expected)
    {
        Formats.Number(value).Should().Be(expected);
    }
}
