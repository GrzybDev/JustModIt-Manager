using System.IO.Enumeration;
using JustModIt.Manager.Core.IO;

namespace JustModIt.Manager.Core.Platform;

/// <summary>Name matching as <see cref="Directory.EnumerateFileSystemEntries(string, string, EnumerationOptions)"/> does it.</summary>
internal static class EnumerationMatching
{
    /// <summary>Whether a name matches the search pattern.</summary>
    /// <param name="searchPattern">A pattern with <c>*</c> and <c>?</c>.</param>
    /// <param name="name">A file or directory name without its folder.</param>
    /// <param name="options">The enumeration's options, whose casing and match type apply.</param>
    public static bool IsMatch(string searchPattern, ReadOnlySpan<char> name, EnumerationOptions options)
    {
        var ignoreCase = options.MatchCasing switch
        {
            MatchCasing.CaseInsensitive => true,
            MatchCasing.CaseSensitive => false,
            _ => PathComparison.Comparison == StringComparison.OrdinalIgnoreCase,
        };
        return options.MatchType == MatchType.Win32
            ? FileSystemName.MatchesWin32Expression(FileSystemName.TranslateWin32Expression(searchPattern), name, ignoreCase)
            : FileSystemName.MatchesSimpleExpression(searchPattern, name, ignoreCase);
    }
}
