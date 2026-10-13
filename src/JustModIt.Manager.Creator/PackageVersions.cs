using System.Globalization;

namespace JustModIt.Manager.Creator;

public static class PackageVersions
{
    /// <summary>
    /// The version that follows a release of the project: its name with the last number raised, e.g. <c>1.2</c> gives
    /// <c>1.3</c>, else the number after the release's own; empty when neither has a number.
    /// </summary>
    /// <param name="name">The release's name as a version, without a word for "version" before it.</param>
    /// <param name="number">The release's number on the site, counted from 1; 0 when unknown.</param>
    public static string Next(string name, int number)
    {
        ArgumentNullException.ThrowIfNull(name);
        var end = name.Length;
        while (end > 0 && !char.IsAsciiDigit(name[end - 1]))
        {
            end--;
        }

        var start = end;
        while (start > 0 && char.IsAsciiDigit(name[start - 1]))
        {
            start--;
        }

        if (start < end && long.TryParse(name.AsSpan(start, end - start), NumberStyles.None, CultureInfo.InvariantCulture, out var last)
            && last < long.MaxValue)
        {
            var raised = (last + 1).ToString(CultureInfo.InvariantCulture).PadLeft(end - start, '0');
            return name[..start] + raised + name[end..];
        }

        return number > 0 ? (number + 1L).ToString(CultureInfo.InvariantCulture) : "";
    }
}
