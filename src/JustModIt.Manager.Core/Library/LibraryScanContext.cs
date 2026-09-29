using JustModIt.Manager.Core.Collections;

namespace JustModIt.Manager.Core.Library;

/// <summary>What a scan of one store is given besides the file system and registry.</summary>
/// <param name="ManualFolders">Canonical folders the player added for the store (see <see cref="Settings.LibraryFolder"/>).</param>
public sealed record LibraryScanContext(ValueList<string> ManualFolders)
{
    public static LibraryScanContext Empty { get; } = new(ManualFolders: []);

    /// <summary>
    /// Reads each library on its own, so a drive that does not answer holds up only its libraries, which the scan then
    /// reports as offline.
    /// </summary>
    public LibraryAccess Access { get; init; } = LibraryAccess.Unlimited;
}
