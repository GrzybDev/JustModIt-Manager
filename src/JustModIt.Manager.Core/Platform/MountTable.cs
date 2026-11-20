using System.Text;
using System.Text.RegularExpressions;

namespace JustModIt.Manager.Core.Platform;

/// <summary>
/// The file systems Linux has mounted and the mount points <c>/etc/fstab</c> names, read again for every question since
/// drives come and go; it never touches the paths it is asked about, so a mount that does not answer cannot block it.
/// </summary>
internal sealed partial class MountTable
{
    private const string MountInfoFile = "/proc/self/mountinfo";
    private const string FstabFile = "/etc/fstab";
    private const string UuidLinks = "/dev/disk/by-uuid";
    private const string LabelLinks = "/dev/disk/by-label";

    // udisks mounts removable drives in these folders and removes each drive's folder when it goes.
    private static readonly string[] RemovableMedia = ["/run/media/", "/media/"];

    private readonly IReadOnlyList<string> _configured;

    private MountTable(IReadOnlyList<Mount> mounts, IReadOnlyList<string> configured)
    {
        Mounts = mounts;
        _configured = configured;
    }

    /// <summary>Every mount, in the order the system made them.</summary>
    public IReadOnlyList<Mount> Mounts { get; }

    /// <summary>Reads the system's mounts and its fstab, which is all that telling a path's drive needs.</summary>
    public static MountTable Read() => Parse(File.ReadAllText(MountInfoFile), ReadFstab(), _ => null);

    /// <summary>Reads the system's mounts and its fstab with the UUIDs and labels of its file systems, which name its drives.</summary>
    public static MountTable ReadNamed()
    {
        var uuids = DeviceNames(UuidLinks);
        var labels = DeviceNames(LabelLinks);
        return Parse(File.ReadAllText(MountInfoFile), ReadFstab(), source =>
        {
            var device = DeviceOf(source);
            return new FileSystemNames(uuids.GetValueOrDefault(device), labels.GetValueOrDefault(device));
        });
    }

    /// <summary>Reads the mounts from the texts of the system's files.</summary>
    /// <param name="mountInfo">The text of <c>/proc/self/mountinfo</c>.</param>
    /// <param name="fstab">The text of <c>/etc/fstab</c>.</param>
    /// <param name="namesOf">The UUID and label of the file system on a mount's source; null for a source without them.</param>
    public static MountTable Parse(string mountInfo, string fstab, Func<string, FileSystemNames?> namesOf)
    {
        var mounts = new List<Mount>();
        foreach (var line in mountInfo.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var fields = line.Split(' ');
            // Optional fields of any number come before the separator.
            var separator = Array.IndexOf(fields, "-", 6);
            if (separator < 0 || separator + 2 >= fields.Length)
            {
                continue;
            }

            var source = Unescape(fields[separator + 2]);
            var names = namesOf(source);
            mounts.Add(new Mount(Unescape(fields[4]), fields[separator + 1], source, names?.Uuid, names?.Label));
        }

        var configured = new List<string>();
        foreach (var line in fstab.Split('\n'))
        {
            // Swap and other entries without a mount point write none or swap in its place.
            var fields = line.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
            if (fields is [var device, ['/', ..] point, ..] && !device.StartsWith('#'))
            {
                configured.Add(TrimEnd(Unescape(point)));
            }
        }

        return new MountTable(mounts, configured);
    }

    /// <summary>
    /// Decodes the name of a link udev makes for a file system, which writes a space, a slash or a byte of no UTF-8 character
    /// as <c>\xHH</c>.
    /// </summary>
    /// <param name="linkName">The link's name, such as a label in <c>/dev/disk/by-label</c>.</param>
    public static string UdevName(string linkName) => HexEscapes().Replace(linkName, escaped =>
        Encoding.UTF8.GetString(Convert.FromHexString(escaped.Value.Replace(@"\x", "", StringComparison.Ordinal))));

    /// <summary>The mount a path lies on: the deepest mount point holding it, the last one made on that point.</summary>
    /// <param name="path">A full, canonical path.</param>
    public Mount? MountOf(string path)
    {
        Mount? found = null;
        foreach (var mount in Mounts)
        {
            if (IsWithin(mount.Point, path) && mount.Point.Length >= (found?.Point.Length ?? 0))
            {
                found = mount;
            }
        }

        return found;
    }

    /// <summary>
    /// Whether the drive a path lies on is not mounted: a removable drive's folder or a mount point fstab names, while a
    /// plain folder is no drive.
    /// </summary>
    /// <param name="path">A full, canonical path.</param>
    /// <param name="user">The user's name, which names the folder of the user's removable drives.</param>
    public bool IsDriveMissing(string path, string user)
    {
        var root = RemovableRootOf(path, user);
        foreach (var point in _configured)
        {
            if (IsWithin(point, path) && point.Length > (root?.Length ?? 0))
            {
                root = point;
            }
        }

        return root is not null && !Mounts.Any(mount => mount.Point == root);
    }

    /// <summary>The mounts of drives and shares as the window names them, the disk holding the home folder marked.</summary>
    /// <param name="home">The user's home folder.</param>
    public IReadOnlyList<NamedVolume> Volumes(string home)
    {
        // Bind mounts of the home folder's file system, such as SteamOS keeps under /opt and /var, lie on its disk as well.
        var homeSource = MountOf(home)?.Source;
        return [.. Mounts.Where(mount => mount.IsVolume)
            .Select(mount => new NamedVolume(mount.Point, mount.Label, mount.Source == homeSource))];
    }

    private static string? RemovableRootOf(string path, string user)
    {
        foreach (var media in RemovableMedia)
        {
            if (path.StartsWith(media, StringComparison.Ordinal))
            {
                var segments = path[media.Length..].Split('/', StringSplitOptions.RemoveEmptyEntries);
                var depth = segments is [var first, ..] && first == user ? 2 : 1;
                return segments.Length >= depth ? media + string.Join('/', segments[..depth]) : null;
            }
        }

        return null;
    }

    private static bool IsWithin(string point, string path) =>
        point == "/" || path == point || (path.StartsWith(point, StringComparison.Ordinal) && path[point.Length] == '/');

    private static string TrimEnd(string point) => point.Length > 1 ? point.TrimEnd('/') : point;

    // Spaces, tabs, line breaks and backslashes in paths are written as octal escapes such as \040.
    private static string Unescape(string field) =>
        OctalEscape().Replace(field, match => ((char)Convert.ToInt32(match.Groups[1].Value, 8)).ToString());

    // fstab may be missing, and a hardened system may keep it from users.
    private static string ReadFstab()
    {
        try
        {
            return File.ReadAllText(FstabFile);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return string.Empty;
        }
    }

    // udev names each file system by a link to its device.
    private static Dictionary<string, string> DeviceNames(string folder)
    {
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        try
        {
            foreach (var link in new DirectoryInfo(folder).EnumerateFileSystemInfos())
            {
                if (TargetOf(link) is { } target)
                {
                    names[Path.GetFullPath(target, folder)] = UdevName(link.Name);
                }
            }
        }
        // A system without a file system of that kind has no such folder, and udev removes it with the last one.
        catch (DirectoryNotFoundException)
        {
        }

        return names;
    }

    // udev removes a drive's links as the drive goes, also between listing a link and reading it.
    private static string? TargetOf(FileSystemInfo link)
    {
        try
        {
            return link.LinkTarget;
        }
        catch (IOException)
        {
            return null;
        }
    }

    // A mount may name its device by another link, such as an encrypted volume's in /dev/mapper.
    private static string DeviceOf(string source)
    {
        if (!source.StartsWith("/dev/", StringComparison.Ordinal))
        {
            return source;
        }

        try
        {
            return File.ResolveLinkTarget(source, returnFinalTarget: true)?.FullName ?? source;
        }
        catch (IOException)
        {
            return source;
        }
    }

    [GeneratedRegex(@"\\([0-7]{3})", RegexOptions.CultureInvariant)]
    private static partial Regex OctalEscape();

    [GeneratedRegex(@"(?:\\x[0-9a-fA-F]{2})+", RegexOptions.CultureInvariant)]
    private static partial Regex HexEscapes();
}

/// <summary>What udev names a file system by.</summary>
/// <param name="Uuid">Its UUID; null when it has none.</param>
/// <param name="Label">Its label; null when it has none.</param>
internal sealed record FileSystemNames(string? Uuid, string? Label);

/// <summary>A mounted file system.</summary>
/// <param name="Point">The folder it is mounted on.</param>
/// <param name="Type">Its file system's type, such as <c>ext4</c> or <c>nfs4</c>.</param>
/// <param name="Source">What it is mounted from: a block device, a network share, or a name such as <c>tmpfs</c>.</param>
/// <param name="Uuid">Its file system's UUID; null when it has none.</param>
/// <param name="Label">Its file system's label; null when it has none.</param>
internal sealed record Mount(string Point, string Type, string Source, string? Uuid, string? Label)
{
    private static readonly HashSet<string> NetworkTypes =
        ["nfs", "nfs4", "cifs", "smb3", "smbfs", "fuse.sshfs", "fuse.rclone", "fuse.gvfsd-fuse"];

    /// <summary>
    /// Its file system's UUID (else its source) with the mount point, since a rename fails between two mounts of one file
    /// system and another card at the same place is another drive.
    /// </summary>
    public string VolumeId => $"{Uuid ?? Source}@{Point}";

    /// <summary>Whether the file system is a network share rather than a local disk.</summary>
    public bool IsNetwork => NetworkTypes.Contains(Type);

    /// <summary>Whether it holds a drive's or a share's files rather than the system's own, as memory and kernel file systems do.</summary>
    public bool IsVolume => Source.StartsWith("/dev/", StringComparison.Ordinal) || IsNetwork;
}
