using JustModIt.Manager.Core.Catalog;

namespace JustModIt.Manager.Protocol.Views;

/// <summary>A game of the catalogue with its listed projects, as a card of the catalogue shows it.</summary>
/// <param name="Id">The game's id on the site.</param>
/// <param name="Slug">The game's slug in the site's addresses.</param>
/// <param name="Name">The game's name on the site.</param>
public sealed record CatalogueGameView(Guid Id, string Slug, string Name)
{
    /// <summary>An absolute image address; empty when the game has no cover.</summary>
    public string Cover { get; init; } = "";

    public bool IsAdult { get; init; }

    public string? Platform { get; init; }

    public IReadOnlyList<string> Genres { get; init; } = [];

    public DateTimeOffset? CreatedAt { get; init; }

    /// <inheritdoc cref="GameSummary.LatestReleaseAt"/>
    public DateTimeOffset? LatestReleaseAt { get; init; }

    /// <inheritdoc cref="GameSummary.LatestReleaseVersion"/>
    public string LatestReleaseVersion { get; init; } = "";

    public IReadOnlyList<CatalogueProjectView> Projects { get; init; } = [];

    /// <summary>Maps a game of the catalogue's listing.</summary>
    /// <param name="listed">The game with its listed projects.</param>
    public static CatalogueGameView From(ListedGame listed)
    {
        ArgumentNullException.ThrowIfNull(listed);
        var game = listed.Game;
        return new CatalogueGameView(game.Uuid, game.Slug, game.Name)
        {
            Cover = game.Cover,
            IsAdult = game.IsAdult,
            Platform = game.Platform?.Name,
            Genres = [.. game.Genres.Select(genre => genre.Name)],
            CreatedAt = game.CreatedAt,
            LatestReleaseAt = game.LatestReleaseAt,
            LatestReleaseVersion = game.LatestReleaseVersion,
            Projects = [.. listed.Projects.Select(CatalogueProjectView.From)],
        };
    }
}

public sealed record CatalogueProjectView(Guid Id, string Slug, string Name, TeamView Team)
{
    public ProjectStatusView? Status { get; init; }

    public IReadOnlyList<string> Types { get; init; } = [];

    public string Summary { get; init; } = "";

    /// <summary>The primary progress in percent, when the project shows one.</summary>
    public int? Progress { get; init; }

    public DateTimeOffset? UpdatedAt { get; init; }

    /// <summary>Maps a project of the catalogue's listing.</summary>
    /// <param name="project">The project as the games list shows it.</param>
    public static CatalogueProjectView From(ProjectSummary project)
    {
        ArgumentNullException.ThrowIfNull(project);
        return new CatalogueProjectView(project.Uuid, project.Slug, project.Name, TeamView.From(project.Team))
        {
            Status = project.Status is { } status ? ProjectStatusView.From(status) : null,
            Types = [.. project.Types.Select(type => type.Name)],
            Summary = project.Summary,
            Progress = project.PrimaryProgress?.Percentage,
            UpdatedAt = project.UpdatedAt,
        };
    }
}

/// <summary>A value one of the catalogue's filters offers.</summary>
/// <param name="Id">What a <see cref="CatalogueQuery"/> filters by.</param>
/// <param name="Name">The value's name in the site's language.</param>
public sealed record CatalogueOptionView(Guid Id, string Name);

/// <summary>The values the catalogue's filters offer, in the order the site lists them: higher positions first.</summary>
public sealed record CatalogueFiltersView
{
    public IReadOnlyList<CatalogueOptionView> Platforms { get; init; } = [];

    public IReadOnlyList<CatalogueOptionView> Genres { get; init; } = [];

    public IReadOnlyList<CatalogueOptionView> Statuses { get; init; } = [];

    public IReadOnlyList<CatalogueOptionView> Types { get; init; } = [];

    public IReadOnlyList<CatalogueOptionView> Stores { get; init; } = [];

    public IReadOnlyList<CatalogueOptionView> Teams { get; init; } = [];

    /// <summary>Maps the site's lists of platforms, genres, statuses, types, stores and teams.</summary>
    /// <param name="reference">The lists the catalogue's listing holds.</param>
    public static CatalogueFiltersView From(CatalogueReference reference)
    {
        ArgumentNullException.ThrowIfNull(reference);
        return new CatalogueFiltersView
        {
            Platforms = [.. reference.Platforms.Select(item => new CatalogueOptionView(item.Uuid, item.Name))],
            Genres = [.. reference.Genres.Select(item => new CatalogueOptionView(item.Uuid, item.Name))],
            Statuses = [.. reference.Statuses.Select(item => new CatalogueOptionView(item.Uuid, item.Name))],
            Types = [.. reference.Types.Select(item => new CatalogueOptionView(item.Uuid, item.Name))],
            Stores = [.. reference.StorefrontPlatforms.Select(item => new CatalogueOptionView(item.Uuid, item.Name))],
            Teams = [.. reference.Teams.Select(item => new CatalogueOptionView(item.Uuid, item.Name))],
        };
    }
}
