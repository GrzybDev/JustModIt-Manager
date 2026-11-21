using System.Globalization;

namespace JustModIt.Manager.Ui.Shell;

/// <summary>Orders the app's versions as Semantic Versioning does, which tells the newer of two copies of the app.</summary>
internal static class AppVersions
{
    /// <summary>Compares two versions by their precedence, leaving out what the build appended.</summary>
    /// <param name="left">A version such as <c>1.2.0-beta.3</c>.</param>
    /// <param name="right">The version to compare it with.</param>
    /// <returns>Below zero when <paramref name="left"/> comes first, zero for the same version, above zero otherwise.</returns>
    public static int Compare(string left, string right)
    {
        var (leftCore, leftLabel) = Split(left);
        var (rightCore, rightLabel) = Split(right);
        var order = CompareIdentifiers(leftCore, rightCore);
        // A release comes after its pre-releases.
        return order != 0 || (leftLabel is null && rightLabel is null) ? order
            : leftLabel is null ? 1
            : rightLabel is null ? -1
            : CompareIdentifiers(leftLabel, rightLabel);
    }

    private static (string[] Core, string[]? Label) Split(string version)
    {
        var plain = version.Split('+')[0];
        var dash = plain.IndexOf('-', StringComparison.Ordinal);
        return dash < 0 ? (plain.Split('.'), null) : (plain[..dash].Split('.'), plain[(dash + 1)..].Split('.'));
    }

    // Numbers by their value and before words, words by their characters, and of two equal beginnings the shorter first.
    private static int CompareIdentifiers(string[] left, string[] right)
    {
        foreach (var (first, second) in left.Zip(right))
        {
            var firstIsNumber = ulong.TryParse(first, NumberStyles.None, CultureInfo.InvariantCulture, out var firstNumber);
            var secondIsNumber = ulong.TryParse(second, NumberStyles.None, CultureInfo.InvariantCulture, out var secondNumber);
            var order = (firstIsNumber, secondIsNumber) switch
            {
                (true, true) => firstNumber.CompareTo(secondNumber),
                (true, false) => -1,
                (false, true) => 1,
                _ => string.CompareOrdinal(first, second),
            };
            if (order != 0)
            {
                return order;
            }
        }

        return left.Length.CompareTo(right.Length);
    }
}
