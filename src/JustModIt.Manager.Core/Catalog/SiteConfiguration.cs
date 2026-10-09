using System.Text.Json;
using JustModIt.Manager.Core.IO;

namespace JustModIt.Manager.Core.Catalog;

/// <summary>
/// What the site says about itself in its public configuration, so nothing about a particular site has to be assumed;
/// every value is optional, and empty text means the site did not say.
/// </summary>
public sealed record SiteConfiguration
{
    public string Name { get; init => field = value ?? ""; } = "";

    /// <summary>The site's language, e.g. <c>en</c>.</summary>
    public string Language { get; init => field = value ?? ""; } = "";

    /// <summary>The site's locale, e.g. <c>en_US</c>.</summary>
    public string Locale { get; init => field = value ?? ""; } = "";

    public string Tagline { get; init => field = value ?? ""; } = "";

    public string Description { get; init => field = value ?? ""; } = "";

    /// <summary>The site's brand colour as <c>#rrggbb</c>.</summary>
    public string BackgroundColor { get; init => field = value ?? ""; } = "";

    public string ThemeColor { get; init => field = value ?? ""; } = "";

    /// <summary>The website is closed for maintenance; the API keeps answering meanwhile.</summary>
    public bool IsUnderMaintenance { get; init; }

    public ReleaseFileRules ReleaseFiles { get; init => field = value ?? new(); } = new();

    /// <summary>Reads the site's configuration, leaving out what it does not give.</summary>
    /// <param name="root">The JSON object of <c>system/v1/config</c>.</param>
    internal static SiteConfiguration Read(JsonElement root)
    {
        var site = root.GetOrNull("site", JsonValueKind.Object) ?? default;
        var maintenance = root.GetOrNull("maintenance", JsonValueKind.Object) ?? default;
        var limits = root.GetOrNull("limits", JsonValueKind.Object);
        string Text(JsonElement section, string name) =>
            section.ValueKind == JsonValueKind.Object ? section.GetTextOrNull(name) ?? "" : "";

        return new SiteConfiguration
        {
            Name = Text(site, "name"),
            Language = Text(site, "lang"),
            Locale = Text(site, "locale"),
            Tagline = Text(site, "tagline"),
            Description = Text(site, "description"),
            BackgroundColor = Text(site, "background_color"),
            ThemeColor = Text(site, "theme_color"),
            IsUnderMaintenance = maintenance.ValueKind == JsonValueKind.Object && (maintenance.GetBooleanOrNull("active") ?? false),
            ReleaseFiles = new ReleaseFileRules
            {
                MaxBytes = limits?.GetOrNull("release_file_max_bytes", JsonValueKind.Number) is { } max
                    && max.TryGetInt64(out var bytes) && bytes > 0
                        ? bytes
                        : null,
                Formats = root.GetOrNull("release_file_formats", JsonValueKind.Array) is { } formats
                    ? [.. formats.EnumerateArray().Select(ReadFormat).OfType<ReleaseFileFormat>()]
                    : [],
            },
        };
    }

    private static ReleaseFileFormat? ReadFormat(JsonElement format) =>
        format.ValueKind == JsonValueKind.Object && format.GetTextOrNull("extension") is { } extension
            ? new ReleaseFileFormat
            {
                Extension = extension,
                Name = format.GetTextOrNull("name") ?? "",
                Description = format.GetTextOrNull("description") ?? "",
                RequiresManager = format.GetBooleanOrNull("requires_manager") ?? false,
            }
            : null;
}

/// <summary>What the site takes as release files.</summary>
public sealed record ReleaseFileRules
{
    /// <summary>The largest file the site takes; null when it does not say.</summary>
    public long? MaxBytes { get; init; }

    /// <summary>The formats it takes uploads of; empty when it does not say.</summary>
    public IReadOnlyList<ReleaseFileFormat> Formats { get; init => field = value ?? []; } = [];
}

public sealed record ReleaseFileFormat
{
    /// <summary>With its dot, e.g. <c>.zip</c>.</summary>
    public required string Extension { get; init; }

    public string Name { get; init => field = value ?? ""; } = "";

    public string Description { get; init => field = value ?? ""; } = "";

    /// <summary>Files of this format install only through the app and are never a direct download.</summary>
    public bool RequiresManager { get; init; }
}
