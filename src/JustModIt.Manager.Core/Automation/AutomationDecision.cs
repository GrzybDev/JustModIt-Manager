using JustModIt.Manager.Core.Catalog;

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

public enum AutomationReason
{
    OnboardingPending,
    Hidden,

    /// <summary>A package the package creator installed; it has no release to follow.</summary>
    TestInstall,

    Busy,
    GameNotReady,
    Orphaned,
    Unavailable,
    BackupsUnavailable,

    /// <summary>The current release has nothing yet for the installed variant, e.g. its voice-over is still being uploaded.</summary>
    VariantUnavailable,

    UpToDate,

    /// <summary>Several teams offer a project for the game and the player has not chosen one.</summary>
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
public sealed record AutomationDecision(AutomationAction Action, AutomationReason? Reason = null)
{
    /// <summary>The release to install or announce; null for a repair, which installs the project's current one.</summary>
    public Release? Release { get; init; }

    /// <summary>
    /// The release the history tracks the action by: the one to install; for a repair the current one, or the installed
    /// one while the catalogue does not have the project.
    /// </summary>
    public Guid ReleaseId { get; init; }

    public ReleaseVariant? Variant { get; init; }

    public bool StartsJob => Action is AutomationAction.Install or AutomationAction.Update or AutomationAction.Repair;

    /// <summary>A decision to do nothing.</summary>
    /// <param name="reason">Why nothing is done.</param>
    public static AutomationDecision Skip(AutomationReason reason) => new(AutomationAction.None, reason);
}
