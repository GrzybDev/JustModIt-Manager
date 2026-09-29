namespace JustModIt.Manager.Core.Packages;

/// <summary>The entries of a manifest package (see docs/package-format.md), whose reserved names are case-sensitive.</summary>
public static class PackageLayout
{
    public const string ZipExtension = ".zip";

    public const string ManifestEntry = "manifest.json";

    /// <summary>Whole-file payloads of <c>copy</c> operations.</summary>
    public const string FilesFolder = "files/";

    /// <summary>xdelta3 payloads of <c>patch</c> operations.</summary>
    public const string PatchesFolder = "patches/";

    public const string PatchExtension = ".xdelta";

    /// <summary>Whether a normalised entry name belongs in a manifest package apart from its manifest.</summary>
    /// <param name="name">The entry's name with '/' separators.</param>
    public static bool IsPayload(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return IsFolderOrWithin(name, FilesFolder) || IsFolderOrWithin(name, PatchesFolder);
    }

    private static bool IsFolderOrWithin(string name, string folder) =>
        name.StartsWith(folder, StringComparison.Ordinal) || name.AsSpan().SequenceEqual(folder.AsSpan(0, folder.Length - 1));
}
