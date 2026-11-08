using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.IO;
using JustModIt.Manager.Core.Packages;

namespace JustModIt.Manager.Creator.Projects;

/// <summary>What a project's builds left behind, kept apart from the project so a large listing stays out of every save and export.</summary>
/// <param name="Parts">Each part that was built.</param>
public sealed record BuildRecord(IReadOnlyList<PartBuild> Parts)
{
    /// <summary>The record with a finished package's row, in place of an earlier row of its file.</summary>
    /// <param name="part">The part the package belongs to.</param>
    /// <param name="package">Its row, with its own release and date.</param>
    public BuildRecord WithPackage(string part, BuiltPackage package) =>
        new BuildRecord([.. Parts.Select(build => build with
        {
            Packages = [.. build.Packages.Where(row => !PathComparison.AreSame(row.Path, package.Path))],
        })]).With(part, build => build with { Packages = [.. build.Packages, package] });

    /// <summary>The record with what a part's finished packages read, and the release that belongs to.</summary>
    /// <param name="part">The part all of whose packages are done.</param>
    /// <param name="version">The release they were built as.</param>
    /// <param name="folders">The author's folders as they read them.</param>
    public BuildRecord WithFolders(string part, string version, IReadOnlyList<FolderBuild> folders) =>
        With(part, build => build with { Version = version, Folders = folders });

    /// <summary>The record with the new stamps of files of a part's folder whose bytes are still the ones it holds the hash of.</summary>
    /// <param name="part">The part.</param>
    /// <param name="folder">The author's folder.</param>
    /// <param name="files">The new stamps, each with the hash the file was checked against.</param>
    public BuildRecord WithStamps(string part, string folder, IReadOnlyDictionary<string, BuiltFile> files) =>
        With(part, build => build with
        {
            Folders = [.. build.Folders.Select(built => !PathComparison.AreSame(built.Folder, folder) ? built : built with
            {
                // A build that wrote the record meanwhile read its files anew, so only a stamp of the same bytes takes the place of its own.
                Files = built.Files.ToDictionary(file => file.Key,
                    file => files.GetValueOrDefault(file.Key) is { } stamp && stamp.Sha256 == file.Value.Sha256 ? stamp : file.Value),
            })],
        });

    private BuildRecord With(string part, Func<PartBuild, PartBuild> change) => new(
        Parts.Any(build => build.Part == part)
            ? [.. Parts.Select(build => build.Part == part ? change(build) : build)]
            : [.. Parts, change(new PartBuild(part, string.Empty, [], []))]);
}

/// <summary>The builds of one part of a project.</summary>
/// <param name="Part">The part's name; empty for the base.</param>
/// <param name="Version">The release the stamps in <paramref name="Folders"/> belong to.</param>
/// <param name="Folders">The author's folders as that release's build read them.</param>
/// <param name="Packages">The packages built, of that release and of earlier ones.</param>
public sealed record PartBuild(string Part, string Version, IReadOnlyList<FolderBuild> Folders, IReadOnlyList<BuiltPackage> Packages);

/// <summary>An author's folder as a build read it.</summary>
/// <param name="Folder">The folder.</param>
/// <param name="Files">Its files by their paths inside it.</param>
public sealed record FolderBuild(string Folder, IReadOnlyDictionary<string, BuiltFile> Files);

/// <summary>A file of an author's folder as a build read it.</summary>
/// <param name="Size">Its size in bytes.</param>
/// <param name="LastWriteTime">When it was last written.</param>
/// <param name="Sha256">Its hash, when the build computed one.</param>
public sealed record BuiltFile(long Size, DateTimeOffset LastWriteTime, string? Sha256 = null);

/// <summary>A package a build wrote.</summary>
/// <param name="Path">The package's file.</param>
/// <param name="Kind">Its format.</param>
/// <param name="Size">Its size in bytes.</param>
/// <param name="Versions">The versions of the game it serves.</param>
/// <param name="Version">The release it was built as.</param>
/// <param name="BuiltAt">When it was built.</param>
public sealed record BuiltPackage(string Path, PackageKind Kind, long Size, IReadOnlyList<GameKey> Versions, string Version, DateTimeOffset BuiltAt);
