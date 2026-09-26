using JustModIt.Manager.Core.Games;

namespace JustModIt.Manager.Core.Settings;

/// <summary>
/// A folder the player added because detection did not find it, such as a Steam library or a folder of GOG games, scanned
/// like the ones the store reports, where only the store's own files count as evidence of a game.
/// </summary>
/// <param name="Store">The store whose games the folder holds.</param>
/// <param name="Path">A canonical, fully qualified path.</param>
public sealed record LibraryFolder(StoreId Store, string Path);
