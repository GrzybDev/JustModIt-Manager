using System.Globalization;
using System.Text.Json;
using System.Text.Unicode;
using JustModIt.Manager.Core.IO;

namespace JustModIt.Manager.Core.Packages;

internal static class ManifestReader
{
    private const string FormatVersionProperty = "formatVersion";
    private const string RequiresProperty = "requires";
    private const string OperationsProperty = "operations";
    private const string TypeProperty = "type";
    private const string ThirdPartyNoticesProperty = "thirdPartyNotices";
    private const string RuntimeFilesProperty = "runtimeFiles";
    private const string RuntimeFoldersProperty = "runtimeFolders";

    /// <summary>Reads a package's manifest, refusing one made for a newer app before anything else.</summary>
    /// <param name="json">The bytes of <c>manifest.json</c>, with or without a byte order mark.</param>
    /// <exception cref="InvalidPackageException">The manifest is malformed or made for a newer app.</exception>
    public static PackageManifest Parse(ReadOnlyMemory<byte> json)
    {
        json = Utf8Json.WithoutByteOrderMark(json);
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
                    ? WithCheckedRuntimeFiles(manifest)
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
            using var document = JsonDocument.Parse(Utf8Json.WithoutByteOrderMark(json));
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
        var required = new HashSet<string>(StringComparer.Ordinal);
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
                var name = capability.ValueKind == JsonValueKind.String ? capability.GetString() : null;
                RequireSupported(name, RequiresProperty);
                required.Add(name!);
            }
        }

        CheckDeclarations(root, RuntimeFilesProperty, PackageCapabilities.RuntimeFiles, required);
        CheckDeclarations(root, RuntimeFoldersProperty, PackageCapabilities.RuntimeFolders, required);

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

    // An app that ignored the list would install the package without ever undoing what its software changes, which is why
    // the list needs its capability while notices do not.
    private static void CheckDeclarations(JsonElement root, string property, string capability, HashSet<string> required)
    {
        if (!root.TryGetProperty(property, out var list) || list.ValueKind == JsonValueKind.Null)
        {
            return;
        }

        if (list.ValueKind != JsonValueKind.Array)
        {
            throw Invalid(property);
        }

        if (list.GetArrayLength() > 0 && !required.Contains(capability))
        {
            throw Invalid(RequiresProperty);
        }
    }

    private static PackageManifest WithCheckedRuntimeFiles(PackageManifest manifest)
    {
        if (manifest.RuntimeFiles is not { } files)
        {
            return manifest;
        }

        return files.Count <= RuntimeFile.MaxPerPackage && files.All(file => file is not null)
            ? manifest with { RuntimeFiles = [.. files.Select(Checked)] }
            : throw Invalid(RuntimeFilesProperty);
    }

    private static RuntimeFile Checked(RuntimeFile file)
    {
        if (!PackagePath.IsValid(file.Path) || PackagePath.HasShortName(file.Path))
        {
            throw new InvalidPackageException(PackageError.InvalidPath, file.Path);
        }

        if (file.Originals is not { } originals)
        {
            return file;
        }

        return originals.Count is > 0 and <= RuntimeFile.MaxOriginals
            && originals.All(original => original is { Size: >= 0 } && ManifestSteps.IsSha256(original.Sha256))
                ? file with { Originals = [.. originals.Select(original => original with { Sha256 = original.Sha256.ToLowerInvariant() })] }
                : throw Invalid(RuntimeFilesProperty);
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

    private static InvalidPackageException Invalid(string detail) => new(PackageError.InvalidManifest, detail);
}
