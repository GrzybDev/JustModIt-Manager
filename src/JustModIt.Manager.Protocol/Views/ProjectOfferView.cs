using JustModIt.Manager.Core.Catalog;

namespace JustModIt.Manager.Protocol.Views;

/// <summary>A project made for a game, with what its public release offers that game.</summary>
public sealed record ProjectOfferView
{
    public required Guid ProjectId { get; init; }

    public required string ProjectSlug { get; init; }

    public required string ProjectName { get; init; }

    public required string GameSlug { get; init; }

    public required string GameName { get; init; }

    /// <summary>An absolute image address; empty when the game has no cover.</summary>
    public string Cover { get; init; } = "";

    public required TeamView Team { get; init; }

    public ProjectStatusView? Status { get; init; }

    public IReadOnlyList<string> Types { get; init; } = [];

    /// <summary>The game's store version has a build the team has not confirmed the project with.</summary>
    public bool IsOutdated { get; init; }

    /// <summary>When that build appeared, when the site says.</summary>
    public DateTimeOffset? OutdatedSince { get; init; }

    /// <summary>That build's id in the store's terms; empty when the site does not say.</summary>
    public string LatestBuild { get; init; } = "";

    public DateTimeOffset? UpdatedAt { get; init; }

    /// <summary>The public release; null when there is none to offer.</summary>
    public ReleaseView? Release { get; init; }

    /// <summary>The base's package for the game; null when none fits, and for a release from before add-ons.</summary>
    public PackageView? Base { get; init; }

    /// <summary>The add-ons that fit the game, or the variants of a release from before add-ons, in the release's order.</summary>
    public IReadOnlyList<FeatureView> Features { get; init; } = [];

    /// <summary>A release from before add-ons whose variants make one choice, which the app names itself.</summary>
    public bool IsLegacy { get; init; }

    /// <summary>Why nothing can be installed; null when something can.</summary>
    public SelectionUnavailability? Unavailable { get; init; }

    /// <summary>Maps what a project offers a game, as a new install takes it or, for an installed one, as its install does.</summary>
    /// <param name="offer">The project with the release and the plan that fit the game.</param>
    /// <param name="installed">The packages the project's install holds already; null for a new install.</param>
    public static ProjectOfferView From(ProjectOffer offer, InstalledRelease? installed = null)
    {
        ArgumentNullException.ThrowIfNull(offer);
        var project = offer.Project;
        var selection = offer.Selection;
        var plan = offer.Plan;
        return new ProjectOfferView
        {
            ProjectId = project.ProjectUuid,
            ProjectSlug = project.ProjectSlug,
            ProjectName = project.ProjectName,
            GameSlug = project.Game.Slug,
            GameName = project.Game.Name,
            Cover = project.Game.Cover,
            Team = TeamView.From(project.Team),
            Status = project.Status is { } status ? ProjectStatusView.From(status) : null,
            Types = [.. project.Types.Select(type => type.Name)],
            IsOutdated = offer.IsOutdated,
            OutdatedSince = offer.OutdatedLink?.OutdatedSince,
            LatestBuild = offer.OutdatedLink?.LatestBuild ?? "",
            UpdatedAt = project.UpdatedAt,
            Release = selection.Release is { } release ? ReleaseView.From(release) : null,
            Base = selection.Base is { } package ? PackageView.From(package) : null,
            Features = [.. selection.Features.Select(feature => FeatureView.From(feature,
                plan is not null && (plan.FeatureKeys.Contains(feature.Key) || plan.Variant == feature.Key), installed))],
            IsLegacy = selection.IsLegacy,
            Unavailable = selection.Unavailable,
        };
    }
}

public sealed record TeamView(Guid Id, string Slug, string Name)
{
    /// <summary>An absolute image address; empty when the team has none.</summary>
    public string Avatar { get; init; } = "";

    /// <summary>Maps a team as the site names it.</summary>
    /// <param name="team">The team a project belongs to.</param>
    public static TeamView From(TeamReference team)
    {
        ArgumentNullException.ThrowIfNull(team);
        return new TeamView(team.Uuid, team.Slug, team.Name) { Avatar = team.Avatar };
    }
}

/// <summary>A status the site's admins defined, with the flags that give it meaning.</summary>
/// <param name="Id">The status's id on the site.</param>
/// <param name="Name">The status's name in the site's language.</param>
public sealed record ProjectStatusView(Guid Id, string Name)
{
    /// <summary>The site's colour for it, as <c>#rrggbb</c>; empty when none.</summary>
    public string Color { get; init; } = "";

    public bool IsCompleted { get; init; }

    public bool IsAbandoned { get; init; }

    public bool ShowsProgress { get; init; }

    /// <summary>
    /// The status keeps releases from the public, as while work has not started or once the game's makers ship the work.
    /// </summary>
    public bool HoldsReleasesBack { get; init; }

    /// <summary>Maps a project's status.</summary>
    /// <param name="status">The status as the site defines it.</param>
    public static ProjectStatusView From(ProjectStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);
        return new ProjectStatusView(status.Uuid, status.Name)
        {
            Color = status.Color,
            IsCompleted = status.IsCompleted,
            IsAbandoned = status.IsAbandoned,
            ShowsProgress = status.ShowsProgress,
            HoldsReleasesBack = !status.AllowsPublicReleases,
        };
    }
}

public enum ReleaseDateKind
{
    Released,
    Updated,
}

public sealed record ReleaseView(Guid Id, int Version)
{
    /// <summary>The team's own name for the release; empty when it gave none.</summary>
    public string Name { get; init; } = "";

    /// <summary>As the site shows it: when the release came out, or when it last changed if the site does not know.</summary>
    public DateTimeOffset? Date { get; init; }

    public ReleaseDateKind DateKind { get; init; }

    /// <summary>Maps a project's public release.</summary>
    /// <param name="release">The release as the project's page holds it.</param>
    public static ReleaseView From(Release release)
    {
        ArgumentNullException.ThrowIfNull(release);
        // The site dates a release by its creation, on its pages and as a game's newest release, until it tells when the
        // release first went public.
        var released = release.FirstPublishedAt ?? release.CreatedAt;
        return new ReleaseView(release.Uuid, release.Version)
        {
            Name = release.Name,
            Date = released ?? release.UpdatedAt,
            DateKind = released is null ? ReleaseDateKind.Updated : ReleaseDateKind.Released,
        };
    }
}

/// <summary>
/// An add-on that fits the game, or a variant of a release from before add-ons, with whether the plan takes it, as the
/// agent works it out with the engine's rules so the window never repeats them.
/// </summary>
/// <param name="Key">The add-on's key, which commands choose it by; a variant's name.</param>
/// <param name="Name">The name to show.</param>
/// <param name="Description">Plain text; empty for none.</param>
/// <param name="IsDefault">The author turns it on, or recommends it among its group.</param>
/// <param name="IsOn">
/// What installing, or the pending update or repair, takes with the player's choices; for an install that is up to date,
/// what it has.
/// </param>
public sealed record FeatureView(string Key, string Name, string Description, bool IsDefault, bool IsOn)
{
    /// <summary>The caption of the choice it makes with others; null for an add-on on its own.</summary>
    public string? Group { get; init; }

    public required PackageView Package { get; init; }

    /// <summary>The install the offer is shown for holds the package already, so taking it downloads nothing.</summary>
    public bool IsPresent { get; init; }

    /// <summary>Maps an add-on of the release.</summary>
    /// <param name="feature">The add-on with the package picked for the game.</param>
    /// <param name="isOn">Whether the plan takes it.</param>
    /// <param name="installed">The packages the project's install holds already; null for a new install.</param>
    public static FeatureView From(FeatureOffer feature, bool isOn, InstalledRelease? installed)
    {
        ArgumentNullException.ThrowIfNull(feature);
        return new FeatureView(feature.Key, feature.Name, feature.Description, feature.IsDefault, isOn)
        {
            Group = feature.Group,
            Package = PackageView.From(feature.Package),
            IsPresent = installed?.Holds(feature.Package) == true,
        };
    }
}

public sealed record PackageView(Guid FileId, string FileName)
{
    /// <summary>In bytes; null while the upload is pending.</summary>
    public long? Size { get; init; }

    public PackageFormat? Format { get; init; }

    /// <summary>Maps a release file.</summary>
    /// <param name="file">The file as the release lists it.</param>
    public static PackageView From(ReleaseFile file)
    {
        ArgumentNullException.ThrowIfNull(file);
        return new PackageView(file.Uuid, file.DownloadFilename) { Size = file.Size, Format = file.Format };
    }
}
