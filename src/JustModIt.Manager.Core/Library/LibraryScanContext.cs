using JustModIt.Manager.Core.Collections;

namespace JustModIt.Manager.Core.Library;

/// <summary>What a scan of one store is given besides the file system and registry.</summary>
/// <param name="ManualFolders">Canonical folders the player added for the store (see <see cref="Settings.LibraryFolder"/>).</param>
public sealed record LibraryScanContext(ValueList<string> ManualFolders)
{
    public static LibraryScanContext Empty { get; } = new(ManualFolders: []);
}
