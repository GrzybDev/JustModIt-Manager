using System.Globalization;
using JustModIt.Manager.Localization.Tests.TestSupport;

namespace JustModIt.Manager.Localization.Tests;

[Collection(nameof(TestTexts))]
public sealed class FormatsTests
{
    private static readonly TimeSpan Offset = FixedClock.Offset;
    private static readonly TimeZoneInfo Zone = FixedClock.Zone;

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

    [Theory]
    [InlineData(-5, "0\u00A0B")]
    [InlineData(0, "0\u00A0B")]
    [InlineData(512, "512\u00A0B")]
    [InlineData(1023, "1023\u00A0B")]
    [InlineData(1024, "1,0\u00A0KB")]
    [InlineData(1536, "1,5\u00A0KB")]
    [InlineData(102_297, "99,9\u00A0KB")]
    [InlineData(102_350, "100\u00A0KB")]
    [InlineData(245_760, "240\u00A0KB")]
    [InlineData(1_048_575, "1,0\u00A0MB")]
    [InlineData(1_572_864, "1,5\u00A0MB")]
    [InlineData(16_043_212, "15,3\u00A0MB")]
    [InlineData(5_368_709_120, "5,0\u00A0GB")]
    [InlineData(1_649_267_441_664, "1,5\u00A0TB")]
    [InlineData(long.MaxValue, "8\u00A0388\u00A0608\u00A0TB")]
    public void File_sizes_use_binary_units_and_a_decimal_comma(long bytes, string expected)
    {
        Formats.FileSize(bytes).Should().Be(expected);
    }

    [Theory]
    [InlineData(42, "42%")]
    [InlineData(-5, "0%")]
    [InlineData(150, "100%")]
    public void Percentages_are_clamped(int percent, string expected)
    {
        Formats.Percent(percent).Should().Be(expected);
    }

    [Theory]
    [InlineData(1, 3, "33%")]
    [InlineData(999, 1000, "99%")]
    [InlineData(1000, 1000, "100%")]
    [InlineData(2000, 1000, "100%")]
    [InlineData(5, 0, "0%")]
    [InlineData(-5, 10, "0%")]
    [InlineData(long.MaxValue - 1, long.MaxValue, "99%")]
    [InlineData(long.MaxValue, long.MaxValue, "100%")]
    public void Progress_percentages_round_down(long part, long whole, string expected)
    {
        Formats.Percent(part, whole).Should().Be(expected);
    }

    [Fact]
    public void Dates_are_shown_in_the_given_time_zone()
    {
        Formats.Date(new DateTimeOffset(2026, 9, 5, 10, 0, 0, TimeSpan.Zero), Zone).Should().Be("5.09.2026");
        Formats.Date(new DateTimeOffset(2026, 9, 5, 23, 30, 0, TimeSpan.Zero), Zone).Should().Be("6.09.2026");
        Formats.DateAndTime(new DateTimeOffset(2026, 9, 5, 23, 30, 0, TimeSpan.Zero), Zone).Should().Be("6.09.2026, 01:30");
    }

    [Fact]
    public void Calendar_dates_are_shown_as_they_are()
    {
        Formats.Date(new DateOnly(2026, 12, 31)).Should().Be("31.12.2026");
    }

    [Theory]
    [InlineData("2026-09-26T12:00:00+02:00", "przed chwilą")]
    [InlineData("2026-09-26T11:59:30+02:00", "przed chwilą")]
    [InlineData("2026-09-26T12:00:30+02:00", "przed chwilą")]
    [InlineData("2026-09-26T11:59:00+02:00", "1\u00A0min temu")]
    [InlineData("2026-09-26T11:55:00+02:00", "5\u00A0min temu")]
    [InlineData("2026-09-26T11:00:01+02:00", "59\u00A0min temu")]
    [InlineData("2026-09-26T11:00:00+02:00", "dziś o 11:00")]
    [InlineData("2026-09-26T00:00:00+02:00", "dziś o 00:00")]
    [InlineData("2026-09-26T14:00:00+02:00", "dziś o 14:00")]
    [InlineData("2026-09-25T23:59:00+02:00", "wczoraj o 23:59")]
    [InlineData("2026-09-25T00:00:00+02:00", "wczoraj o 00:00")]
    [InlineData("2026-09-24T23:59:00+02:00", "24.09.2026, 23:59")]
    [InlineData("2026-09-27T09:00:00+02:00", "27.09.2026, 09:00")]
    public void Relative_time_goes_from_moments_to_days_to_dates(string value, string expected)
    {
        var clock = new FixedClock(DateTimeOffset.Parse("2026-09-26T12:00:00+02:00", CultureInfo.InvariantCulture), Zone);

        Formats.RelativeTime(DateTimeOffset.Parse(value, CultureInfo.InvariantCulture), clock).Should().Be(expected);
    }

    [Fact]
    public void Relative_time_prefers_minutes_across_midnight()
    {
        var clock = new FixedClock(new DateTimeOffset(2026, 9, 26, 0, 10, 0, Offset), Zone);

        Formats.RelativeTime(new DateTimeOffset(2026, 9, 25, 23, 30, 0, Offset), clock).Should().Be("40\u00A0min temu");
        Formats.RelativeTime(new DateTimeOffset(2026, 9, 25, 22, 0, 0, Offset), clock).Should().Be("wczoraj o 22:00");
    }

    [Fact]
    public void Relative_time_counts_days_in_the_local_time_zone()
    {
        var now = new DateTimeOffset(2026, 9, 26, 10, 0, 0, TimeSpan.Zero);
        var lateEveningUtc = new DateTimeOffset(2026, 9, 25, 23, 30, 0, TimeSpan.Zero);

        Formats.RelativeTime(lateEveningUtc, new FixedClock(now, Zone)).Should().Be("dziś o 01:30");
        Formats.RelativeTime(lateEveningUtc, new FixedClock(now, TimeZoneInfo.Utc)).Should().Be("wczoraj o 23:30");
    }

    [Theory]
    [InlineData(-3, "0\u00A0s")]
    [InlineData(0, "0\u00A0s")]
    [InlineData(16, "16\u00A0s")]
    [InlineData(59.4, "59\u00A0s")]
    [InlineData(59.5, "1\u00A0min")]
    [InlineData(125, "2\u00A0min 5\u00A0s")]
    [InlineData(3600, "1\u00A0godz.")]
    [InlineData(3900, "1\u00A0godz. 5\u00A0min")]
    [InlineData(3959, "1\u00A0godz. 5\u00A0min")]
    [InlineData(90000, "25\u00A0godz.")]
    public void Durations_leave_out_empty_smaller_units(double seconds, string expected)
    {
        Formats.Duration(TimeSpan.FromSeconds(seconds)).Should().Be(expected);
    }

    [Theory]
    [InlineData(15.2, "zostało ok. 16\u00A0s")]
    [InlineData(0.3, "zostało ok. 1\u00A0s")]
    [InlineData(-1, "zostało ok. 0\u00A0s")]
    [InlineData(179.1, "zostało ok. 3\u00A0min")]
    public void Time_remaining_rounds_up(double seconds, string expected)
    {
        Formats.TimeRemaining(TimeSpan.FromSeconds(seconds)).Should().Be(expected);
    }

    [Fact]
    public void Time_remaining_accepts_the_longest_time_span()
    {
        var format = () => Formats.TimeRemaining(TimeSpan.MaxValue);

        format.Should().NotThrow();
    }

    [Fact]
    public void Formats_do_not_depend_on_the_thread_culture()
    {
        var (culture, uiCulture) = (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture);
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
        try
        {
            Formats.FileSize(1_572_864).Should().Be("1,5\u00A0MB");
            Formats.DateAndTime(new DateTimeOffset(2026, 9, 5, 12, 0, 0, TimeSpan.Zero), Zone).Should().Be("5.09.2026, 14:00");
            Formats.TimeRemaining(TimeSpan.FromSeconds(15.2)).Should().Be("zostało ok. 16\u00A0s");
        }
        finally
        {
            (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture) = (culture, uiCulture);
        }
    }
}
