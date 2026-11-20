namespace JustModIt.Manager.Core.Platform;

/// <summary>The listing options the store readers share, and a listing that takes an unreadable folder as empty.</summary>
internal static class FileListing
{
    // Linux reports dot folders as hidden, and the default options would skip them. Never changed, since they are shared.

    /// <summary>Every entry, hidden ones included, skipping those that cannot be read.</summary>
    public static readonly EnumerationOptions Everything = new() { AttributesToSkip = 0, IgnoreInaccessible = true };

    /// <summary>As <see cref="Everything"/>, matching names in any letter case with plain wildcards.</summary>
    public static readonly EnumerationOptions AnyCase = new()
    {
        AttributesToSkip = 0,
        IgnoreInaccessible = true,
        MatchCasing = MatchCasing.CaseInsensitive,
        MatchType = MatchType.Simple,
    };

    /// <summary>Lists a folder at once; empty when it is missing or cannot be read.</summary>
    /// <param name="fileSystem">Lists the folder.</param>
    /// <param name="folder">The folder to list.</param>
    /// <param name="pattern">A pattern with <c>*</c> and <c>?</c> that names must match.</param>
    /// <param name="options">Recursion, casing and the attributes to skip.</param>
    /// <param name="maxEntries">The most entries listed.</param>
    public static List<FileEntry> TryList(this IFileSystem fileSystem, string folder, string pattern, EnumerationOptions options,
        int maxEntries = int.MaxValue)
    {
        try
        {
            return [.. fileSystem.Enumerate(folder, pattern, options).Take(maxEntries)];
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }
}
