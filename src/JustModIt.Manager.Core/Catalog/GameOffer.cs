using JustModIt.Manager.Core.Games;

namespace JustModIt.Manager.Core.Catalog;

/// <summary>A project made for a game, with what it offers that game.</summary>
public sealed record ProjectOffer(IndexEntry Project, ReleaseSelection Selection);

/// <summary>Everything the catalogue offers one detected game.</summary>
/// <param name="Game">The game's store and store id.</param>
/// <param name="Projects">Every project made for the game, installable or not.</param>
/// <param name="OffersTargetLanguage">
/// The game's store already sells it in the app's target language, so it is never changed automatically.
/// </param>
public sealed record GameOffer(GameKey Game, IReadOnlyList<ProjectOffer> Projects, bool OffersTargetLanguage)
{
    /// <summary>The projects with something to install; a game appears among the player's games only with one of them.</summary>
    public IReadOnlyList<ProjectOffer> Installable => [.. Projects.Where(offer => offer.Selection.IsInstallable)];

    /// <summary>
    /// The project to use: the installed or chosen one while the catalogue has it, otherwise the only installable one.
    /// Null while several teams offer one and nobody chose, which the automation must not decide.
    /// </summary>
    public ProjectOffer? Choose(Guid? preferredProject)
    {
        if (preferredProject is { } preferred && Projects.FirstOrDefault(offer => offer.Project.ProjectUuid == preferred) is { } chosen)
        {
            return chosen;
        }

        var installable = Installable;
        return installable.Count == 1 ? installable[0] : null;
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
        return new GameOffer(game, projects, language.IsOfferedByAny(projects.SelectMany(offer => offer.Selection.Links)));
    }
}
