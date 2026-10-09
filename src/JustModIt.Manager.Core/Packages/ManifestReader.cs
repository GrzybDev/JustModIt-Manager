using System.Globalization;
using System.Text.Json;
using System.Text.Unicode;

namespace JustModIt.Manager.Core.Packages;

internal static class ManifestReader
{
    private const string FormatVersionProperty = "formatVersion";
    private const string RequiresProperty = "requires";
    private const string OperationsProperty = "operations";
    private const string TypeProperty = "type";
    private const string ThirdPartyNoticesProperty = "thirdPartyNotices";

    private static readonly byte[] ByteOrderMark = [0xEF, 0xBB, 0xBF];

    /// <summary>Reads a package's manifest, refusing one made for a newer app before anything else.</summary>
    /// <param name="json">The bytes of <c>manifest.json</c>, with or without a byte order mark.</param>
    /// <exception cref="InvalidPackageException">The manifest is malformed or made for a newer app.</exception>
    public static PackageManifest Parse(ReadOnlyMemory<byte> json)
    {
        json = StripByteOrderMark(json);
        // The parser checks UTF-8 only when a string is read, which would otherwise fail with another exception type.
        if (!Utf8.IsValid(json.Span))
        {
            throw Invalid("encoding");
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            CheckVersionAndCapabilities(document.RootElement);
            var manifest = document.RootElement.Deserialize(PackageJsonContext.Default.PackageManifest) ?? throw Invalid("null");
            // The serializer checks the nullability of properties but not of list items.
            return manifest.ThirdPartyNotices is not { } notices
                || (notices.Count <= ThirdPartyNotice.MaxPerPackage && notices.All(notice => notice?.IsValid() == true))
                    ? manifest
                    : throw Invalid(ThirdPartyNoticesProperty);
        }
        catch (JsonException exception)
        {
            throw new InvalidPackageException(PackageError.InvalidManifest, exception.Message, exception);
        }
    }

    /// <summary>
    /// Whether the JSON looks like a package manifest at all, for telling a package from a plain archive that happens to
    /// hold a file named like one.
    /// </summary>
    /// <param name="json">The bytes of the archive's root <c>manifest.json</c>.</param>
    public static bool DeclaresFormatVersion(ReadOnlyMemory<byte> json)
    {
        try
        {
            using var document = JsonDocument.Parse(StripByteOrderMark(json));
            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty(FormatVersionProperty, out _);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    // Runs before full deserialisation, so a package made for a newer app says "update" instead of "damaged".
    private static void CheckVersionAndCapabilities(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty(FormatVersionProperty, out var version)
            || version.ValueKind != JsonValueKind.Number
            || !version.TryGetInt32(out var formatVersion))
        {
            throw Invalid(FormatVersionProperty);
        }

        if (formatVersion != PackageManifest.CurrentFormatVersion)
        {
            throw new InvalidPackageException(PackageError.UnsupportedFormatVersion,
                formatVersion.ToString(CultureInfo.InvariantCulture));
        }

        if (root.TryGetProperty(RequiresProperty, out var requires) && requires.ValueKind != JsonValueKind.Null)
        {
            if (requires.ValueKind != JsonValueKind.Array)
            {
                throw Invalid(RequiresProperty);
            }

            foreach (var capability in requires.EnumerateArray())
            {
                RequireSupported(capability.ValueKind == JsonValueKind.String ? capability.GetString() : null, RequiresProperty);
            }
        }

        if (!root.TryGetProperty(OperationsProperty, out var operations) || operations.ValueKind != JsonValueKind.Array)
        {
            throw Invalid(OperationsProperty);
        }

        foreach (var operation in operations.EnumerateArray())
        {
            if (operation.ValueKind != JsonValueKind.Object
                || !operation.TryGetProperty(TypeProperty, out var type)
                || type.ValueKind != JsonValueKind.String
                || type.GetString() is not { Length: > 0 } name)
            {
                throw Invalid(TypeProperty);
            }

            RequireSupported(PackageCapabilities.ForOperation(name), TypeProperty);
        }
    }

    private static void RequireSupported(string? capability, string property)
    {
        if (string.IsNullOrEmpty(capability))
        {
            throw Invalid(property);
        }

        if (!PackageCapabilities.Supported.Contains(capability))
        {
            throw new InvalidPackageException(PackageError.UnsupportedCapability, capability);
        }
    }

    private static ReadOnlyMemory<byte> StripByteOrderMark(ReadOnlyMemory<byte> json) =>
        json.Span.StartsWith(ByteOrderMark) ? json[ByteOrderMark.Length..] : json;

    private static InvalidPackageException Invalid(string detail) => new(PackageError.InvalidManifest, detail);
}
