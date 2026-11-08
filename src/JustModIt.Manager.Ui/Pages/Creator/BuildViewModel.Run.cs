using CommunityToolkit.Mvvm.ComponentModel;
using JustModIt.Manager.Creator.Building;
using JustModIt.Manager.Localization;
using JustModIt.Manager.Ui.Pages.Library;

namespace JustModIt.Manager.Ui.Pages.Creator;

/// <summary>Where a package of the run under way stands.</summary>
public enum RunRowState
{
    Waiting,
    Building,
    Finished,
}

/// <summary>A package of the run under way, as the bar lists it.</summary>
/// <param name="name">Its file's name.</param>
/// <param name="details">Its part and the stores of the versions it serves.</param>
public sealed partial class RunRow(string name, string details) : ObservableObject
{
    public string Name { get; } = name;

    public string Details { get; } = details;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsWaiting), nameof(IsBuilding), nameof(IsFinished))]
    public partial RunRowState State { get; internal set; }

    public bool IsWaiting => State == RunRowState.Waiting;

    public bool IsBuilding => State == RunRowState.Building;

    public bool IsFinished => State == RunRowState.Finished;

    /// <summary>The size of a finished package, or how far the one being built has got; empty for one that waits.</summary>
    [ObservableProperty]
    public partial string Amount { get; internal set; } = "";
}

// How far the run has got, which the bar shows while it builds: the package it is at and each package of the run.
public sealed partial class BuildViewModel
{
    /// <summary>Which package of the run is being built, or that one is, for a run of one.</summary>
    [ObservableProperty]
    public partial string Heading { get; private set; } = "";

    /// <summary>The run's packages, once it knows them.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRunRows), nameof(ShowsFile))]
    public partial IReadOnlyList<RunRow> RunRows { get; private set; } = [];

    public bool HasRunRows => RunRows.Count > 0;

    /// <summary>The bar has room to name the file being worked on: the run has one package.</summary>
    public bool ShowsFile => RunRows.Count == 1;

    /// <summary>The row of the package being built; null while the run prepares.</summary>
    [ObservableProperty]
    public partial RunRow? CurrentRow { get; private set; }

    /// <summary>What the package being built is doing, its file and its bytes.</summary>
    [ObservableProperty]
    public partial string Stage { get; private set; } = "";

    [ObservableProperty]
    public partial string Percent { get; private set; } = "";

    [ObservableProperty]
    public partial double Progress { get; private set; }

    /// <summary>The file being worked on, by its path in the game; empty while there is none.</summary>
    [ObservableProperty]
    public partial string FileLine { get; private set; } = "";

    private void Show(ProjectBuildProgress run)
    {
        if (State != PackageState.Building)
        {
            return;
        }

        var progress = run.Build;
        Heading = run.PackageCount > 1 ? Text.Format(Strings.Creator_BuildingOf, ("number", run.Package), ("count", run.PackageCount)) : Strings.Creator_BuildingTitle;
        Progress = progress.BytesTotal > 0 ? 100.0 * progress.BytesDone / progress.BytesTotal : 0;
        Percent = Formats.Percent(progress.BytesDone, progress.BytesTotal);
        Stage = GameWording.Join(
        [
            Text.Format(Strings.Creator_BuildStage, ("stage", progress.Stage.ToString())),
            progress.FileNumber > 0 ? Text.Format(Strings.Creator_FileOf, ("number", progress.FileNumber), ("count", progress.FileCount)) : "",
            progress.BytesTotal > 0 ? Text.Format(Strings.Creator_BytesOf, ("done", Formats.FileSize(progress.BytesDone)),
                ("total", Formats.FileSize(progress.BytesTotal))) : "",
        ]);
        FileLine = progress.FileNumber > 0 ? progress.CurrentFile ?? "" : "";
        if (RunRows.Count != run.Packages.Count)
        {
            RunRows = [.. run.Packages.Select(package => new RunRow(package.Name, Details(package.Part, package.Versions)))];
        }

        foreach (var (index, (row, package)) in RunRows.Zip(run.Packages).Index())
        {
            var building = index + 1 == run.Package;
            row.State = package.Size is not null ? RunRowState.Finished : building ? RunRowState.Building : RunRowState.Waiting;
            row.Amount = package.Size is { } size ? Formats.FileSize(size) : building ? Percent : "";
        }

        CurrentRow = RunRows.ElementAtOrDefault(run.Package - 1);
    }
}
