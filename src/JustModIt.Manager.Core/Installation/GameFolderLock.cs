namespace JustModIt.Manager.Core.Installation;

/// <summary>
/// Keeps other copies of the app that share its folder in games, such as another Windows user's, from changing the same
/// game at the same time; it lives in the game folder because game libraries are shared between users.
/// </summary>
internal sealed class GameFolderLock : IDisposable
{
    private const string FileName = "lock";

    private readonly FileStream _stream;
    private readonly GameFolder _game;

    private GameFolderLock(FileStream stream, GameFolder game)
    {
        _stream = stream;
        _game = game;
    }

    /// <summary>Takes the game's lock until it is disposed.</summary>
    /// <param name="game">The game folder to lock.</param>
    /// <returns>Null when someone else holds it.</returns>
    /// <exception cref="UnauthorizedAccessException">The game folder cannot be written to.</exception>
    public static GameFolderLock? TryAcquire(GameFolder game)
    {
        FileOperations.CreateHiddenDirectory(game.HiddenDirectory);
        try
        {
            // Elsewhere than on Windows a lock outlives its file's name: a copy may open the file just before its holder
            // deletes it and lock it just after, while another locks a new file at the path. Such a lock counts for nothing,
            // and closing it must not delete the new file, so the lock is taken once more.
            for (var attempt = 0; attempt < 2; attempt++)
            {
                var stream = new FileStream(Path.Join(game.HiddenDirectory, FileName), FileMode.OpenOrCreate, FileAccess.ReadWrite,
                    FileShare.None, bufferSize: 0, OperatingSystem.IsWindows() ? FileOptions.DeleteOnClose : FileOptions.None);
                if (!IsDeleted(stream))
                {
                    return new GameFolderLock(stream, game);
                }

                stream.Dispose();
            }
        }
        catch (IOException)
        {
        }

        FileOperations.DeleteDirectoryIfEmpty(game.HiddenDirectory);
        return null;
    }

    /// <summary>Whether the file a stream holds is no longer at its path, which only Linux tells.</summary>
    /// <param name="stream">The open file.</param>
    internal static bool IsDeleted(FileStream stream) =>
        OperatingSystem.IsLinux()
        && new FileInfo($"/proc/self/fd/{stream.SafeFileHandle.DangerousGetHandle()}").LinkTarget?.EndsWith(" (deleted)", StringComparison.Ordinal) == true;

    public void Dispose()
    {
        // Deleted while still locked, so a copy that opened it meanwhile finds it deleted once it gets the lock; Windows
        // deletes it as the lock goes.
        if (!OperatingSystem.IsWindows())
        {
            FileOperations.TryDeleteFile(_stream.Name);
        }

        _stream.Dispose();
        FileOperations.DeleteDirectoryIfEmpty(_game.HiddenDirectory);
    }
}
