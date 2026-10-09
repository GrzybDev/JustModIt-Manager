namespace JustModIt.Manager.Core.Catalog;

// Every field but ids and names is optional: the deployed backend lags its repository, so newer fields may be missing,
// and a missing one must never make a whole page unreadable. Text and list properties turn null into empty in their
// init accessors, because source-generated deserialization passes null for a missing init-only property instead of
// keeping its initializer.

/// <summary>One page of a paginated list; pages are numbered from 1.</summary>
public sealed record ApiPage<T>
{
    public int Count { get; init; }

    /// <summary>Empty on the last page.</summary>
    public string Next { get; init => field = value ?? ""; } = "";

    public IReadOnlyList<T> Results { get; init => field = value ?? []; } = [];
}

/// <summary>Placeholder facts about an image, so a layout can reserve its space and colour before it loads.</summary>
public sealed record ImageMeta
{
    public int? Width { get; init; }

    public int? Height { get; init; }

    public string Color { get; init => field = value ?? ""; } = "";

    public string ComputedColor { get; init => field = value ?? ""; } = "";
}

/// <summary>A hardware platform such as PC; <see cref="HasOperatingSystems"/> says whether its games come per system.</summary>
public sealed record GamePlatform
{
    public required Guid Uuid { get; init; }

    public required string Name { get; init; }

    public string Slug { get; init => field = value ?? ""; } = "";

    /// <summary>The name of the site's icon, such as <c>FaComputer</c>; empty when it has none.</summary>
    public string Icon { get; init => field = value ?? ""; } = "";

    public int Position { get; init; }

    public bool HasOperatingSystems { get; init; }
}

public sealed record Genre
{
    public required Guid Uuid { get; init; }

    public required string Name { get; init; }

    public string Slug { get; init => field = value ?? ""; } = "";

    public string Icon { get; init => field = value ?? ""; } = "";

    public int Position { get; init; }
}

/// <summary>A game, whose store ids belong to the storefront links of its projects rather than to the game.</summary>
public record GameSummary
{
    public required Guid Uuid { get; init; }

    public required string Slug { get; init; }

    public required string Name { get; init; }

    public GamePlatform? Platform { get; init; }

    /// <summary>An absolute image address; empty when the game has no cover.</summary>
    public string Cover { get; init => field = value ?? ""; } = "";

    public ImageMeta? CoverMeta { get; init; }

    public bool IsAdult { get; init; }

    public IReadOnlyList<Genre> Genres { get; init => field = value ?? []; } = [];

    /// <summary>
    /// In the games list only the projects listed there, which leaves abandoned ones out; elsewhere every visible one.
    /// </summary>
    public int VisibleProjectCount { get; init; }

    public DateTimeOffset? CreatedAt { get; init; }

    /// <summary>When the newest release that players can download came out; null when there is none.</summary>
    public DateTimeOffset? LatestReleaseAt { get; init; }

    /// <summary>That release's label, e.g. <c>v3</c>; empty when there is none.</summary>
    public string LatestReleaseVersion { get; init => field = value ?? ""; } = "";

    /// <summary>The downloads of every listed project; null when they were not read.</summary>
    public long? TotalDownloads { get; init; }

    /// <summary>A plain copy, without what a derived row adds.</summary>
    public GameSummary ToSummary() => new(this);
}

/// <summary>A row of the games list ordered by popularity, of which only the downloads are read.</summary>
public sealed record GameDownloads
{
    public required Guid Uuid { get; init; }

    public long? TotalDownloads { get; init; }
}

/// <summary>A row of the games list.</summary>
public sealed record CatalogueGame : GameSummary
{
    /// <summary>The game's only listed project; null when it has several, which its project list names.</summary>
    public ProjectSummary? SoloProject { get; init; }
}
