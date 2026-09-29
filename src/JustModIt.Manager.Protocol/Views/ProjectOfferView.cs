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

    /// <summary>The site marks the project as made for an older version of the game.</summary>
    public bool IsOutdated { get; init; }

    public DateTimeOffset? UpdatedAt { get; init; }

    /// <summary>The public release; null when there is none to offer.</summary>
    public ReleaseView? Release { get; init; }

    public IReadOnlyList<VariantView> Variants { get; init; } = [];

    /// <summary>The variant to recommend; null exactly when nothing can be installed.</summary>
    public string? DefaultVariant { get; init; }

    /// <summary>Why nothing can be installed; null when <see cref="DefaultVariant"/> is set.</summary>
    public SelectionUnavailability? Unavailable { get; init; }

    /// <summary>Maps what a project offers a game.</summary>
    /// <param name="offer">The project with the release and variants that fit the game.</param>
    public static ProjectOfferView From(ProjectOffer offer)
    {
        ArgumentNullException.ThrowIfNull(offer);
        var project = offer.Project;
        var selection = offer.Selection;
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
            IsOutdated = project.IsOutdated,
            UpdatedAt = project.UpdatedAt,
            Release = selection.Release is { } release ? ReleaseView.From(release) : null,
            Variants = [.. selection.Variants.Select(VariantView.From)],
            DefaultVariant = selection.Default?.Name,
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
        };
    }
}

public sealed record ReleaseView(Guid Id, int Version)
{
    /// <summary>The team's own name for the release; empty when it gave none.</summary>
    public string Name { get; init; } = "";

    public DateTimeOffset? PublishedAt { get; init; }

    /// <summary>Maps a project's public release.</summary>
    /// <param name="release">The release as the project's page holds it.</param>
    public static ReleaseView From(Release release)
    {
        ArgumentNullException.ThrowIfNull(release);
        return new ReleaseView(release.Uuid, release.Version) { Name = release.Name, PublishedAt = release.PublishedAt };
    }
}

/// <summary>A choice such as "text only" next to "text and voice", with the package that fits the game.</summary>
/// <param name="Name">The variant's name, which commands choose it by.</param>
public sealed record VariantView(string Name)
{
    public string Description { get; init; } = "";

    public PackageView? Package { get; init; }

    /// <summary>Null exactly when <see cref="Package"/> is set.</summary>
    public SelectionUnavailability? Unavailable { get; init; }

    /// <summary>Maps a variant of the release.</summary>
    /// <param name="variant">The variant with the package picked for the game.</param>
    public static VariantView From(ReleaseVariant variant)
    {
        ArgumentNullException.ThrowIfNull(variant);
        return new VariantView(variant.Name)
        {
            Description = variant.Description,
            Package = variant.Package is { } file ? PackageView.From(file) : null,
            Unavailable = variant.Unavailable,
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
