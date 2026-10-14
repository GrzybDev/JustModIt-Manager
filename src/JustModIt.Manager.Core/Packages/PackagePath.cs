using System.Buffers;
using System.Collections.Frozen;
using System.Text;
using System.Text.RegularExpressions;

namespace JustModIt.Manager.Core.Packages;

public enum PackagePathCheck
{
    Valid,
    Invalid,
    EscapesFolder,
}

/// <summary>
/// Game-relative, '/'-separated paths as packages and install records hold them, by Windows rules on every OS, because
/// packages are made for games that mostly run on Windows.
/// </summary>
public static partial class PackagePath
{
    public const char Separator = '/';

    /// <summary>NTFS allows 255 UTF-16 units in a name and most Linux file systems 255 UTF-8 bytes.</summary>
    public const int MaxNameLength = 255;

    public const int MaxLength = 1024;

    private static readonly SearchValues<char> InvalidNameCharacters =
        SearchValues.Create("<>:\"/\\|?*" + new string([.. Enumerable.Range(0, 32).Select(code => (char)code)]));

    // Windows opens these devices whatever extension follows, and also with superscript digits.
    private static readonly FrozenSet<string> ReservedDeviceNames = FrozenSet.Create(StringComparer.OrdinalIgnoreCase,
    [
        "CON", "PRN", "AUX", "NUL", "CONIN$", "CONOUT$",
        .. Enumerable.Range(0, 10).SelectMany(digit => new[] { $"COM{digit}", $"LPT{digit}" }),
        "COM¹", "COM²", "COM³", "LPT¹", "LPT²", "LPT³",
    ]);

    /// <summary>Windows gives paths that differ only in letter case to one file.</summary>
    public static StringComparer Comparer => StringComparer.OrdinalIgnoreCase;

    /// <summary>Whether a path is a safe game-relative path, and if not, whether it would escape the game's folder.</summary>
    /// <param name="path">A game-relative path with '/' separators; null is invalid.</param>
    public static PackagePathCheck Check(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length > MaxLength)
        {
            return PackagePathCheck.Invalid;
        }

        // A colon means a drive-relative path ("C:evil.dll") or an NTFS alternate data stream.
        if (path[0] == Separator || path.Contains(':', StringComparison.Ordinal) || Path.IsPathRooted(path))
        {
            return PackagePathCheck.EscapesFolder;
        }

        var result = PackagePathCheck.Valid;
        foreach (var segment in path.Split(Separator))
        {
            if (segment == "..")
            {
                return PackagePathCheck.EscapesFolder;
            }

            if (!IsValidName(segment))
            {
                result = PackagePathCheck.Invalid;
            }
        }

        return result;
    }

    /// <summary>Whether a path is a safe game-relative path.</summary>
    /// <param name="path">A game-relative path with '/' separators; null is invalid.</param>
    public static bool IsValid(string? path) => Check(path) == PackagePathCheck.Valid;

    /// <summary>
    /// Whether a single file or folder name is safe, without a trailing dot or space, which Windows strips, so "a.txt."
    /// would alias "a.txt" (the same rule rejects the "." segment).
    /// </summary>
    /// <param name="name">One segment of a path.</param>
    public static bool IsValidName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return name.Length is > 0 and <= MaxNameLength
            && Encoding.UTF8.GetByteCount(name) <= MaxNameLength
            && !name.EndsWith('.')
            && !name.EndsWith(' ')
            && !name.AsSpan().ContainsAny(InvalidNameCharacters)
            && !ReservedDeviceNames.Contains(name.Split('.')[0].TrimEnd(' '));
    }

    /// <summary>Turns backslashes into '/', since archive entry names written on Windows may use them.</summary>
    /// <param name="name">The entry name as the archive holds it.</param>
    public static string NormalizeEntryName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return name.Replace('\\', Separator);
    }

    /// <summary>The full path of a game-relative path.</summary>
    /// <param name="root">The game's folder.</param>
    /// <param name="relativePath">A game-relative path with '/' separators.</param>
    public static string ToFullPath(string root, string relativePath)
    {
        ArgumentNullException.ThrowIfNull(relativePath);
        return Path.Join(root, relativePath.Replace(Separator, Path.DirectorySeparatorChar));
    }

    /// <summary>The game-relative path of a full path, with '/' separators.</summary>
    /// <param name="root">The game's folder.</param>
    /// <param name="fullPath">A path inside it.</param>
    public static string FromFullPath(string root, string fullPath) =>
        Path.GetRelativePath(root, fullPath).Replace(Path.DirectorySeparatorChar, Separator);

    /// <summary>
    /// Whether a segment of the path is an 8.3 short name, such as <c>LIST_O~1.DAT</c>, which a file not created yet would keep
    /// as it is given, so it would never meet the file's long name.
    /// </summary>
    /// <param name="path">A game-relative path with '/' separators.</param>
    public static bool HasShortName(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return path.Split(Separator).Any(ShortName().IsMatch);
    }

    /// <summary>The first segment, e.g. <c>data</c> for <c>data/lang/text.txt</c>.</summary>
    /// <param name="path">A game-relative path with '/' separators.</param>
    public static string FirstSegment(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        var separator = path.IndexOf(Separator, StringComparison.Ordinal);
        return separator < 0 ? path : path[..separator];
    }

    [GeneratedRegex(@"^[^~]{1,6}~[0-9]+(\.[^.]{0,3})?$", RegexOptions.CultureInvariant)]
    private static partial Regex ShortName();
}
