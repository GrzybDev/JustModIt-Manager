using System.Globalization;

namespace JustModIt.Manager.Core.Platform;

/// <summary>A registry value the store readers use: a string or a number.</summary>
public sealed class RegistryValue
{
    private readonly string? _text;
    private readonly ulong? _number;

    private RegistryValue(string? text, ulong? number)
    {
        _text = text;
        _number = number;
    }

    /// <summary>A string value, expandable or not.</summary>
    /// <param name="text">The string as read.</param>
    public static RegistryValue FromText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return new(text, null);
    }

    /// <summary>A DWORD value.</summary>
    /// <param name="number">The 32-bit number.</param>
    public static RegistryValue FromDWord(uint number) => new(null, number);

    /// <summary>A QWORD value.</summary>
    /// <param name="number">The 64-bit number.</param>
    public static RegistryValue FromQWord(ulong number) => new(null, number);

    /// <summary>The text of a string value, or the decimal digits of a number, since stores write ids either way.</summary>
    public string? AsText() => _text ?? _number?.ToString(CultureInfo.InvariantCulture);

    /// <summary>The number of a DWORD or QWORD value, read as unsigned; null for a string.</summary>
    public ulong? AsNumber() => _number;
}
