using JustModIt.Manager.Core.Interop;
using JustModIt.Manager.Core.IO;

namespace JustModIt.Manager.Core.Platform;

public sealed class PhysicalVolumes : IVolumes
{
    public static PhysicalVolumes Instance { get; } = new();

    public string? GetVolumeId(string path)
    {
        if (MountPointOf(path) is not { } mountPoint)
        {
            return null;
        }

        // Network shares have no volume GUID; their root names them well enough.
        var name = OperatingSystem.IsWindows() ? WindowsVolumeApi.TryGetVolumeName(mountPoint) : null;
        return name ?? mountPoint.ToUpperInvariant();
    }

    public bool IsAttached(string volumeId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(volumeId);
        try
        {
            return Directory.Exists(volumeId);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public long? GetAvailableBytes(string path)
    {
        if (ExistingAncestor(path) is not { } directory)
        {
            return null;
        }

        if (OperatingSystem.IsWindows())
        {
            return WindowsVolumeApi.TryGetAvailableBytes(directory);
        }

        try
        {
            return MountPointOf(directory) is { } mountPoint ? new DriveInfo(mountPoint).AvailableFreeSpace : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    public bool IsNetworkPath(string path)
    {
        var full = PathComparison.Normalize(path);
        if (full.StartsWith(@"\\", StringComparison.Ordinal))
        {
            return true;
        }

        if (MountPointOf(full) is not { } mountPoint)
        {
            return false;
        }

        if (OperatingSystem.IsWindows())
        {
            return WindowsVolumeApi.IsRemote(mountPoint);
        }

        try
        {
            return new DriveInfo(mountPoint).DriveType == DriveType.Network;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
    }

    private static string? MountPointOf(string path)
    {
        if (ExistingAncestor(path) is not { } existing)
        {
            return null;
        }

        if (OperatingSystem.IsWindows())
        {
            return WindowsVolumeApi.TryGetMountPoint(existing) is { } mountPoint ? LongPath.StripPrefix(mountPoint) : null;
        }

        return DriveInfo.GetDrives()
            .Select(drive => drive.Name)
            .Where(name => PathComparison.IsWithin(name, existing))
            .MaxBy(name => name.Length);
    }

    // A missing tail says nothing about the volume, and a missing root means the drive is not there.
    private static string? ExistingAncestor(string path)
    {
        for (string? current = PathComparison.Normalize(path); current is not null; current = Path.GetDirectoryName(current))
        {
            if (Directory.Exists(current))
            {
                return current;
            }
        }

        return null;
    }
}
