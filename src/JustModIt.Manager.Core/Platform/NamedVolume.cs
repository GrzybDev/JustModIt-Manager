namespace JustModIt.Manager.Core.Platform;

/// <summary>A drive or share as the window's texts name it on Linux, where a path names no drive.</summary>
/// <param name="MountPoint">The folder it is mounted on.</param>
/// <param name="Label">Its file system's label; null when it has none.</param>
/// <param name="HoldsHome">Whether it lies on the disk that holds the user's home folder.</param>
public sealed record NamedVolume(string MountPoint, string? Label = null, bool HoldsHome = false)
{
    /// <summary>
    /// The system's drives and shares, read without touching any of them; none on Windows, whose paths name their drive.
    /// </summary>
    public static IReadOnlyList<NamedVolume> Read() => OperatingSystem.IsWindows()
        ? []
        : MountTable.ReadNamed().Volumes(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
}
