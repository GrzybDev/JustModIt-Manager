namespace JustModIt.Manager.Localization;

/// <summary>Numbers, sizes, dates and durations as the app's language writes them.</summary>
public static class Formats
{
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
}
