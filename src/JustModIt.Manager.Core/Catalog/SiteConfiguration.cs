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

    /// <summary>Reads the site's configuration, leaving out what it does not give.</summary>
    /// <param name="root">The JSON object of <c>system/v1/config</c>.</param>
    internal static SiteConfiguration Read(JsonElement root)
    {
        var site = root.GetOrNull("site", JsonValueKind.Object) ?? default;
        var maintenance = root.GetOrNull("maintenance", JsonValueKind.Object) ?? default;
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
        };
    }
}
