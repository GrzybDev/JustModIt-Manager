using JustModIt.Manager.Core.Collections;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.Jobs;

namespace JustModIt.Manager.Core.Automation;

public enum NoticeKind
{
    /// <summary>A project can be installed, which the app did not do on its own; see <see cref="AutomationNotice.OffersTargetLanguage"/>.</summary>
    Available,

    /// <summary>
    /// Several projects, updates or repairs at once; see <see cref="AutomationNotice.Games"/>,
    /// <see cref="AutomationNotice.Updates"/> and <see cref="AutomationNotice.Repairs"/>.
    /// </summary>
    AvailableSummary,

    /// <summary>Several teams offer a project for the game, and the player is asked to choose the one to install.</summary>
    ChoiceNeeded,

    /// <summary>A new release of the installed project, which the app did not install since automatic updates are off.</summary>
    UpdateAvailable,

    /// <summary>
    /// Something, usually a game update, changed the installed files, which the app did not repair since reapplying is off.
    /// </summary>
    RepairNeeded,

    Installed,

    Updated,

    /// <summary>The project was installed again after something, usually a game update, changed its files.</summary>
    Repaired,

    /// <summary>An automatic job failed for the first time in a row; see <see cref="AutomationNotice.Failure"/>.</summary>
    Failed,

    /// <summary>An automatic update or repair removed the installed release and could not put it back.</summary>
    PreviousReleaseLost,

    /// <summary>
    /// The game's folder is gone for good, so the app forgot the install on it; see <see cref="AutomationNotice.KeptBackups"/>.
    /// </summary>
    Forgotten,
}

/// <summary>Something the automation did or found, for the app to tell the player in its own words.</summary>
/// <param name="Kind">What happened, which tells which of the other properties are set.</param>
public sealed record AutomationNotice(NoticeKind Kind)
{
    public GameKey? Game { get; init; }

    public string? GameName { get; init; }

    public string? TeamName { get; init; }

    /// <summary>The release's name as the site shows it.</summary>
    public string? ReleaseLabel { get; init; }

    public int? ReleaseVersion { get; init; }

    /// <summary>What the failed job tried to do.</summary>
    public JobKind? Job { get; init; }

    public JobFailure? Failure { get; init; }

    /// <summary>The store already offers the game in the app's target language, which is why the project was not installed.</summary>
    public bool OffersTargetLanguage { get; init; }

    /// <summary>The originals a forgotten install kept outside the game folder stay, listed among the leftover backups.</summary>
    public bool KeptBackups { get; init; }

    /// <summary>The games a summary is about.</summary>
    public ValueList<GameKey> Games { get; init; }

    /// <summary>Those of <see cref="Games"/> with an update.</summary>
    public ValueList<GameKey> Updates { get; init; }

    /// <summary>Those of <see cref="Games"/> whose install needs a repair.</summary>
    public ValueList<GameKey> Repairs { get; init; }
}
