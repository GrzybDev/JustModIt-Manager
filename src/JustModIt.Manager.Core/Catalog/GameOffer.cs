using JustModIt.Manager.Core.Games;

namespace JustModIt.Manager.Core.Catalog;

/// <summary>A project made for a game, with what it offers that game.</summary>
/// <param name="Project">The project as the index has it.</param>
/// <param name="Selection">What its public release offers the game.</param>
public sealed record ProjectOffer(IndexEntry Project, ReleaseSelection Selection)
{
    /// <summary>The link to the game's store version whose newest build the team has not confirmed yet.</summary>
    public StorefrontLink? OutdatedLink => Selection.Links.FirstOrDefault(link => link.IsOutdated == true);

    /// <summary>
    /// Whether the game's store version has a build the team has not confirmed the project with, judged for the whole
    /// project when the backend does not say it per link.
    /// </summary>
    public bool IsOutdated =>
        Project.StorefrontLinks.Any(link => link.IsOutdated is not null) ? OutdatedLink is not null : Project.IsOutdated;
}

/// <summary>What the catalogue offers one detected game.</summary>
/// <param name="Game">The game's store and store id.</param>
/// <param name="Projects">
/// The completed projects with a package that fits the game; without any, only an install puts the game among the
/// player's games.
/// </param>
/// <param name="OffersTargetLanguage">
/// The game's store already sells it in the app's target language, so it is never changed automatically.
/// </param>
public sealed record GameOffer(GameKey Game, IReadOnlyList<ProjectOffer> Projects, bool OffersTargetLanguage)
{
    /// <summary>
    /// The project to install: the chosen one while it is on offer, otherwise the only one, otherwise the one of the most
    /// preferred team that offers one.
    /// </summary>
    /// <param name="preferredProject">The project the player chose for the game; null when none.</param>
    /// <param name="preferredTeams">The teams the player prefers, the most preferred first.</param>
    /// <returns>Null while several teams offer one and none is chosen or preferred, which the automation must not decide.</returns>
    public ProjectOffer? Choose(Guid? preferredProject, IReadOnlyList<Guid> preferredTeams)
    {
        ArgumentNullException.ThrowIfNull(preferredTeams);
        return Projects.FirstOrDefault(offer => offer.Project.ProjectUuid == preferredProject)
            ?? (Projects.Count == 1 ? Projects[0] : null)
            ?? preferredTeams.Select(team => Projects.FirstOrDefault(offer => offer.Project.Team.Uuid == team))
                .FirstOrDefault(offer => offer is not null);
    }

    /// <summary>What the index offers the game.</summary>
    /// <param name="index">The installable index.</param>
    /// <param name="game">The game's store and store id.</param>
    /// <param name="gameSystem">The system the installed build is made for.</param>
    /// <param name="language">The app's target language, checked against the game's store version.</param>
    public static GameOffer For(InstallableIndex index, GameKey game, OperatingSystemKind gameSystem, TargetLanguage language)
    {
        ArgumentNullException.ThrowIfNull(index);
        ArgumentNullException.ThrowIfNull(language);
        var projects = index.Find(game)
            .Select(project => new ProjectOffer(project, ReleaseSelector.Select(project, game, gameSystem)))
            .ToList();
        // Only the links to the player's store version count: another store's edition may be sold in other languages.
        return new GameOffer(game, [.. projects.Where(IsEligible)],
            language.IsOfferedByAny(projects.SelectMany(offer => offer.Selection.Links)));
    }

    private static bool IsEligible(ProjectOffer offer) => offer.Project.Status is { IsCompleted: true } && offer.Selection.IsInstallable;
}
