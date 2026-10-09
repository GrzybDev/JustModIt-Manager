using System.Diagnostics.CodeAnalysis;
using JustModIt.Manager.Core.Installation;
using JustModIt.Manager.Core.Library;
using JustModIt.Manager.Core.Packages;
using JustModIt.Manager.Core.Platform;

namespace JustModIt.Manager.Creator.Comparison;

/// <summary>
/// A file's size and time as the creator saw them; a build refuses a file that changed since, as the plan was made
/// from it.
/// </summary>
/// <param name="Size">In bytes.</param>
/// <param name="LastWriteTime">In UTC.</param>
public readonly record struct FileStamp(long Size, DateTimeOffset LastWriteTime)
{
    /// <summary>The stamp of a listed file.</summary>
    /// <param name="entry">The file as the file system listed it.</param>
    internal static FileStamp Of(FileEntry entry) => new(entry.Length, entry.LastWriteTimeUtc);

    /// <summary>Whether a file as listed now is still the one this stamp describes.</summary>
    /// <param name="entry">The file as the file system lists it now; null when nothing is there.</param>
    internal bool Matches(FileEntry? entry) => entry is { IsDirectory: false } file && Of(file) == this;
}

/// <summary>A file of the game folder as the index listed it.</summary>
/// <param name="Path">Game-relative, in the game's own spelling.</param>
/// <param name="FullPath">The file on disk.</param>
/// <param name="Stamp">The file as listed.</param>
public sealed record GameFile(string Path, string FullPath, FileStamp Stamp);

/// <summary>
/// The game folder listed once per comparison, so every lookup agrees on what the game holds, ignoring letter case like
/// Windows and returning the game's spelling, which a package needs on case-sensitive file systems.
/// </summary>
public sealed class GameIndex
{
    private static readonly EnumerationOptions TopLevel = new() { AttributesToSkip = 0, IgnoreInaccessible = true };

    // Hidden and system files of a game are still game files.
    private static readonly EnumerationOptions Everything =
        new() { AttributesToSkip = 0, IgnoreInaccessible = true, RecurseSubdirectories = true };

    private readonly Dictionary<string, GameFile> _files = new(PackagePath.Comparer);
    private readonly Dictionary<string, string> _directories = new(PackagePath.Comparer);
    private Dictionary<long, List<GameFile>>? _bySize;
    private Dictionary<string, List<GameFile>>? _byDirectory;
    private ILookup<string, string>? _byName;

    private GameIndex(string root, string hiddenFolderName)
    {
        Root = root;
        HiddenFolderName = hiddenFolderName;
    }

    public string Root { get; }

    public string HiddenFolderName { get; }

    public int FileCount => _files.Count;

    /// <summary>
    /// Lists <paramref name="root"/> without the app's own folder, which holds backups, and without the stores' folders
    /// (<see cref="StoreFolders"/>), which hold their downloads and records; neither holds game files.
    /// </summary>
    /// <param name="fileSystem">Lists the folder.</param>
    /// <param name="root">The game folder, canonical.</param>
    /// <param name="hiddenFolderName">The app's own folder at the game's root.</param>
    /// <param name="cancellationToken">Cancels the listing.</param>
    public static GameIndex Build(IFileSystem fileSystem, string root, string hiddenFolderName, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        var index = new GameIndex(root, hiddenFolderName);
        foreach (var entry in fileSystem.Enumerate(root, "*", TopLevel))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (entry.IsDirectory && AppFolders.IsReservedTopFolder(entry.Name, hiddenFolderName))
            {
                continue;
            }

            if (!index.Add(entry) || !entry.IsDirectory)
            {
                continue;
            }

            foreach (var inner in fileSystem.Enumerate(entry.FullPath, "*", Everything))
            {
                cancellationToken.ThrowIfCancellationRequested();
                index.Add(inner);
            }
        }

        return index;
    }

    /// <summary>Looks up a game file in any letter case.</summary>
    /// <param name="path">Game-relative.</param>
    /// <param name="file">The file, in the game's spelling; null when the game has none.</param>
    public bool TryGetFile(string path, [NotNullWhen(true)] out GameFile? file) =>
        _files.TryGetValue(path, out file);

    /// <summary>Whether the game has a folder at the path.</summary>
    /// <param name="path">Game-relative; empty for the game's root.</param>
    public bool IsDirectory(string path) => path.Length == 0 || _directories.ContainsKey(path);

    /// <summary>
    /// The game's spelling of <paramref name="path"/>: of the file when the game has it, otherwise of the longest folder
    /// the game has, followed by the rest as given.
    /// </summary>
    /// <param name="path">Game-relative, in any letter case.</param>
    public string Spell(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (_files.TryGetValue(path, out var file))
        {
            return file.Path;
        }

        if (_directories.TryGetValue(path, out var directory))
        {
            return directory;
        }

        for (var separator = path.LastIndexOf(PackagePath.Separator); separator > 0;
             separator = path.LastIndexOf(PackagePath.Separator, separator - 1))
        {
            if (_directories.TryGetValue(path[..separator], out var parent))
            {
                return parent + path[separator..];
            }
        }

        return path;
    }

    /// <summary>The game files of a size.</summary>
    /// <param name="size">In bytes.</param>
    internal IReadOnlyList<GameFile> WithSize(long size)
    {
        _bySize ??= _files.Values.GroupBy(file => file.Stamp.Size).ToDictionary(group => group.Key, group => group.ToList());
        return _bySize.TryGetValue(size, out var files) ? files : [];
    }

    /// <summary>The game files directly in a folder.</summary>
    /// <param name="directory">Game-relative; empty for the game's root.</param>
    internal IReadOnlyList<GameFile> FilesIn(string directory)
    {
        _byDirectory ??= _files.Values
            .GroupBy(file => DirectoryOf(file.Path), PackagePath.Comparer)
            .ToDictionary(group => group.Key, group => group.ToList(), PackagePath.Comparer);
        return _byDirectory.TryGetValue(directory, out var files) ? files : [];
    }

    /// <summary>Game-relative folders holding a file of this name, in any letter case.</summary>
    /// <param name="name">A file name without its folders.</param>
    internal IEnumerable<string> FoldersWith(string name)
    {
        _byName ??= _files.Values.ToLookup(file => NameOf(file.Path), file => DirectoryOf(file.Path), PackagePath.Comparer);
        return _byName[name];
    }

    /// <summary>The folder of a game path; empty at the game's root.</summary>
    /// <param name="path">Game-relative.</param>
    internal static string DirectoryOf(string path)
    {
        var separator = path.LastIndexOf(PackagePath.Separator);
        return separator < 0 ? string.Empty : path[..separator];
    }

    /// <summary>The file name of a game path.</summary>
    /// <param name="path">Game-relative.</param>
    internal static string NameOf(string path) => path[(path.LastIndexOf(PackagePath.Separator) + 1)..];

    // A link describes itself rather than its target, and the installer refuses links that lead out of the game, so a
    // link is no game file to compare with.
    private bool Add(FileEntry entry)
    {
        if (entry.IsLink)
        {
            return false;
        }

        var path = PackagePath.FromFullPath(Root, entry.FullPath);
        if (entry.IsDirectory)
        {
            _directories.TryAdd(path, path);
        }
        else
        {
            _files.TryAdd(path, new GameFile(path, entry.FullPath, FileStamp.Of(entry)));
        }

        return true;
    }
}
