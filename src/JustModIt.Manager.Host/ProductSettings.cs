using JustModIt.Manager.Core;

namespace JustModIt.Manager.Host;

/// <summary>An app's identity and site settings, as its build configured them; the app's ProductConfiguration.Settings.</summary>
public sealed record ProductSettings
{
    public required string ProductName { get; init; }

    public required string AppId { get; init; }

    public required string VendorId { get; init; }

    public required string AppSlug { get; init; }

    /// <summary>The extension, with its dot, of the app's manifest packages.</summary>
    public required string PackageExtension { get; init; }

    /// <summary>The extension, with its dot, of the package creator's project files.</summary>
    public required string CreatorProjectExtension { get; init; }

    public required string ApiUrl { get; init; }

    public string SiteUrl { get; init; } = "";

    /// <summary>Empty uses the API anonymously.</summary>
    public string ApiKey { get; init; } = "";

    /// <summary>The Sentry DSN that error reports go to, with the player's consent; empty offers no reports.</summary>
    public string ErrorReportsDsn { get; init; } = "";

    /// <summary>Empty turns self-update off.</summary>
    public string UpdateUrl { get; init; } = "";

    /// <summary>Empty follows the channel the installed build came from.</summary>
    public string UpdateChannel { get; init; } = "";

    public bool UpdatePrereleases { get; init; }

    /// <summary>Empty offers no link to the source code.</summary>
    public string SourceUrl { get; init; } = "";

    /// <summary>Empty offers no way to report a problem.</summary>
    public string IssuesUrl { get; init; } = "";

    public string Language { get; init; } = "";

    public string TargetLanguage { get; init; } = "";

    public string AccentColor { get; init; } = "";

    /// <summary>
    /// The hosts, separated by semicolons, that the window loads images from, each alone or with the path its images are
    /// under, as the site loads them; empty loads none.
    /// </summary>
    public string ImageHosts { get; init; } = "";

    public bool IsDevelopmentBuild { get; init; }

    /// <summary>What a development build adds to its names, e.g. "Dev".</summary>
    public string DevelopmentName { get; init; } = "Dev";

    /// <summary>A development build registers autostart and the link scheme, which otherwise only an installed copy does.</summary>
    public bool DevelopmentShellIntegration { get; init; }

    public AppIdentity Identity => new(AppId, VendorId, AppSlug, IsDevelopmentBuild, DevelopmentName);
}
