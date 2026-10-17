using JustModIt.Manager.Protocol.Messages;

namespace JustModIt.Manager.Protocol.Activation;

/// <summary>
/// Reads and writes links such as <c>scheme://game/steam/1000</c>; any website can open one, so a link only navigates and
/// none installs, uninstalls or changes anything.
/// </summary>
public sealed class AppLinks
{
    private const int MaxLinkLength = 2048;
    private const int MaxSegmentLength = 256;
    private const int MaxFeatures = 64;

    private readonly string _prefix;

    /// <summary>Creates the links of one scheme.</summary>
    /// <param name="scheme">The scheme the app registers for its links, without <c>://</c>.</param>
    public AppLinks(string scheme)
    {
        ArgumentException.ThrowIfNullOrEmpty(scheme);
        if (!Uri.CheckSchemeName(scheme))
        {
            throw new ArgumentException($"'{scheme}' is not a valid URI scheme.", nameof(scheme));
        }

        Scheme = scheme;
        Open = $"{scheme}://open";
        _prefix = scheme + ":";
    }

    public string Scheme { get; }

    /// <summary>Only brings the window up; the default activation of notifications.</summary>
    public string Open { get; }

    /// <summary>Tells whether a command-line argument is one of these links.</summary>
    /// <param name="argument">An argument as the process received it.</param>
    public bool IsLink(string argument)
    {
        ArgumentNullException.ThrowIfNull(argument);
        return argument.StartsWith(_prefix, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Reads where a link leads.</summary>
    /// <param name="link">The link as opened, from any website.</param>
    /// <returns>
    /// Where the link leads, or null when it only opens the window: <see cref="Open"/>, unknown destinations and
    /// links that are not valid.
    /// </returns>
    public NavigationTarget? Parse(string link)
    {
        ArgumentNullException.ThrowIfNull(link);
        if (link.Length > MaxLinkLength)
        {
            // The site's download makes these links, so one too long to read still opens the catalogue rather than nothing.
            return link.StartsWith(Scheme + "://install", StringComparison.OrdinalIgnoreCase) ? new CatalogueTarget() : null;
        }

        if (!Uri.TryCreate(link, UriKind.Absolute, out var uri)
            || !string.Equals(uri.Scheme, Scheme, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return uri.Host.ToUpperInvariant() switch
        {
            "LIBRARY" => new LibraryTarget(),

            // A broken game link still lands in the library, where the game can be found by hand.
            "GAME" => segments.Length == 2 && Decode(segments[0]) is { } store && Decode(segments[1]) is { } gameId
                ? new GameTarget(store, gameId)
                : new LibraryTarget(),
            "CATALOGUE" => Catalogue(segments),
            "INSTALL" => Install(segments, uri.Query),
            "SETTINGS" => new SettingsTarget(),
            "CREATOR" => new CreatorTarget(),
            _ => null,
        };
    }

    /// <summary>Writes the link that leads to a place in the UI.</summary>
    /// <param name="target">Where the link should lead.</param>
    public string For(NavigationTarget target) => target switch
    {
        LibraryTarget => $"{Scheme}://library",
        GameTarget game => $"{Scheme}://game/{Uri.EscapeDataString(game.Store)}/{Uri.EscapeDataString(game.GameId)}",
        CatalogueTarget { GameSlug: { } slug, TeamSlug: { } team, ProjectSlug: { } project } =>
            $"{Scheme}://catalogue/{Uri.EscapeDataString(slug)}/{Uri.EscapeDataString(team)}/{Uri.EscapeDataString(project)}",
        CatalogueTarget { GameSlug: { } slug } => $"{Scheme}://catalogue/{Uri.EscapeDataString(slug)}",
        CatalogueTarget => $"{Scheme}://catalogue",
        InstallTarget install => Install(install),
        SettingsTarget => $"{Scheme}://settings",
        CreatorTarget => $"{Scheme}://creator",
        _ => throw new ArgumentOutOfRangeException(nameof(target), target, "Unknown navigation target."),
    };

    // A broken project link still opens its game, and a broken game link the catalogue.
    private static CatalogueTarget Catalogue(string[] segments)
    {
        if (segments.Length == 0 || Decode(segments[0]) is not { } game)
        {
            return new CatalogueTarget();
        }

        return segments.Length == 3 && Decode(segments[1]) is { } team && Decode(segments[2]) is { } project
            ? new CatalogueTarget(game, team, project)
            : new CatalogueTarget(game);
    }

    // The site writes each key apart, joined by commas; no parameter leaves the author's choice, and an empty one means
    // none.
    private static NavigationTarget Install(string[] segments, string query)
    {
        if (segments.Length != 3 || Decode(segments[0]) is not { } game || Decode(segments[1]) is not { } team
            || Decode(segments[2]) is not { } project)
        {
            return Catalogue(segments);
        }

        List<string>? features = null;
        string? variant = null;
        foreach (var parameter in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var (name, value) = parameter.IndexOf('=', StringComparison.Ordinal) is var at and >= 0
                ? (parameter[..at], parameter[(at + 1)..])
                : (parameter, "");
            if (name == "features" && features is null)
            {
                features = [.. value.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(Decode).OfType<string>().Distinct()
                    .Take(MaxFeatures)];
            }
            else if (name == "variant" && variant is null)
            {
                variant = DecodeName(value);
            }
        }

        return new InstallTarget(game, team, project, features, variant);
    }

    private string Install(InstallTarget install)
    {
        var link = $"{Scheme}://install/{Uri.EscapeDataString(install.GameSlug)}/{Uri.EscapeDataString(install.TeamSlug)}/"
            + Uri.EscapeDataString(install.ProjectSlug);
        string[] query =
        [
            .. install.Features is { } features ? [$"features={string.Join(',', features.Select(Uri.EscapeDataString))}"] : Array.Empty<string>(),
            .. install.Variant is { } variant ? [$"variant={Uri.EscapeDataString(variant)}"] : Array.Empty<string>(),
        ];
        return query.Length > 0 ? $"{link}?{string.Join('&', query)}" : link;
    }

    // A variant is named as the site shows it, so it may hold slashes that a path segment may not.
    private static string? DecodeName(string value)
    {
        var name = Uri.UnescapeDataString(value);
        return name.Length <= MaxSegmentLength && !string.IsNullOrWhiteSpace(name) && !name.Any(char.IsControl) ? name : null;
    }

    private static string? Decode(string segment)
    {
        var value = Uri.UnescapeDataString(segment);
        var valid = value.Length <= MaxSegmentLength
            && !string.IsNullOrWhiteSpace(value)
            && value is not ("." or "..")
            && !value.Any(character => char.IsControl(character) || character is '/' or '\\');
        return valid ? value : null;
    }
}
