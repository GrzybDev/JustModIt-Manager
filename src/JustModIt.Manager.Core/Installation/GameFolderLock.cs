namespace JustModIt.Manager.Core.Installation;

/// <summary>
/// Keeps other copies of the app, such as a development build or another Windows user's, from changing the same game
/// at the same time. The lock lives in the game folder because game libraries are shared between users.
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
            return new GameFolderLock(new FileStream(Path.Join(game.HiddenDirectory, FileName), FileMode.OpenOrCreate,
                FileAccess.ReadWrite, FileShare.None, bufferSize: 0, FileOptions.DeleteOnClose), game);
        }
        catch (IOException)
        {
            FileOperations.DeleteDirectoryIfEmpty(game.HiddenDirectory);
            return null;
        }
    }

    public void Dispose()
    {
        _stream.Dispose();
        FileOperations.DeleteDirectoryIfEmpty(_game.HiddenDirectory);
    }
}
