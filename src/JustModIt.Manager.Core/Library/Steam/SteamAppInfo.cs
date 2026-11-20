using System.Buffers.Binary;
using System.Text;
using JustModIt.Manager.Core.Games;

namespace JustModIt.Manager.Core.Library.Steam;

/// <summary>
/// Reads the systems that Steam's app info cache (<c>appcache/appinfo.vdf</c>) names for apps and their depots, in the
/// layout current clients write: a header, one entry per app with its binary KeyValues, whose keys index a string table
/// at the end of the file.
/// </summary>
internal static class SteamAppInfo
{
    private const uint Magic = 0x07564429;

    // Info state, last change, PICS token, SHA-1 of the text info, change number, SHA-1 of the binary info.
    private const int EntryHeaderSize = 4 + 4 + 8 + 20 + 4 + 20;

    private const string OsList = "oslist";

    private enum KeyValueType : byte
    {
        Object = 0,
        String = 1,
        Int32 = 2,
        Float32 = 3,
        Pointer = 4,
        Color = 6,
        UInt64 = 7,
        End = 8,
        Int64 = 10,
        AlternateEnd = 11,
    }

    /// <summary>Reads what the cache names for the given apps, skipping every other app by its size.</summary>
    /// <param name="stream">The cache, readable and seekable; it stays open.</param>
    /// <param name="appIds">The apps to read; an app the cache lacks is left out of the result.</param>
    /// <exception cref="InvalidDataException">The cache has another layout or is damaged.</exception>
    public static Dictionary<uint, SteamAppSystems> Read(Stream stream, IReadOnlySet<uint> appIds)
    {
        try
        {
            // The entries are read a few bytes at a time and mostly skipped, so a buffer saves a system call per field. It
            // is not disposed, since that would close the caller's stream.
            return ReadEntries(new BufferedStream(stream, 1 << 16), appIds);
        }
        catch (EndOfStreamException exception)
        {
            throw new InvalidDataException("Steam's app info cache ends early.", exception);
        }
    }

    private static Dictionary<uint, SteamAppSystems> ReadEntries(Stream stream, IReadOnlySet<uint> appIds)
    {
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
        if (reader.ReadUInt32() != Magic)
        {
            throw new InvalidDataException("Steam's app info cache is in a layout this app does not read.");
        }

        reader.ReadUInt32();
        var tableOffset = reader.ReadInt64();
        var entries = stream.Position;
        if (tableOffset < entries || tableOffset > stream.Length)
        {
            throw new InvalidDataException("Steam's app info cache names a string table outside the file.");
        }

        stream.Position = tableOffset;
        var keys = ReadKeys(reader, stream.Length - tableOffset);
        stream.Position = entries;
        var found = new Dictionary<uint, SteamAppSystems>();
        for (var appId = reader.ReadUInt32(); appId != 0; appId = reader.ReadUInt32())
        {
            var size = reader.ReadUInt32();
            if (size < EntryHeaderSize || size > tableOffset - stream.Position)
            {
                throw new InvalidDataException($"The entry of app {appId} in Steam's app info cache runs past the entries.");
            }

            if (appIds.Contains(appId))
            {
                stream.Seek(EntryHeaderSize, SeekOrigin.Current);
                found[appId] = ReadSystems(reader.ReadBytes((int)size - EntryHeaderSize), keys);
            }
            else
            {
                stream.Seek(size, SeekOrigin.Current);
            }
        }

        return found;
    }

    private static string[] ReadKeys(BinaryReader reader, long length)
    {
        var count = reader.ReadUInt32();
        var rest = reader.ReadBytes((int)Math.Min(length, int.MaxValue)).AsSpan();
        // Every key takes at least its terminator, so a count beyond the bytes left fails before the array would run out.
        var keys = new string[Math.Min(count, (uint)rest.Length)];
        for (var i = 0; i < count; i++)
        {
            var end = rest.IndexOf((byte)0);
            if (end < 0)
            {
                throw new InvalidDataException("Steam's app info cache ends inside its string table.");
            }

            keys[i] = Encoding.UTF8.GetString(rest[..end]);
            rest = rest[(end + 1)..];
        }

        return keys;
    }

    // Keeps appinfo/common/oslist and appinfo/depots/<depot>/config/oslist, walking the objects with a stack of their keys.
    private static SteamAppSystems ReadSystems(ReadOnlySpan<byte> data, string[] keys)
    {
        string? common = null;
        var depots = new Dictionary<string, string>(StringComparer.Ordinal);
        var path = new List<string>();
        var position = 0;
        while (true)
        {
            var type = (KeyValueType)Take(data, ref position, 1)[0];
            if (type is KeyValueType.End or KeyValueType.AlternateEnd)
            {
                if (path.Count == 0)
                {
                    return new SteamAppSystems(common, depots);
                }

                path.RemoveAt(path.Count - 1);
                continue;
            }

            var index = BinaryPrimitives.ReadInt32LittleEndian(Take(data, ref position, 4));
            var key = (uint)index < (uint)keys.Length
                ? keys[index]
                : throw new InvalidDataException("Steam's app info cache names a key its string table does not hold.");
            switch (type)
            {
                case KeyValueType.Object:
                    path.Add(key);
                    break;
                case KeyValueType.String:
                    var end = data[position..].IndexOf((byte)0);
                    var value = end >= 0
                        ? data.Slice(position, end)
                        : throw new InvalidDataException("A text in Steam's app info cache runs past its entry.");
                    position += end + 1;
                    if (key == OsList && path is ["appinfo", "common"])
                    {
                        common = Encoding.UTF8.GetString(value);
                    }
                    else if (key == OsList && path is ["appinfo", "depots", var depot, "config"])
                    {
                        depots[depot] = Encoding.UTF8.GetString(value);
                    }

                    break;
                case KeyValueType.Int32 or KeyValueType.Float32 or KeyValueType.Pointer or KeyValueType.Color:
                    Take(data, ref position, 4);
                    break;
                case KeyValueType.UInt64 or KeyValueType.Int64:
                    Take(data, ref position, 8);
                    break;
                default:
                    throw new InvalidDataException($"Steam's app info cache holds a value of the unknown type {(byte)type}.");
            }
        }
    }

    private static ReadOnlySpan<byte> Take(ReadOnlySpan<byte> data, ref int position, int length)
    {
        if (length > data.Length - position)
        {
            throw new InvalidDataException("A value in Steam's app info cache runs past its entry.");
        }

        var taken = data.Slice(position, length);
        position += length;
        return taken;
    }
}

/// <summary>The systems that Steam's app info cache names for one app.</summary>
/// <param name="CommonOsList">The app's own list, such as <c>windows,linux</c>; null when it names none.</param>
/// <param name="DepotOsLists">The lists of the depots that name one, by depot id.</param>
internal sealed record SteamAppSystems(string? CommonOsList, IReadOnlyDictionary<string, string> DepotOsLists)
{
    /// <summary>
    /// Tells the system of the build whose depots are installed: a depot made for one system decides, Windows first,
    /// since Steam downloads a game's Windows depots exactly when it runs the game through Proton; otherwise the app's own
    /// list, where Linux wins, and without one Windows, as Steam takes it.
    /// </summary>
    /// <param name="installedDepots">The installed depots as the app manifest names them, <c>depot@manifest</c>.</param>
    public OperatingSystemKind BuildOf(IEnumerable<string> installedDepots)
    {
        var depots = installedDepots.Select(depot => DepotOsLists.GetValueOrDefault(depot.Split('@')[0])).OfType<string>()
            .Select(Systems).ToList();
        if (depots.Any(systems => systems is { Windows: true, Linux: false }))
        {
            return OperatingSystemKind.Windows;
        }

        if (depots.Any(systems => systems is { Windows: false, Linux: true }))
        {
            return OperatingSystemKind.Linux;
        }

        return Systems(CommonOsList) switch
        {
            { Linux: true } => OperatingSystemKind.Linux,
            { Windows: true } => OperatingSystemKind.Windows,
            _ => string.IsNullOrWhiteSpace(CommonOsList) ? OperatingSystemKind.Windows : OperatingSystemKind.Unknown,
        };
    }

    private static (bool Windows, bool Linux) Systems(string? osList)
    {
        var names = osList?.Split(',', StringSplitOptions.TrimEntries) ?? [];
        return (names.Contains("windows", StringComparer.OrdinalIgnoreCase), names.Contains("linux", StringComparer.OrdinalIgnoreCase));
    }
}
