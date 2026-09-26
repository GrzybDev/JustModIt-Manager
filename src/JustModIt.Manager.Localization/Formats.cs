namespace JustModIt.Manager.Localization;

/// <summary>Numbers, sizes, dates and durations as the app's language writes them.</summary>
public static class Formats
{
    private static readonly TimeSpan OneMinute = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan OneHour = TimeSpan.FromHours(1);

    /// <summary>At most two decimals, with digits grouped only from where the language groups them.</summary>
    public static string Number(double value)
    {
        var rounded = Math.Round(value, 2, MidpointRounding.AwayFromZero);
        if (rounded == 0)
        {
            // A tiny negative value rounds to -0, which .NET prints with its sign.
            rounded = 0;
        }

        return rounded.ToString(Math.Abs(rounded) < Text.SmallestGroupedNumber ? "0.##" : "#,0.##", Text.Culture);
    }

    /// <summary>Binary units, as Windows Explorer and the website count them.</summary>
    /// <param name="bytes">The size; a negative one prints as 0 bytes.</param>
    public static string FileSize(long bytes)
    {
        if (bytes < 1024)
        {
            return Text.Format(Strings.SizeBytes, ("size", Math.Max(bytes, 0)));
        }

        var unit = 0;
        var value = (double)bytes;
        double shown;
        do
        {
            value /= 1024;
            unit++;
            shown = RoundSize(value);
        }
        while (shown >= 1024 && unit < 4);

        var pattern = unit switch
        {
            1 => Strings.SizeKilobytes,
            2 => Strings.SizeMegabytes,
            3 => Strings.SizeGigabytes,
            _ => Strings.SizeTerabytes,
        };
        var size = shown < 100 ? shown.ToString("0.0", Text.Culture) : Number(shown);
        return Text.Format(pattern, ("size", size));
    }

    /// <summary>A whole percentage.</summary>
    /// <param name="percent">Clamped to 0-100.</param>
    public static string Percent(int percent) => Text.Format(Strings.Percent, ("percent", Math.Clamp(percent, 0, 100)));

    /// <summary>
    /// The share of a whole, rounded down and capped at 99% until the end, so a job never shows 100% before it has
    /// finished.
    /// </summary>
    /// <param name="part">What is done, e.g. bytes.</param>
    /// <param name="whole">What there is to do; 0 or less gives 0%.</param>
    public static string Percent(long part, long whole)
    {
        if (whole <= 0)
        {
            return Percent(0);
        }

        return Percent(part >= whole ? 100 : (int)Math.Clamp(Math.Floor(100.0 * part / whole), 0, 99));
    }

    /// <summary>A date in the language's short pattern.</summary>
    /// <param name="date">A calendar date, printed as it is.</param>
    public static string Date(DateOnly date) => date.ToString(Strings.DatePattern, Text.Culture);

    /// <summary>The date of a moment in the language's short pattern.</summary>
    /// <param name="value">The moment.</param>
    /// <param name="zone">The time zone whose date it is.</param>
    public static string Date(DateTimeOffset value, TimeZoneInfo zone) => LocalDate(TimeZoneInfo.ConvertTime(value, zone));

    /// <summary>The date and time of a moment.</summary>
    /// <param name="value">The moment.</param>
    /// <param name="zone">The time zone whose date and time it is.</param>
    public static string DateAndTime(DateTimeOffset value, TimeZoneInfo zone) =>
        LocalDateAndTime(TimeZoneInfo.ConvertTime(value, zone));

    /// <summary>Just now, minutes ago, today or yesterday at a time, then the date and time, in the clock's time zone.</summary>
    /// <param name="value">The moment.</param>
    /// <param name="clock">Gives the time now and the local time zone.</param>
    public static string RelativeTime(DateTimeOffset value, TimeProvider clock)
    {
        var zone = clock.LocalTimeZone;
        var now = TimeZoneInfo.ConvertTime(clock.GetUtcNow(), zone);
        var then = TimeZoneInfo.ConvertTime(value, zone);
        var elapsed = now - then;

        // A moment slightly ahead of this machine's clock is another clock running fast, not a future event.
        if (elapsed.Duration() < OneMinute)
        {
            return Strings.TimeJustNow;
        }

        if (elapsed > TimeSpan.Zero && elapsed < OneHour)
        {
            return Text.Format(Strings.TimeMinutesAgo, ("minutes", (int)elapsed.TotalMinutes));
        }

        if (then.Date == now.Date)
        {
            return Text.Format(Strings.TimeToday, ("time", LocalTime(then)));
        }

        return then.Date == now.Date.AddDays(-1)
            ? Text.Format(Strings.TimeYesterday, ("time", LocalTime(then)))
            : LocalDateAndTime(then);
    }

    /// <summary>A duration in seconds, minutes and seconds, or hours and minutes.</summary>
    /// <param name="duration">Rounded to whole seconds; a negative one counts as zero.</param>
    public static string Duration(TimeSpan duration) =>
        Duration((long)Math.Round(Math.Max(duration.TotalSeconds, 0), MidpointRounding.AwayFromZero));

    /// <summary>The time left, rounded up so an estimate never reads "0 s" while work remains.</summary>
    /// <param name="remaining">The estimate; a negative one counts as zero.</param>
    public static string TimeRemaining(TimeSpan remaining) =>
        Text.Format(Strings.TimeRemaining, ("duration", Duration((long)Math.Ceiling(Math.Max(remaining.TotalSeconds, 0)))));

    private static string Duration(long seconds)
    {
        if (seconds < 60)
        {
            return Text.Format(Strings.DurationSeconds, ("seconds", seconds));
        }

        var minutes = seconds / 60;
        return minutes < 60
            ? Text.Format(Strings.DurationMinutes, ("minutes", minutes), ("seconds", seconds % 60))
            : Text.Format(Strings.DurationHours, ("hours", minutes / 60), ("minutes", minutes % 60));
    }

    private static string LocalDate(DateTimeOffset local) => local.ToString(Strings.DatePattern, Text.Culture);

    private static string LocalTime(DateTimeOffset local) => local.ToString(Strings.TimePattern, Text.Culture);

    private static string LocalDateAndTime(DateTimeOffset local) =>
        Text.Format(Strings.DateAndTime, ("date", LocalDate(local)), ("time", LocalTime(local)));

    private static double RoundSize(double value)
    {
        var tenths = Math.Round(value, 1, MidpointRounding.AwayFromZero);
        return tenths < 100 ? tenths : Math.Round(value, MidpointRounding.AwayFromZero);
    }
}
