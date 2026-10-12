namespace JustModIt.Manager.Localization;

/// <summary>Numbers, sizes, dates and durations as the app's language writes them.</summary>
public static class Formats
{
    private static readonly TimeSpan OneMinute = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan OneHour = TimeSpan.FromHours(1);

    /// <summary>At most two decimals, with digits grouped only from where the language groups them.</summary>
    /// <param name="value">Rounded half away from zero.</param>
    public static string Number(double value) => Grouped(Math.Round(value, 2, MidpointRounding.AwayFromZero), "0.##");

    /// <summary>Binary units with one decimal, as Windows Explorer and the website count them.</summary>
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
            shown = Math.Round(value, 1, MidpointRounding.AwayFromZero);
        }
        while (shown >= 1024 && unit < 4);

        var pattern = unit switch
        {
            1 => Strings.SizeKilobytes,
            2 => Strings.SizeMegabytes,
            3 => Strings.SizeGigabytes,
            _ => Strings.SizeTerabytes,
        };
        return Text.Format(pattern, ("size", Grouped(shown, "0.0")));
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

    /// <summary>With the month's name, as the site shows when a release or a project came out.</summary>
    /// <param name="value">The moment.</param>
    /// <param name="zone">The time zone whose date it is.</param>
    public static string LongDate(DateTimeOffset value, TimeZoneInfo zone) =>
        TimeZoneInfo.ConvertTime(value, zone).ToString(Strings.LongDatePattern, Text.Culture);

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

    /// <summary>
    /// Today, yesterday, then calendar days, months and years ago in the clock's time zone, as the site tells the age of a
    /// release or a project.
    /// </summary>
    /// <param name="value">The moment.</param>
    /// <param name="clock">Gives the time now and the local time zone.</param>
    public static string CalendarAge(DateTimeOffset value, TimeProvider clock) =>
        CalendarAge(DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(value, clock.LocalTimeZone).DateTime), clock);

    /// <summary>The age of a day, told as <see cref="CalendarAge(DateTimeOffset, TimeProvider)"/> tells a moment's.</summary>
    /// <param name="then">The day.</param>
    /// <param name="clock">Gives the time now and the local time zone.</param>
    public static string CalendarAge(DateOnly then, TimeProvider clock)
    {
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), clock.LocalTimeZone).DateTime);
        var days = today.DayNumber - then.DayNumber;
        if (days < 31)
        {
            return Text.Format(Strings.CalendarAge, ("unit", "day"), ("count", Math.Max(days, 0)));
        }

        var months = ((today.Year - then.Year) * 12) + today.Month - then.Month - (today.Day < then.Day ? 1 : 0);
        return months < 12
            ? Text.Format(Strings.CalendarAge, ("unit", "month"), ("count", months))
            : Text.Format(Strings.CalendarAge, ("unit", "year"), ("count", months / 12));
    }

    /// <summary>
    /// A release's name as a version, as the site shows it: without a word for "version" before it, which teams write in
    /// the app's language or in English, and empty when the name is nothing but that word.
    /// </summary>
    /// <param name="name">The release's name as the site gives it.</param>
    public static string Version(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return Text.VersionWord.Replace(name.Trim(), "", 1);
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

    private static string Grouped(double value, string format)
    {
        // A tiny negative value rounds to -0, which .NET prints with its sign.
        var number = value == 0 ? 0 : value;
        return number.ToString(Math.Abs(number) < Text.SmallestGroupedNumber ? format : "#," + format, Text.Culture);
    }
}
