using System.Buffers;
using System.Text.Json;
using JustModIt.Manager.Core.IO;
using JustModIt.Manager.Core.Packages;
using JustModIt.Manager.Core.Platform;

namespace JustModIt.Manager.Core.Installation;

/// <summary>The hidden folder of another app, or of another build of this one, at a game's root, above it or below it.</summary>
/// <param name="App">The copy of the app it belongs to, as its owner file names it.</param>
/// <param name="Directory">A full path.</param>
public sealed record OtherAppFolder(string App, string Directory);

/// <summary>
/// The owner file that marks an app's hidden folder in a game with that copy's instance name while it has an install or
/// an unfinished operation there, wherever the originals are, so other apps and builds can tell the game is managed.
/// </summary>
public static class AppFolders
{
    /// <summary>The same in every app, whatever its folder is called, so each one finds the others'.</summary>
    public const string OwnerFileName = "owner.json";

    private const int FormatVersion = 1;
    private const int MaxOwnerFileLength = 4096;
    private const string VersionProperty = "version";
    private const string AppProperty = "app";

    private static readonly EnumerationOptions TopLevel = new() { AttributesToSkip = 0, IgnoreInaccessible = true };

    /// <summary>Reads which copy of an app a folder belongs to.</summary>
    /// <param name="fileSystem">Reads the owner file.</param>
    /// <param name="directory">A folder in or around a game.</param>
    /// <returns>The copy of the app the folder belongs to; null when it is not an app's folder.</returns>
    public static string? OwnerOf(IFileSystem fileSystem, string directory)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(directory);
        // Games keep folders and files of their own under any name, so only a hidden folder with an owner file that reads
        // as this format counts.
        if (!Path.GetFileName(Path.TrimEndingDirectorySeparator(directory)).StartsWith('.'))
        {
            return null;
        }

        try
        {
            var content = new byte[MaxOwnerFileLength + 1];
            int length;
            using (var stream = fileSystem.OpenRead(Path.Join(directory, OwnerFileName), SharedFileRead.Sharing))
            {
                length = stream.ReadAtLeast(content, content.Length, throwOnEndOfStream: false);
            }

            if (length > MaxOwnerFileLength)
            {
                return null;
            }

            using var document = JsonDocument.Parse(content.AsMemory(0, length));
            var root = document.RootElement;
            return root.ValueKind == JsonValueKind.Object && root.GetOrNull(VersionProperty, JsonValueKind.Number) is not null
                ? root.GetTextOrNull(AppProperty)
                : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// The folders of other apps at the game's root and in the folders above it; any of them means that another app
    /// manages files of the game.
    /// </summary>
    /// <param name="fileSystem">Lists the folders and reads their owner files.</param>
    /// <param name="gameRoot">The game's folder.</param>
    /// <param name="app">This copy of the app, whose own folders do not count.</param>
    /// <param name="paths">
    /// Full paths in the game an operation changes; another app may manage a game inside this one, so the folders between
    /// them and the game's root count as well.
    /// </param>
    public static IReadOnlyList<OtherAppFolder> FindOthers(IFileSystem fileSystem, string gameRoot, string app,
        IEnumerable<string>? paths = null)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentException.ThrowIfNullOrWhiteSpace(gameRoot);
        var root = Path.TrimEndingDirectorySeparator(gameRoot);
        var directories = new List<string>();
        for (var directory = root; directory is not null; directory = Path.GetDirectoryName(directory))
        {
            directories.Add(directory);
        }

        var inside = new HashSet<string>(PathComparison.Comparer);
        foreach (var path in paths ?? [])
        {
            for (var directory = Path.GetDirectoryName(path);
                 directory is not null && PathComparison.IsWithin(root, directory) && !PathComparison.AreSame(root, directory)
                 && inside.Add(directory);
                 directory = Path.GetDirectoryName(directory))
            {
                directories.Add(directory);
            }
        }

        return [.. directories.SelectMany(directory => OthersIn(fileSystem, directory, app))];
    }

    /// <summary>Whether a game-relative path puts an owner file into a hidden folder, passing that folder off as an app's.</summary>
    /// <param name="relativePath">A game-relative package path.</param>
    public static bool WouldMark(string relativePath)
    {
        ArgumentNullException.ThrowIfNull(relativePath);
        var separator = relativePath.LastIndexOf(PackagePath.Separator);
        if (separator < 0 || !relativePath.AsSpan(separator + 1).Equals(OwnerFileName, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var parent = relativePath.AsSpan(0, separator);
        return parent[(parent.LastIndexOf(PackagePath.Separator) + 1)..].StartsWith('.');
    }

    /// <summary>Marks the app's folder in a game as this copy's, unless an app's owner file is there already.</summary>
    /// <param name="hiddenDirectory">The app's hidden folder at the game root.</param>
    /// <param name="app">This copy's instance name.</param>
    /// <param name="cancellationToken">Stops the write.</param>
    internal static async Task ClaimAsync(string hiddenDirectory, string app, CancellationToken cancellationToken)
    {
        if (OwnerOf(PhysicalFileSystem.Instance, hiddenDirectory) is not null)
        {
            return;
        }

        FileOperations.CreateHiddenDirectory(hiddenDirectory);
        var content = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(content))
        {
            writer.WriteStartObject();
            writer.WriteNumber(VersionProperty, FormatVersion);
            writer.WriteString(AppProperty, app);
            writer.WriteEndObject();
        }

        await AtomicFile.WriteAllBytesAsync(Path.Join(hiddenDirectory, OwnerFileName), content.WrittenMemory, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Removes this copy's owner file, and the folder once nothing else is in it.</summary>
    /// <param name="hiddenDirectory">The app's hidden folder at the game root.</param>
    /// <param name="app">This copy's instance name.</param>
    internal static void Release(string hiddenDirectory, string app)
    {
        if (string.Equals(OwnerOf(PhysicalFileSystem.Instance, hiddenDirectory), app, StringComparison.OrdinalIgnoreCase))
        {
            FileOperations.DeleteFile(Path.Join(hiddenDirectory, OwnerFileName));
        }

        FileOperations.DeleteDirectoryIfEmpty(hiddenDirectory);
    }

    private static IEnumerable<OtherAppFolder> OthersIn(IFileSystem fileSystem, string directory, string app)
    {
        List<FileEntry> folders;
        try
        {
            folders = [.. fileSystem.Enumerate(directory, ".*", TopLevel).Where(entry => entry.IsDirectory)];
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return [];
        }

        return folders
            .Select(folder => (Folder: folder, Owner: OwnerOf(fileSystem, folder.FullPath)))
            .Where(found => found.Owner is { } owner && !string.Equals(owner, app, StringComparison.OrdinalIgnoreCase))
            .Select(found => new OtherAppFolder(found.Owner!, found.Folder.FullPath));
    }
}
