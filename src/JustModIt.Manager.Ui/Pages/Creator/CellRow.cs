using JustModIt.Manager.Core.Collections;
using JustModIt.Manager.Localization;

namespace JustModIt.Manager.Ui.Pages.Creator;

/// <summary>Where a part stands on a version of the game.</summary>
public enum CellState
{
    /// <summary>The part has no folder of the author's yet.</summary>
    NoFolder,
    Comparing,

    /// <summary>The author stopped the comparison.</summary>
    Stopped,

    /// <summary>The comparison refused or failed; <see cref="CellViewModel.ProblemTitle"/> says why.</summary>
    Failed,

    /// <summary>The author's folder is not there.</summary>
    FolderGone,

    /// <summary>Every file of the author's is the game's own, so the package would be empty.</summary>
    NothingToPackage,
    Compared,

    /// <summary>The stores do not report the version's game.</summary>
    GameGone,

    /// <summary>A package is installed on the game, whose files would count as the game's.</summary>
    Installed,

    /// <summary>The store or a job is at work on the game.</summary>
    NotReady,

    /// <summary>A test package is in the game, so its files are no baseline for a comparison or a build.</summary>
    TestInstalled,

    /// <summary>An add-on waits for its base on the version, over whose plan it is planned, to be compared.</summary>
    Waiting,

    /// <summary>An add-on's base on the version was not compared, so the add-on is not either.</summary>
    BaseMissing,
}

/// <summary>A row of a project's table: one part over the versions of the game.</summary>
/// <param name="Page">The project's page, which renames the part.</param>
/// <param name="Part">The part's name; empty for the base.</param>
/// <param name="Caption">What the part is, under its name: an add-on as the site offers it, else the base's mode.</param>
/// <param name="Mode">The add-on's mode; empty for the base, whose caption says it.</param>
/// <param name="Cells">The part's cells, in the versions' order.</param>
public sealed record CellRow(ProjectPageViewModel Page, string Part, string Caption, string Mode, ValueList<CellViewModel> Cells)
{
    public bool IsFeature => Part.Length > 0;

    public string Name => IsFeature ? Part : Strings.Creator_PartBase;

    /// <summary>The add-on's name in the row's head, which renames it once the author leaves the box.</summary>
    public string TypedName
    {
        get => Part;
        set => _ = Page.RenamePartAsync(Part, value);
    }
}
