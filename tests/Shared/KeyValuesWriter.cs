using System.Buffers.Binary;
using System.Text;

namespace JustModIt.Manager.Tests;

/// <summary>Writes Valve's binary KeyValues with inline keys, as Steam writes its shortcuts.</summary>
internal sealed class KeyValuesWriter
{
    private readonly List<byte> _bytes = [];

    public KeyValuesWriter Object(string key) => Entry(0, key);

    public KeyValuesWriter Text(string key, string value) => Entry(1, key).Terminated(value);

    public KeyValuesWriter Number(string key, int value)
    {
        Entry(2, key);
        var bytes = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, value);
        _bytes.AddRange(bytes);
        return this;
    }

    public KeyValuesWriter End()
    {
        _bytes.Add(8);
        return this;
    }

    public byte[] ToArray() => [.. _bytes];

    private KeyValuesWriter Entry(byte type, string key)
    {
        _bytes.Add(type);
        return Terminated(key);
    }

    private KeyValuesWriter Terminated(string text)
    {
        _bytes.AddRange(Encoding.UTF8.GetBytes(text));
        _bytes.Add(0);
        return this;
    }
}
