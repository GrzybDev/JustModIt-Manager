namespace JustModIt.Manager.Creator;

/// <summary>
/// One change a package makes, written by the author. Steps run in order, each on the game folder as the steps before
/// it left it, the way the installer replays a manifest. Paths are game-relative and '/'-separated.
/// </summary>
public abstract record ProjectStep;

/// <summary>Puts the author's file at a path of the game.</summary>
/// <param name="Source">The author's file, relative to the author's folder.</param>
/// <param name="Path">Where the file goes; whatever is there is replaced.</param>
public sealed record CopyFileStep(string Source, string Path) : ProjectStep;

/// <summary>Turns the file at <paramref name="Path"/>, as the steps before left it, into the author's file.</summary>
/// <param name="Source">The author's file, relative to the author's folder.</param>
/// <param name="Path">The game file to patch.</param>
public sealed record PatchFileStep(string Source, string Path) : ProjectStep;

/// <summary>Moves a file of the game, as the steps before left it, to another path.</summary>
/// <param name="From">The file's path.</param>
/// <param name="To">Its new path.</param>
public sealed record RenameFileStep(string From, string To) : ProjectStep;

/// <summary>Deletes a file of the game, as the steps before left it.</summary>
/// <param name="Path">The file's path.</param>
public sealed record DeleteFileStep(string Path) : ProjectStep;
