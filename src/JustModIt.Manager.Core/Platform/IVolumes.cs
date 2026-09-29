namespace JustModIt.Manager.Core.Platform;

/// <summary>
/// The volumes that folders live on, which tell the install engine whether backing up a file is an instant rename or a
/// copy to another volume; calls may block on slow or network drives.
/// </summary>
public interface IVolumes
{
    /// <summary>
    /// A stable id of the volume holding <paramref name="path"/> or its nearest existing ancestor, which survives a
    /// change of drive letter; null when it cannot be told.
    /// </summary>
    /// <param name="path">A file or directory path, which need not exist.</param>
    string? GetVolumeId(string path);

    /// <summary>Whether a volume with this id is attached now, e.g. to tell a deleted folder from an unplugged drive.</summary>
    /// <param name="volumeId">An id from <see cref="GetVolumeId"/>.</param>
    bool IsAttached(string volumeId);

    /// <summary>Free bytes the user may fill on the volume of <paramref name="path"/>; null when unknown.</summary>
    /// <param name="path">A file or directory path, which need not exist.</param>
    long? GetAvailableBytes(string path);

    /// <summary>Whether <paramref name="path"/> is on a network share rather than a local disk.</summary>
    /// <param name="path">A file or directory path, which need not exist.</param>
    bool IsNetworkPath(string path);
}

public static class VolumesExtensions
{
    /// <summary>
    /// Whether two paths are on one volume; false when either volume is unknown, so a copy is chosen over a move that
    /// might not be atomic.
    /// </summary>
    /// <param name="volumes">The volumes to ask.</param>
    /// <param name="first">A file or directory path, which need not exist.</param>
    /// <param name="second">Another such path.</param>
    public static bool AreOnSameVolume(this IVolumes volumes, string first, string second)
    {
        ArgumentNullException.ThrowIfNull(volumes);
        return volumes.GetVolumeId(first) is { } a && volumes.GetVolumeId(second) is { } b
            && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    }
}
