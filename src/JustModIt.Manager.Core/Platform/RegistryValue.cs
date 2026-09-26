using System.Collections.Immutable;
using System.Globalization;

namespace JustModIt.Manager.Core.Platform;

public enum RegistryValueType
{
    Text,

    /// <summary>Text whose environment variables have already been expanded.</summary>
    ExpandedText,
    MultiText,
    DWord,
    QWord,
    Binary,
}

public sealed class RegistryValue
{
    private readonly string? _text;
    private readonly ulong _number;

    private RegistryValue(RegistryValueType type, string? text = null, ulong number = 0, ImmutableArray<string> lines = default,
        ImmutableArray<byte> bytes = default)
    {
        Type = type;
        _text = text;
        _number = number;
        Lines = lines.IsDefault ? [] : lines;
        Bytes = bytes.IsDefault ? [] : bytes;
    }

    public RegistryValueType Type { get; }

    /// <summary>The strings of a multi-string value; empty for other types.</summary>
    public ImmutableArray<string> Lines { get; }

    /// <summary>The data of a binary value; empty for other types.</summary>
    public ImmutableArray<byte> Bytes { get; }

    /// <summary>A string value.</summary>
    /// <param name="text">The string as stored.</param>
    public static RegistryValue FromText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return new(RegistryValueType.Text, text);
    }

    /// <summary>An expandable string value.</summary>
    /// <param name="text">The string with its environment variables expanded.</param>
    public static RegistryValue FromExpandedText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return new(RegistryValueType.ExpandedText, text);
    }

    /// <summary>A multi-string value.</summary>
    /// <param name="lines">Its strings in order.</param>
    public static RegistryValue FromMultiText(IEnumerable<string> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        return new(RegistryValueType.MultiText, lines: [.. lines]);
    }

    /// <summary>A DWORD value.</summary>
    /// <param name="number">The 32-bit number.</param>
    public static RegistryValue FromDWord(uint number) => new(RegistryValueType.DWord, number: number);

    /// <summary>A QWORD value.</summary>
    /// <param name="number">The 64-bit number.</param>
    public static RegistryValue FromQWord(ulong number) => new(RegistryValueType.QWord, number: number);

    /// <summary>A binary value.</summary>
    /// <param name="bytes">The data, which is copied.</param>
    public static RegistryValue FromBinary(ReadOnlySpan<byte> bytes) => new(RegistryValueType.Binary, bytes: [.. bytes]);

    /// <summary>
    /// The text of a string value, or the decimal digits of a number, since stores write ids either way; null for other
    /// types.
    /// </summary>
    public string? AsText() => Type switch
    {
        RegistryValueType.Text or RegistryValueType.ExpandedText => _text,
        RegistryValueType.DWord or RegistryValueType.QWord => _number.ToString(CultureInfo.InvariantCulture),
        _ => null,
    };

    /// <summary>The number of a DWORD or QWORD value, read as unsigned; null for other types.</summary>
    public ulong? AsNumber() => Type is RegistryValueType.DWord or RegistryValueType.QWord ? _number : null;
}
