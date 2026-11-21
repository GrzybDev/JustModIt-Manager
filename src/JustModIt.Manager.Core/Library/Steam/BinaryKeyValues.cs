using System.Buffers.Binary;
using System.Text;

namespace JustModIt.Manager.Core.Library.Steam;

/// <summary>What the entry that <see cref="BinaryKeyValues"/> read last is.</summary>
internal enum BinaryKeyValueKind
{
    /// <summary>An object, whose entries follow until its end.</summary>
    Object,

    /// <summary>A text, in <see cref="BinaryKeyValues.Text"/>.</summary>
    Text,

    /// <summary>The end of the innermost object.</summary>
    End,
}

/// <summary>
/// Reads Steam's binary KeyValues an entry at a time, each a type, a key and a value, with an object's entries closed by an
/// end and keys indexing a string table, as in the app info cache, or written inline, as in the shortcuts of Steam's library.
/// </summary>
/// <param name="data">The entries of the outermost object, up to its end; bytes after it are not read.</param>
/// <param name="keys">The string table the keys index, or null for keys written inline.</param>
internal ref struct BinaryKeyValues(ReadOnlySpan<byte> data, string[]? keys)
{
    private const string CutShort = "Steam's binary KeyValues are cut short.";

    private readonly ReadOnlySpan<byte> _data = data;
    private readonly string[]? _keys = keys;
    private int _position;
    private int _depth;

    private enum EntryType : byte
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

    /// <summary>What the entry read last is.</summary>
    public BinaryKeyValueKind Kind { get; private set; }

    /// <summary>The key of the entry read last, empty for an end.</summary>
    public string Key { get; private set; } = "";

    /// <summary>The UTF-8 bytes of the text read last, without their terminator.</summary>
    public ReadOnlySpan<byte> Text { get; private set; }

    /// <summary>Reads the next object, text or end, passing over the numbers between them.</summary>
    /// <returns>False once the outermost object ends.</returns>
    /// <exception cref="InvalidDataException">
    /// The data ends inside the outermost object, names a key the string table lacks or holds a value of an unknown
    /// type.
    /// </exception>
    public bool Read()
    {
        while (true)
        {
            var type = (EntryType)Take(1)[0];
            if (type is EntryType.End or EntryType.AlternateEnd)
            {
                if (_depth == 0)
                {
                    return false;
                }

                _depth--;
                Kind = BinaryKeyValueKind.End;
                Key = "";
                return true;
            }

            Key = ReadKey();
            switch (type)
            {
                case EntryType.Object:
                    _depth++;
                    Kind = BinaryKeyValueKind.Object;
                    return true;
                case EntryType.String:
                    Text = TakeText();
                    Kind = BinaryKeyValueKind.Text;
                    return true;
                case EntryType.Int32 or EntryType.Float32 or EntryType.Pointer or EntryType.Color:
                    Take(4);
                    break;
                case EntryType.UInt64 or EntryType.Int64:
                    Take(8);
                    break;
                default:
                    throw new InvalidDataException(
                        $"Steam's binary KeyValues hold a value of the unknown type {(byte)type}.");
            }
        }
    }

    private string ReadKey()
    {
        if (_keys is null)
        {
            return Encoding.UTF8.GetString(TakeText());
        }

        var index = BinaryPrimitives.ReadInt32LittleEndian(Take(4));
        return (uint)index < (uint)_keys.Length
            ? _keys[index]
            : throw new InvalidDataException("Steam's binary KeyValues name a key their string table does not hold.");
    }

    private ReadOnlySpan<byte> TakeText()
    {
        var length = _data[_position..].IndexOf((byte)0);
        return length >= 0 ? Take(length + 1)[..length] : throw new InvalidDataException(CutShort);
    }

    private ReadOnlySpan<byte> Take(int length)
    {
        if (length > _data.Length - _position)
        {
            throw new InvalidDataException(CutShort);
        }

        var taken = _data.Slice(_position, length);
        _position += length;
        return taken;
    }
}
