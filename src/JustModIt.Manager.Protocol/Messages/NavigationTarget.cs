using System.Text.Json.Serialization;

namespace JustModIt.Manager.Protocol.Messages;

/// <summary>A place in the UI; links from any website end up here, so a target never starts an action.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "page")]
[JsonDerivedType(typeof(LibraryTarget), "library")]
[JsonDerivedType(typeof(GameTarget), "game")]
[JsonDerivedType(typeof(CatalogueTarget), "catalogue")]
[JsonDerivedType(typeof(InstallTarget), "install")]
[JsonDerivedType(typeof(SettingsTarget), "settings")]
[JsonDerivedType(typeof(CreatorTarget), "creator")]
public abstract record NavigationTarget;

/// <summary>The player's list of games.</summary>
public sealed record LibraryTarget : NavigationTarget;

/// <summary>A detected game, opened on top of the library so that going back lands there.</summary>
/// <param name="Store">The store's id, e.g. <c>steam</c>.</param>
/// <param name="GameId">The game's id in that store.</param>
public sealed record GameTarget(string Store, string GameId) : NavigationTarget;

/// <summary>The catalogue, or one game or project in it.</summary>
/// <param name="GameSlug">The game to open; null for the whole catalogue.</param>
/// <param name="TeamSlug">With <paramref name="ProjectSlug"/>, the team whose project of the game to open.</param>
/// <param name="ProjectSlug">The project to open; used only with a game and a team.</param>
public sealed record CatalogueTarget(string? GameSlug = null, string? TeamSlug = null, string? ProjectSlug = null) : NavigationTarget;

/// <summary>
/// A project as the site's download offers it: the game's page with the project and add-ons the site chose, set but not
/// saved, so only the player's install takes them; the project's page in the catalogue when no game here offers it.
/// </summary>
/// <param name="GameSlug">The game's slug in the site's addresses.</param>
/// <param name="TeamSlug">The team's slug in the site's addresses.</param>
/// <param name="ProjectSlug">The project's slug in the site's addresses.</param>
/// <param name="Features">The keys of the add-ons that are on, a group's by its chosen member; null leaves the author's.</param>
/// <param name="Variant">The variant of a release from before add-ons, by its name; null leaves the author's.</param>
public sealed record InstallTarget(string GameSlug, string TeamSlug, string ProjectSlug, IReadOnlyList<string>? Features = null,
    string? Variant = null) : NavigationTarget
{
    public bool Equals(InstallTarget? other) =>
        other is not null && GameSlug == other.GameSlug && TeamSlug == other.TeamSlug && ProjectSlug == other.ProjectSlug
        && Variant == other.Variant
        && (Features is null ? other.Features is null : other.Features is not null && Features.SequenceEqual(other.Features));

    public override int GetHashCode() => HashCode.Combine(GameSlug, TeamSlug, ProjectSlug, Variant, Features?.Count);
}

public sealed record SettingsTarget : NavigationTarget;

/// <summary>Opens the package creator only while creator mode is on.</summary>
public sealed record CreatorTarget : NavigationTarget;
