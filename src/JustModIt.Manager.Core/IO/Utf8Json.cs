namespace JustModIt.Manager.Core.IO;

/// <summary>Prepares UTF-8 JSON read from a file for the parser.</summary>
public static class Utf8Json
{
    // Editors like to add a byte order mark, and the parser refuses one in front of UTF-8 bytes.
    private static ReadOnlySpan<byte> ByteOrderMark => "\uFEFF"u8;

    /// <summary>The JSON without the byte order mark it may start with.</summary>
    /// <param name="json">The bytes as read from the file.</param>
    public static ReadOnlyMemory<byte> WithoutByteOrderMark(ReadOnlyMemory<byte> json) =>
        json.Span.StartsWith(ByteOrderMark) ? json[ByteOrderMark.Length..] : json;

    /// <summary>The JSON without the byte order mark it may start with.</summary>
    /// <param name="json">The bytes as read from the file.</param>
    public static ReadOnlySpan<byte> WithoutByteOrderMark(ReadOnlySpan<byte> json) =>
        json.StartsWith(ByteOrderMark) ? json[ByteOrderMark.Length..] : json;
}
