using JustModIt.Manager.Core.Games;

namespace JustModIt.Manager.Core.Catalog;

/// <summary>A project made for a game, with what it offers that game.</summary>
public sealed record ProjectOffer(IndexEntry Project, ReleaseSelection Selection);

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
    /// The project to install: the chosen one while it is on offer, otherwise the only one. Null while several teams offer
    /// one and nobody chose, which the automation must not decide.
    /// </summary>
    public ProjectOffer? Choose(Guid? preferredProject) =>
        Projects.FirstOrDefault(offer => offer.Project.ProjectUuid == preferredProject) ?? (Projects.Count == 1 ? Projects[0] : null);

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
