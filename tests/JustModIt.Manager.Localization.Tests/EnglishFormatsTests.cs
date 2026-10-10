using System.Globalization;
using JustModIt.Manager.Localization.Tests.TestSupport;

namespace JustModIt.Manager.Localization.Tests;

[Collection(nameof(TestTexts))]
public sealed class EnglishFormatsTests
{
    private static readonly TimeZoneInfo Zone = FixedClock.Zone;

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

    [Theory]
    [InlineData(512, "512\u00A0B")]
    [InlineData(1536, "1.5\u00A0KB")]
    [InlineData(102_350, "100.0\u00A0KB")]
    [InlineData(16_043_212, "15.3\u00A0MB")]
    [InlineData(long.MaxValue, "8,388,608.0\u00A0TB")]
    public void File_sizes_use_binary_units_and_a_decimal_point(long bytes, string expected)
    {
        Formats.FileSize(bytes).Should().Be(expected);
    }

    [Fact]
    public void Dates_name_the_month()
    {
        Formats.Date(new DateOnly(2026, 12, 31)).Should().Be("31 Dec 2026");
        Formats.DateAndTime(new DateTimeOffset(2026, 9, 5, 23, 30, 0, TimeSpan.Zero), Zone).Should().Be("6 Sep 2026, 01:30");
    }

    [Fact]
    public void Release_dates_ages_and_versions_read_in_english()
    {
        var clock = new FixedClock(DateTimeOffset.Parse("2026-09-26T12:00:00+02:00", CultureInfo.InvariantCulture), Zone);

        Formats.LongDate(new DateTimeOffset(2026, 9, 5, 12, 0, 0, TimeSpan.Zero), Zone).Should().Be("5 September 2026");
        Formats.CalendarAge(clock.GetUtcNow().AddDays(-1), clock).Should().Be("yesterday");
        Formats.CalendarAge(clock.GetUtcNow().AddDays(-40), clock).Should().Be("a month ago");
        Formats.CalendarAge(clock.GetUtcNow().AddYears(-3), clock).Should().Be("3 years ago");
        Formats.Version("Version 1.2").Should().Be("1.2");
        Formats.Version("Wersja 1.2").Should().Be("Wersja 1.2");
    }

    [Theory]
    [InlineData("2026-09-26T11:59:30+02:00", "just now")]
    [InlineData("2026-09-26T11:55:00+02:00", "5\u00A0min ago")]
    [InlineData("2026-09-26T09:05:00+02:00", "today at 09:05")]
    [InlineData("2026-09-25T23:59:00+02:00", "yesterday at 23:59")]
    [InlineData("2026-09-24T23:59:00+02:00", "24 Sep 2026, 23:59")]
    public void Relative_time_goes_from_moments_to_days_to_dates(string value, string expected)
    {
        var clock = new FixedClock(DateTimeOffset.Parse("2026-09-26T12:00:00+02:00", CultureInfo.InvariantCulture), Zone);

        Formats.RelativeTime(DateTimeOffset.Parse(value, CultureInfo.InvariantCulture), clock).Should().Be(expected);
    }

    [Theory]
    [InlineData(16, "16\u00A0s")]
    [InlineData(125, "2\u00A0min 5\u00A0s")]
    [InlineData(3600, "1\u00A0h")]
    [InlineData(3900, "1\u00A0h 5\u00A0min")]
    public void Durations_leave_out_empty_smaller_units(double seconds, string expected)
    {
        Formats.Duration(TimeSpan.FromSeconds(seconds)).Should().Be(expected);
    }

    [Fact]
    public void Time_remaining_rounds_up()
    {
        Formats.TimeRemaining(TimeSpan.FromSeconds(15.2)).Should().Be("about 16\u00A0s left");
    }
}
