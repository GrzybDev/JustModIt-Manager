using System.Text.Json.Serialization;

namespace JustModIt.Manager.Protocol.Messages;

/// <summary>A place in the UI; links from any website end up here, so a target never starts an action.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "page")]
[JsonDerivedType(typeof(LibraryTarget), "library")]
[JsonDerivedType(typeof(GameTarget), "game")]
[JsonDerivedType(typeof(CatalogueTarget), "catalogue")]
[JsonDerivedType(typeof(SettingsTarget), "settings")]
[JsonDerivedType(typeof(CreatorTarget), "creator")]
public abstract record NavigationTarget;

/// <summary>The player's list of games.</summary>
public sealed record LibraryTarget : NavigationTarget;

/// <summary>A detected game, opened on top of the library so that going back lands there.</summary>
/// <param name="Store">The store's id, e.g. <c>steam</c>.</param>
/// <param name="GameId">The game's id in that store.</param>
public sealed record GameTarget(string Store, string GameId) : NavigationTarget;

/// <summary>The catalogue, or one game in it when <paramref name="GameSlug"/> is set.</summary>
public sealed record CatalogueTarget(string? GameSlug = null) : NavigationTarget;

public sealed record SettingsTarget : NavigationTarget;

/// <summary>Opens the package creator only while creator mode is on.</summary>
public sealed record CreatorTarget : NavigationTarget;
