using System.Text;
using JustModIt.Manager.Core.Packages;

namespace JustModIt.Manager.Creator;

public static class FileNames
{
    private const int MaxLength = 100;

    /// <summary>
    /// A file name made from a game's name that every system accepts, for a project or a package, e.g.
    /// <c>Example: Chapter 1</c> gives <c>Example Chapter 1.zip</c>.
    /// </summary>
    /// <param name="name">The game's name; null gives the fallback.</param>
    /// <param name="fallback">Used when nothing of <paramref name="name"/> is left, or it names a device.</param>
    /// <param name="extension">With its dot, e.g. <c>.zip</c>.</param>
    public static string Suggest(string? name, string fallback, string extension)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fallback);
        ArgumentNullException.ThrowIfNull(extension);
        var cleaned = new StringBuilder();
        foreach (var character in name ?? string.Empty)
        {
            var kept = PackagePath.IsValidName(character.ToString()) || character == '.' ? character : ' ';
            if (kept != ' ' || (cleaned.Length > 0 && cleaned[^1] != ' '))
            {
                cleaned.Append(kept);
            }
        }

        var length = Math.Min(cleaned.Length, MaxLength);
        if (length < cleaned.Length && char.IsHighSurrogate(cleaned[length - 1]))
        {
            length--;
        }

        var stem = cleaned.ToString(0, length).Trim(' ', '.');
        return stem.Length > 0 && PackagePath.IsValidName(stem + extension) ? stem + extension : fallback + extension;
    }
}
