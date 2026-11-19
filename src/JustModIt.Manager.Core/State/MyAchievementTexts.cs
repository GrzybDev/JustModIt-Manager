using JustModIt.Manager.Core.Catalog;

namespace JustModIt.Manager.Core.State;

/// <summary>How far a game's achievement texts got into Steam's interface, in the order the player must hear it.</summary>
public enum AchievementTextsState
{
    /// <summary>
    /// Neither Millennium nor the classic mode can bring texts into Steam's interface, also while the player keeps the app's
    /// plugin off in Millennium.
    /// </summary>
    NoDelivery,

    /// <summary>Off, since the player switched them off or they do not come on by themselves.</summary>
    Off,

    /// <summary>On, and Steam shows them once it starts again, which loads the plugin or opens the classic mode's port.</summary>
    WaitingForSteamRestart,

    /// <summary>On, and Steam has to start again, which waits while a Steam game runs, since closing Steam would close it.</summary>
    RestartBlocked,

    /// <summary>On, but this version of Steam's interface has nothing the hook can wrap, so Steam shows its own texts.</summary>
    HookBlocked,

    /// <summary>On, but Steam's interface is in another language, in which Steam shows its own texts.</summary>
    SteamInOtherLanguage,

    /// <summary>On, and Steam shows them, or will as soon as it starts.</summary>
    On,
}

/// <summary>The achievement texts a project published for a Steam game in the player's list, and whether Steam shows them.</summary>
public sealed record MyAchievementTexts
{
    public required Guid Project { get; init; }

    public required string TeamName { get; init; }

    /// <summary>The texts as the project's page sums them up: how many, of which revision, whether Steam has official ones.</summary>
    public required ProjectAchievements Texts { get; init; }

    /// <summary>
    /// Steam has its own texts in the language, so the team's take their place only together with the team's installed
    /// project, worded as the game is.
    /// </summary>
    public bool WithInstall => Texts.HasOfficial;

    /// <summary>Steam is to show the texts: the player's choice for the game, else they came on by themselves.</summary>
    public bool IsOn { get; init; }

    public AchievementTextsState State { get; init; }

    /// <summary>On, and Millennium or the classic mode can bring them into Steam's interface.</summary>
    public bool ReachesSteam => IsOn && State != AchievementTextsState.NoDelivery;
}
