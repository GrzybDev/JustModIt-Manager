using System.Text;
using JustModIt.Manager.Core.Interop;

namespace JustModIt.Manager.Core.IO;

/// <summary>
/// Hides the user's name in paths within text bound for logs and reports, turning folder prefixes into placeholders such
/// as <c>%USERPROFILE%</c> and the bare user name as a path segment into <c>&lt;user&gt;</c>.
/// </summary>
public sealed class PathRedactor
{
    public const string UserPlaceholder = "<user>";

    private readonly (string Value, string Placeholder)[] _prefixes;
    private readonly string[] _userSegments;

    /// <summary>Creates a redactor for the given folders and user name.</summary>
    /// <param name="folders">Folders to replace with their placeholder, in any spelling found in text.</param>
    /// <param name="userName">The user name, replaced wherever it forms a whole path segment; null for none.</param>
    public PathRedactor(IEnumerable<(string Folder, string Placeholder)> folders, string? userName)
    {
        ArgumentNullException.ThrowIfNull(folders);
        _prefixes =
        [
            .. folders
                .Where(folder => !string.IsNullOrWhiteSpace(folder.Folder))
                .Select(folder => (Folder: Path.TrimEndingDirectorySeparator(folder.Folder), folder.Placeholder))
                // A root (a HOME of "/" in a container) would swallow every path.
                .Where(folder => !string.Equals(Path.GetPathRoot(folder.Folder), folder.Folder, StringComparison.OrdinalIgnoreCase))
                .SelectMany(folder => Spellings(folder.Folder).Select(spelling => (spelling, folder.Placeholder)))
                .DistinctBy(prefix => prefix.spelling, StringComparer.OrdinalIgnoreCase)
                // A longer folder, e.g. the local app data inside the profile, must win over its ancestors.
                .OrderByDescending(prefix => prefix.spelling.Length),
        ];
        _userSegments = string.IsNullOrWhiteSpace(userName) ? [] : [$"\\{userName}", $"/{userName}"];
    }

    /// <summary>Creates a redactor for the current user's name, profile, app data and temporary folders.</summary>
    public static PathRedactor ForCurrentUser()
    {
        var folders = new List<(string, string)>();
        if (OperatingSystem.IsWindows())
        {
            AddWindowsFolder(folders, Path.GetTempPath(), "%TEMP%");
            AddWindowsFolder(folders, Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData,
                Environment.SpecialFolderOption.DoNotVerify), "%LOCALAPPDATA%");
            AddWindowsFolder(folders, Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData,
                Environment.SpecialFolderOption.DoNotVerify), "%APPDATA%");
            AddWindowsFolder(folders, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile,
                Environment.SpecialFolderOption.DoNotVerify), "%USERPROFILE%");
        }
        else
        {
            folders.Add((Environment.GetFolderPath(Environment.SpecialFolder.UserProfile,
                Environment.SpecialFolderOption.DoNotVerify), "~"));
        }

        return new PathRedactor(folders, Environment.UserName);
    }

    /// <summary>Replaces the user's folders and name in <paramref name="text"/>.</summary>
    /// <param name="text">Any text, such as an exception with its stack trace.</param>
    public string Redact(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.AsSpan().IndexOfAny('\\', '/') < 0)
        {
            return text;
        }

        foreach (var (value, placeholder) in _prefixes)
        {
            text = Replace(text, value, placeholder);
        }

        foreach (var segment in _userSegments)
        {
            text = Replace(text, segment, string.Concat(segment.AsSpan(0, 1), UserPlaceholder));
        }

        return text;
    }

    // Text may spell a Windows path with forward slashes (Steam does) or with the backslashes escaped (JSON does).
    private static IEnumerable<string> Spellings(string folder) =>
        folder.Contains('\\', StringComparison.Ordinal)
            ? [folder, folder.Replace('\\', '/'), folder.Replace("\\", "\\\\", StringComparison.Ordinal)]
            : [folder];

    // The same folder shows up long, in its 8.3 form (a temp path often is) and as configured.
    private static void AddWindowsFolder(List<(string, string)> folders, string folder, string placeholder)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Path.IsPathFullyQualified(folder))
        {
            return;
        }

        folders.Add((folder, placeholder));
        folders.Add((Path.GetFullPath(folder), placeholder));
        if (OperatingSystem.IsWindows() && WindowsFileApi.TryGetShortPath(folder) is { } shortForm)
        {
            folders.Add((shortForm, placeholder));
        }
    }

    private static string Replace(string text, string value, string replacement)
    {
        var start = text.IndexOf(value, StringComparison.OrdinalIgnoreCase);
        if (start < 0)
        {
            return text;
        }

        var result = new StringBuilder(text.Length);
        var copied = 0;
        for (; start >= 0; start = text.IndexOf(value, start + 1, StringComparison.OrdinalIgnoreCase))
        {
            var end = start + value.Length;
            // "C:\Users\ann" must not match inside "C:\Users\anna".
            if (start < copied || (end < text.Length && char.IsLetterOrDigit(text[end])))
            {
                continue;
            }

            result.Append(text, copied, start - copied).Append(replacement);
            copied = end;
        }

        return result.Append(text, copied, text.Length - copied).ToString();
    }
}
