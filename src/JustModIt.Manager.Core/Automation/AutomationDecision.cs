using JustModIt.Manager.Core.Catalog;
using JustModIt.Manager.Core.Collections;
using JustModIt.Manager.Core.State;

namespace JustModIt.Manager.Core.Automation;

public enum AutomationAction
{
    None,
    Install,
    Update,
    Repair,

    /// <summary>Tells the player of a project to install, an update or a repair instead of doing it.</summary>
    Announce,
}

internal enum AutomationReason
{
    OnboardingPending,
    Hidden,

    /// <summary>A package the package creator installed; it has no release to follow.</summary>
    TestInstall,

    Busy,
    GameNotReady,
    Orphaned,
    BackupsUnavailable,

    /// <summary>
    /// What the release offers now would cost the install an add-on or its variant, or give an install from before add-ons
    /// some, which only the player decides.
    /// </summary>
    ChoiceUnavailable,

    UpToDate,

    /// <summary>Several teams offer a project for the game, and the player neither chose one nor prefers any of the teams.</summary>
    NoProjectChosen,

    AutoInstallOff,
    AutoUpdateOff,
    ReapplyOff,

    /// <summary>The store already offers the game in the app's target language.</summary>
    TargetLanguageOffered,

    /// <summary>The same action on the same release was tried a short while ago, or failed on this very build.</summary>
    RecentlyTried,

    AlreadyAnnounced,
}

/// <summary>What the automation does about a game, or about one install on it.</summary>
/// <param name="Action">What to do; <see cref="AutomationAction.None"/> to leave it.</param>
/// <param name="Reason">Why nothing is done, or why a project is only announced.</param>
internal sealed record AutomationDecision(AutomationAction Action, AutomationReason? Reason = null)
{
    /// <summary>The install an update or a repair, or the announcement of one, is about; null for a new install.</summary>
    public MyInstall? Install { get; init; }

    /// <summary>The release to install or announce; null for a repair, which installs the project's current one.</summary>
    public Release? Release { get; init; }

    /// <summary>
    /// The release the history tracks the action by: the one to install; for a repair the current one, or the installed
    /// one while the catalogue does not have the project.
    /// </summary>
    public Guid ReleaseId { get; init; }

    /// <summary>
    /// The releases an announcement tells of, each only once: the one on offer, or every team's while the player chooses.
    /// </summary>
    public ValueList<Guid> Announces { get; init; }

    public bool StartsJob => Action is AutomationAction.Install or AutomationAction.Update or AutomationAction.Repair;

    /// <summary>The announcement tells of a repair, which the history keeps per install and game build.</summary>
    public bool AnnouncesRepair => Action == AutomationAction.Announce && Install?.Status.State == MyGameState.NeedsRepair;

    /// <summary>A decision to do nothing.</summary>
    /// <param name="reason">Why nothing is done.</param>
    public static AutomationDecision Skip(AutomationReason reason) => new(AutomationAction.None, reason);
}
