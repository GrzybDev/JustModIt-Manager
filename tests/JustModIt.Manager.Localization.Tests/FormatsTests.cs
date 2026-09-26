using JustModIt.Manager.Localization.Tests.TestSupport;

namespace JustModIt.Manager.Localization.Tests;

[Collection(nameof(TestTexts))]
public sealed class FormatsTests
{
    public FormatsTests() => Text.Use(TestTexts.Polish);

    [Theory]
    [InlineData(0, "0")]
    [InlineData(1234, "1234")]
    [InlineData(9999, "9999")]
    [InlineData(10000, "10\u00A0000")]
    [InlineData(-12345, "-12\u00A0345")]
    [InlineData(1234567.891, "1\u00A0234\u00A0567,89")]
    [InlineData(1.5, "1,5")]
    [InlineData(0.125, "0,13")]
    [InlineData(-0.001, "0")]
    public void Numbers_group_digits_from_five_on_with_a_decimal_comma(double value, string expected)
    {
        Formats.Number(value).Should().Be(expected);
    }
}
