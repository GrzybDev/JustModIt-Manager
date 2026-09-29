using System.Security.Cryptography;
using JustModIt.Manager.Core.IO;

namespace JustModIt.Manager.LiveTests;

/// <summary>A file as a listing sees it; a file changed in place gets another size or time.</summary>
/// <param name="Size">The file's length in bytes.</param>
/// <param name="LastWriteTimeUtc">When the file system says the file was last written.</param>
internal readonly record struct FileMark(long Size, DateTime LastWriteTimeUtc);

/// <summary>
/// A game folder at one moment, by game-relative '/' path in any letter case: every folder, every file's size and time,
/// hidden ones included, and hashes of only the files a package touches, which keeps it quick for large games.
/// </summary>
internal sealed class GameFolderState
{
    private static readonly EnumerationOptions Everything = new()
    {
        RecurseSubdirectories = true,
        AttributesToSkip = 0,
        IgnoreInaccessible = false,
    };

    private GameFolderState(Dictionary<string, FileMark> files, HashSet<string> folders, Dictionary<string, string> hashes)
    {
        Files = files;
        Folders = folders;
        Hashes = hashes;
    }

    public IReadOnlyDictionary<string, FileMark> Files { get; }

    public IReadOnlySet<string> Folders { get; }

    /// <summary>The SHA-256 of each touched file that exists.</summary>
    public IReadOnlyDictionary<string, string> Hashes { get; }

    /// <summary>Lists the whole game folder and hashes the touched files.</summary>
    /// <param name="directory">The game's folder.</param>
    /// <param name="touched">Game-relative paths of the files a package writes; those missing are left out of the hashes.</param>
    /// <param name="cancellationToken">Stops the hashing.</param>
    public static async Task<GameFolderState> CaptureAsync(string directory, IEnumerable<string> touched,
        CancellationToken cancellationToken)
    {
        var files = new Dictionary<string, FileMark>(StringComparer.OrdinalIgnoreCase);
        var folders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in new DirectoryInfo(directory).EnumerateFileSystemInfos("*", Everything))
        {
            var path = Path.GetRelativePath(directory, entry.FullName).Replace(Path.DirectorySeparatorChar, '/');
            if (entry is FileInfo file)
            {
                files[path] = new FileMark(file.Length, file.LastWriteTimeUtc);
            }
            else
            {
                folders.Add(path);
            }
        }

        var hashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in touched.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var fullPath = FullPath(directory, path);
            if (File.Exists(fullPath))
            {
                hashes[path] = await Sha256Async(fullPath, cancellationToken);
            }
        }

        return new GameFolderState(files, folders, hashes);
    }

    public static string FullPath(string directory, string path) => Path.Join(directory, path.Replace('/', Path.DirectorySeparatorChar));

    public static Task<string> Sha256Async(string path, CancellationToken cancellationToken) =>
        FileHash.ComputeAsync(path, HashAlgorithmName.SHA256, progress: null, cancellationToken);

    /// <summary>The files and folders added, removed or changed since <paramref name="earlier"/>.</summary>
    /// <param name="earlier">An earlier state of the same folder.</param>
    public IReadOnlySet<string> ChangesSince(GameFolderState earlier)
    {
        ArgumentNullException.ThrowIfNull(earlier);
        var changes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        changes.UnionWith(Files.Where(file => !earlier.Files.TryGetValue(file.Key, out var mark) || mark != file.Value)
            .Select(file => file.Key));
        changes.UnionWith(earlier.Files.Keys.Where(path => !Files.ContainsKey(path)));
        changes.UnionWith(Folders.Where(folder => !earlier.Folders.Contains(folder)));
        changes.UnionWith(earlier.Folders.Where(folder => !Folders.Contains(folder)));
        return changes;
    }

    /// <summary>
    /// The changes since <paramref name="earlier"/> other than the <paramref name="written"/> files, the folders made for
    /// them and anything inside the app's hidden folder.
    /// </summary>
    /// <param name="earlier">An earlier state of the same folder.</param>
    /// <param name="written">Game-relative paths of the files the package writes.</param>
    /// <param name="hiddenFolder">The name of the app's hidden folder at the game's root.</param>
    public IEnumerable<string> StrayChangesSince(GameFolderState earlier, IEnumerable<string> written, string hiddenFolder) =>
        ChangesSince(earlier).Where(path => !IsInFolder(hiddenFolder, path) && !written.Any(file => IsInFolder(path, file)));

    private static bool IsInFolder(string folder, string path) =>
        path.Equals(folder, StringComparison.OrdinalIgnoreCase) || path.StartsWith(folder + "/", StringComparison.OrdinalIgnoreCase);
}
