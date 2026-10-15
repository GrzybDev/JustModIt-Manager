using JustModIt.Manager.Core.Games;

namespace JustModIt.Manager.Ui.Navigation;

/// <summary>The sidebar entry a page belongs to; a page opened from it keeps it highlighted.</summary>
public enum ShellSection
{
    Library,
    Catalogue,
    Creator,
    Settings,
}

/// <summary>A place in the window; equal routes are the same place.</summary>
public abstract record Route
{
    public abstract ShellSection Section { get; }

    /// <summary>A sidebar destination: opening it clears the way back, and its page is kept while the window lives.</summary>
    public virtual bool IsRoot => false;
}

/// <summary>The player's games.</summary>
public sealed record LibraryRoute : Route
{
    public override ShellSection Section => ShellSection.Library;

    public override bool IsRoot => true;
}

/// <summary>A game of the player's list.</summary>
/// <param name="Game">The game's key.</param>
/// <param name="Choice">What a link from the site chose for the game, which its page sets without saving; null for none.</param>
public sealed record GameRoute(GameKey Game, SiteChoice? Choice = null) : Route
{
    public override ShellSection Section => ShellSection.Library;
}

/// <summary>The project and add-ons a link from the site chose, which a game's page sets until the player installs them.</summary>
/// <param name="ProjectId">The project, one the game holds or offers.</param>
/// <param name="Features">The keys of the add-ons that are on, a group's by its chosen member; null takes the author's.</param>
/// <param name="Variant">The variant of a release from before add-ons, by its name; null takes the author's.</param>
public sealed record SiteChoice(Guid ProjectId, IReadOnlyList<string>? Features, string? Variant);

/// <summary>What detection found on this computer, for a player whose game is missing.</summary>
/// <param name="From">The sidebar entry of the page it was opened from, which stays highlighted.</param>
public sealed record UndetectedGamesRoute(ShellSection From = ShellSection.Library) : Route
{
    public override ShellSection Section => From;
}

/// <summary>Everything the site offers.</summary>
public sealed record CatalogueRoute : Route
{
    public override ShellSection Section => ShellSection.Catalogue;

    public override bool IsRoot => true;
}

/// <summary>A game of the catalogue with its projects.</summary>
/// <param name="GameSlug">The game's slug in the site's addresses.</param>
public sealed record CatalogueGameRoute(string GameSlug) : Route
{
    public override ShellSection Section => ShellSection.Catalogue;
}

/// <summary>A project's page in the catalogue.</summary>
/// <param name="GameSlug">The game's slug in the site's addresses.</param>
/// <param name="TeamSlug">The team's slug in the site's addresses.</param>
/// <param name="ProjectSlug">The project's slug in the site's addresses.</param>
public sealed record CatalogueProjectRoute(string GameSlug, string TeamSlug, string ProjectSlug) : Route
{
    public override ShellSection Section => ShellSection.Catalogue;
}

/// <summary>The package creator, offered only in creator mode.</summary>
public sealed record CreatorRoute : Route
{
    public override ShellSection Section => ShellSection.Creator;

    public override bool IsRoot => true;
}

public sealed record SettingsRoute : Route
{
    public override ShellSection Section => ShellSection.Settings;

    public override bool IsRoot => true;
}

/// <summary>The licences of the app and of what ships with it, opened from the settings.</summary>
public sealed record LicensesRoute : Route
{
    public override ShellSection Section => ShellSection.Settings;
}
