using System.ComponentModel;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Creator;
using JustModIt.Manager.Localization;
using JustModIt.Manager.Protocol.Views;
using JustModIt.Manager.Ui.Connection;

namespace JustModIt.Manager.Ui.Pages.Creator;

// What the page does when the session, the work under way, the agent or the chosen tab changes.
public sealed partial class ProjectPageViewModel
{
    private void OnSessionChanged(object? sender, PropertyChangedEventArgs e)
    {
        var session = Session!;
        if (e.PropertyName == nameof(CreatorSession.SaveFailed))
        {
            if (session.SaveFailed)
            {
                (ProblemTitle, ProblemText) = ("", Strings.Creator_SaveFailed);
            }
            // The next change saves again, and a save that works takes the failure back.
            else if (ProblemText == Strings.Creator_SaveFailed)
            {
                ClearProblem();
            }

            return;
        }

        if (e.PropertyName is nameof(CreatorSession.Project) or nameof(CreatorSession.Selected))
        {
            ShowGames(session);
        }

        var versions = Folders(session.Project);
        if (!versions.SequenceEqual(_versions))
        {
            // A package and its test belong to the copy of the game they were made for: another copy, e.g. from another store, has
            // other files, so its package is built anew, and a test install never takes the other copy's package.
            var changed = _versions.Except(versions).Select(version => version.Key).ToList();
            _versions = versions;
            Test.Forget(changed);
            Build.Forget(changed);
            // What another version holds of the folder is no work of the author's, so every cell compares again with the versions as they are.
            foreach (var cell in Cells)
            {
                _ = cell.CompareAsync();
            }
        }

        // The files compared anew are what the failed build asked for, so its failure no longer stands in the way.
        if (e.PropertyName == nameof(CreatorSession.Comparison))
        {
            Build.FilesCompared();
        }

        if (e.PropertyName is nameof(CreatorSession.Review) or nameof(CreatorSession.Project))
        {
            Description.Show();
            Build.Show();
        }

        ShowSession();
        ShowBar();
    }

    private void OnWorkChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(CellViewModel.State) or nameof(BuildViewModel.State))
        {
            OnPropertyChanged(nameof(Work));
            CompareOutdated();
        }

        // The last build's packages stand while no comparison runs or found a change since them.
        if (sender is CellViewModel && e.PropertyName is nameof(CellViewModel.State) or nameof(CellViewModel.HasChanges))
        {
            Build.ShowLast();
        }

        if (sender is CellViewModel { IsFeature: false } first && e.PropertyName == nameof(CellViewModel.State))
        {
            foreach (var cell in Cells.Where(cell => cell.IsFeature && cell.Key.Version == first.Key.Version))
            {
                cell.BaseChanged();
            }
        }

        // The other versions' cells of a part say what they share with its first version, which only holds while that one is compared.
        if (sender is CellViewModel told && e.PropertyName == nameof(CellViewModel.State) && told == Cells.FirstOrDefault(cell => cell.Key.Part == told.Key.Part))
        {
            foreach (var cell in Cells.Where(cell => cell != told && cell.Key.Part == told.Key.Part))
            {
                cell.ShowBrief();
            }
        }

        // The site names the add-ons, which the rows' heads say, and its last release, which the head says.
        if (sender == Description && e.PropertyName == nameof(PackageDescriptionViewModel.Hints) && Session is { } session)
        {
            ShowCells(session);
            ShowSession();
        }

        if (sender == Build && e.PropertyName == nameof(BuildViewModel.State) && !Build.IsBuilding)
        {
            foreach (var cell in Cells)
            {
                cell.CheckIfAsked();
            }
        }

        if (e.PropertyName is nameof(CellViewModel.State) or nameof(BuildViewModel.State) or nameof(TestInstallViewModel.State)
            or nameof(BuildViewModel.BuildFindings) or nameof(BuildViewModel.NeedsComparison))
        {
            ShowBar();
        }
    }

    private void OnAgentChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(IAgent.State) || Session is not { } session)
        {
            return;
        }

        // A game the stores did not report may come with their next look.
        if (Cells.Any(cell => cell.Game is null) && _resuming is not { IsCompleted: false } && _creator.Agent.State.Library.ScannedAt != _resumedAt)
        {
            _resuming = ResumeAsync();
        }

        ShowGames(session);
        ShowBar();
    }

    partial void OnGameChanged(DetectedGameEntry? value)
    {
        ShowSession();
        Description.Show();
        Build.Show();
        _ = Description.HintAsync();
        _ = ReadKeptPathsAsync();
    }

    partial void OnTabChanged(ProjectTab oldValue, ProjectTab newValue)
    {
        IsGoingBack = newValue < oldValue;
        // The check does not follow plans made while the description shows.
        if (oldValue == ProjectTab.Description)
        {
            Review.Show();
        }
    }

    // The cells, and each tells again what keeps its version's game from being compared.
    private void ShowGames(CreatorSession session)
    {
        ShowCells(session);
        foreach (var cell in Cells)
        {
            cell.ShowGame();
        }

        CompareOutdated();
    }

    // What another version holds of a folder is no work of the author's, so a version that came back, or a folder no longer shared,
    // makes the cells of that folder compare again.
    private void CompareOutdated()
    {
        foreach (var cell in Cells)
        {
            cell.CompareIfOutdated();
        }
    }

    // What a package and its test are made for: each version's game and the folder it lies in.
    private static List<(GameKey Key, string Folder)> Folders(CreatorProject project) =>
        [.. project.Versions.Select(version => (version.Game.Key, version.GameFolder))];
}
