namespace JustModIt.Manager.Localization.Tests.TestSupport;

internal sealed class FixedClock(DateTimeOffset now, TimeZoneInfo zone) : TimeProvider
{
    public static TimeSpan Offset { get; } = TimeSpan.FromHours(2);

    /// <summary>A time zone without daylight saving time, so the tests do not depend on the date.</summary>
    public static TimeZoneInfo Zone { get; } = TimeZoneInfo.CreateCustomTimeZone("Test+02", Offset, "Test+02", "Test+02");

    public override DateTimeOffset GetUtcNow() => now.ToUniversalTime();

    public override TimeZoneInfo LocalTimeZone => zone;
}
