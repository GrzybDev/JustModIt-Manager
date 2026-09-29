using System.Text.Json.Serialization;

namespace JustModIt.Manager.Core.Catalog;

/// <summary>
/// A status the site's admins define, of which only the flags carry meaning, since names and slugs differ from site to
/// site.
/// </summary>
public sealed record ProjectStatus
{
    public required Guid Uuid { get; init; }

    public required string Name { get; init; }

    public string Slug { get; init => field = value ?? ""; } = "";

    public string Color { get; init => field = value ?? ""; } = "";

    public string Description { get; init => field = value ?? ""; } = "";

    public bool IsCompleted { get; init; }

    public bool IsAbandoned { get; init; }

    public int Position { get; init; }

    [JsonPropertyName("shows_progress")]
    public bool? ShowsProgressFlag { get; init; }

    [JsonPropertyName("allows_public_releases")]
    public bool? AllowsPublicReleasesFlag { get; init; }

    /// <summary>Whether progress bars mean something in this status; backends without the flag imply it from completion.</summary>
    [JsonIgnore]
    public bool ShowsProgress => ShowsProgressFlag ?? !IsCompleted;

    /// <summary>
    /// False while the status holds releases back from the public; backends without the flag never hold them back.
    /// </summary>
    [JsonIgnore]
    public bool AllowsPublicReleases => AllowsPublicReleasesFlag ?? true;
}

public sealed record ProjectType
{
    public required Guid Uuid { get; init; }

    public required string Name { get; init; }

    public string Slug { get; init => field = value ?? ""; } = "";

    public string Icon { get; init => field = value ?? ""; } = "";

    /// <summary>The usual type, which cards leave unmarked.</summary>
    public bool IsDefault { get; init; }

    public int Position { get; init; }
}

public sealed record ProgressKind
{
    public required Guid Uuid { get; init; }

    public required string Name { get; init; }

    public string Slug { get; init => field = value ?? ""; } = "";

    public string Icon { get; init => field = value ?? ""; } = "";

    public string Color { get; init => field = value ?? ""; } = "";

    public int Position { get; init; }
}

public sealed record ProgressStatus
{
    public required Guid Uuid { get; init; }

    public ProgressKind? Kind { get; init; }

    /// <summary><c>manual</c> (a percentage only) or <c>fraction</c> (a current value of a total).</summary>
    public string Mode { get; init => field = value ?? ""; } = "";

    public int? ManualPercentage { get; init; }

    public long? CurrentValue { get; init; }

    public long? TotalValue { get; init; }

    public string UnitLabel { get; init => field = value ?? ""; } = "";

    public int Percentage { get; init; }

    public bool IsPrimary { get; init; }

    public int Position { get; init; }
}

/// <summary>A project as its game lists it: in a game's project list and as the only project of a games list row.</summary>
public sealed record ProjectSummary
{
    public required Guid Uuid { get; init; }

    public required string Slug { get; init; }

    public required string Name { get; init; }

    public required TeamReference Team { get; init; }

    public ProjectStatus? Status { get; init; }

    public IReadOnlyList<ProjectType> Types { get; init => field = value ?? []; } = [];

    /// <summary>Plain text: the team's summary or the start of the description.</summary>
    public string Summary { get; init => field = value ?? ""; } = "";

    public ProgressStatus? PrimaryProgress { get; init; }

    public IReadOnlyList<StorefrontLink> StorefrontLinks { get; init => field = value ?? []; } = [];

    /// <summary>
    /// Moves with the team's changes to the project or its releases, files, links, credits or gallery, but not with
    /// staff hiding a release, the site's background checks or changes to its team, game or status definitions.
    /// </summary>
    public DateTimeOffset? UpdatedAt { get; init; }
}

/// <summary>The public page of a project, as an anonymous visitor sees it.</summary>
public sealed record ProjectPage
{
    public required Guid Uuid { get; init; }

    public required string Slug { get; init; }

    public required string Name { get; init; }

    /// <summary>The game, without the copy of the project that the backend nests in it.</summary>
    public required GameSummary Game { get; init; }

    public required Team Team { get; init; }

    public string CoverImage { get; init => field = value ?? ""; } = "";

    public ProjectStatus? Status { get; init; }

    public IReadOnlyList<ProjectType> Types { get; init => field = value ?? []; } = [];

    public string Summary { get; init => field = value ?? ""; } = "";

    /// <summary>GitHub-flavoured Markdown that may hold raw HTML, like the disclaimer and the instructions.</summary>
    public string Description { get; init => field = value ?? ""; } = "";

    public string Disclaimer { get; init => field = value ?? ""; } = "";

    public string InstallationInstructions { get; init => field = value ?? ""; } = "";

    public string BackgroundImage { get; init => field = value ?? ""; } = "";

    public string PageBackgroundImage { get; init => field = value ?? ""; } = "";

    public ProgressStatus? PrimaryProgress { get; init; }

    public IReadOnlyList<ProgressStatus> ProgressStatuses { get; init => field = value ?? []; } = [];

    public IReadOnlyList<StorefrontLink> StorefrontLinks { get; init => field = value ?? []; } = [];

    public IReadOnlyList<Credit> Credits { get; init => field = value ?? []; } = [];

    public IReadOnlyList<GalleryItem> Gallery { get; init => field = value ?? []; } = [];

    /// <summary>What an anonymous visitor may download: at most the public release.</summary>
    public IReadOnlyList<Release> Releases { get; init => field = value ?? []; } = [];

    /// <summary>The store build moved on since a link was saved, so the project may not fit the game any more.</summary>
    public bool IsOutdated { get; init; }

    public long TotalDownloads { get; init; }

    public DateTimeOffset? CreatedAt { get; init; }

    /// <inheritdoc cref="ProjectSummary.UpdatedAt"/>
    public DateTimeOffset? UpdatedAt { get; init; }

    [JsonIgnore]
    public ProjectLocator Locator => new(Game.Slug, Team.Slug, Uuid);

    [JsonIgnore]
    public Release? PublicRelease => Releases.FirstOrDefault(release => release is { IsPublic: true, IsHidden: false });
}

public sealed record UserSummary
{
    public required Guid Uuid { get; init; }

    public string Display { get; init => field = value ?? ""; } = "";

    public string Avatar { get; init => field = value ?? ""; } = "";
}

public sealed record Credit
{
    public required Guid Uuid { get; init; }

    public string Name { get; init => field = value ?? ""; } = "";

    public string DisplayName { get; init => field = value ?? ""; } = "";

    public UserSummary? User { get; init; }

    public string RoleLabel { get; init => field = value ?? ""; } = "";

    public int Position { get; init; }
}

public sealed record GalleryItem
{
    public required Guid Uuid { get; init; }

    /// <summary><c>image</c> or <c>youtube</c>.</summary>
    public string Kind { get; init => field = value ?? ""; } = "";

    public string Image { get; init => field = value ?? ""; } = "";

    public string YoutubeId { get; init => field = value ?? ""; } = "";

    public string Caption { get; init => field = value ?? ""; } = "";

    public int Position { get; init; }
}

/// <summary>Where a project's public page is: a team makes at most one project per game.</summary>
/// <param name="GameSlug">The game's slug.</param>
/// <param name="TeamSlug">The slug of the team that made the project.</param>
/// <param name="ProjectUuid">The project's id, which stays when slugs change.</param>
public readonly record struct ProjectLocator(string GameSlug, string TeamSlug, Guid ProjectUuid);
