using CommunityToolkit.Mvvm.Input;
using JustModIt.Manager.Core.IO;
using JustModIt.Manager.Creator;
using JustModIt.Manager.Localization;
using JustModIt.Manager.Creator.Projects;
using JustModIt.Manager.Ui.Overlays;
using JustModIt.Manager.Ui.Theme;

namespace JustModIt.Manager.Ui.Pages.Creator;

// The project's add-ons: adding one under a name the site gives or its folder's, renaming and removing it, and the folders a
// part may take.
public sealed partial class ProjectPageViewModel
{
    /// <summary>
    /// Refuses, in the page's callout, a folder in or around a version's game, which would pass off the game's own files as the
    /// author's, or one another part takes.
    /// </summary>
    /// <param name="part">The name of the part that would take the folder.</param>
    /// <param name="folder">The folder.</param>
    /// <returns>Whether the folder is refused.</returns>
    internal bool Refuses(string part, string folder)
    {
        if (Cells.Select(cell => cell.Game?.Game.InstallDirectory).OfType<string>().FirstOrDefault(game => PathComparison.Overlaps(game, folder)) is { } game)
        {
            ShowProblem(PathComparison.AreSame(game, folder) ? Strings.Creator_GameFolderTitle : Strings.Creator_InsideGameTitle, Strings.Creator_GameFolder);
            return true;
        }

        if (Session?.Project.IsTaken(part, folder) == true)
        {
            ShowProblem("", Strings.Creator_FolderTaken);
            return true;
        }

        ClearProblem();
        return false;
    }

    /// <summary>Renames an add-on as the author typed it; a name that is empty or another part's leaves it as it was.</summary>
    /// <param name="part">The add-on's name.</param>
    /// <param name="name">The name typed.</param>
    /// <returns>Completes once the record of the builds, which is kept by the part's name, follows.</returns>
    internal async Task RenamePartAsync(string part, string name)
    {
        name = name.Trim();
        if (Session is not { } session || name == part)
        {
            return;
        }

        if (name.Length == 0 || IsNamed(session.Project, name, besides: part))
        {
            // The row's box shows the name it has again.
            Rows = [.. Rows];
            return;
        }

        // The cells keep their comparisons, so they take the name before the project does.
        foreach (var cell in Cells.Where(cell => cell.Key.Part == part))
        {
            cell.Rename(name);
        }

        session.RenamePart(part, name);
        if (Record is null)
        {
            return;
        }

        try
        {
            await Library.UpdateBuildAsync(ProjectId, record => new([.. (record?.Parts ?? []).Select(built => built.Part == part ? built with { Part = name } : built)]), CancellationToken.None)
                .ConfigureAwait(true);
        }
        // The record keeps the old name, so the add-on's packages go unlisted until its next build.
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }

        await ReadRecordAsync().ConfigureAwait(true);
    }

    // The site's add-ons the project lacks are offered by name, and a folder of the author's names any other one.
    [RelayCommand]
    private async Task AddPartAsync()
    {
        if (Session is not { } session)
        {
            return;
        }

        var named = (Description.Hints?.Features ?? []).Where(feature => !IsNamed(session.Project, feature.Name)).ToList();
        var name = "";
        if (named.Count > 0)
        {
            var answer = await Services.Dialogs.ShowAsync(new ChoiceDialogViewModel(Strings.Creator_AddFeature, "",
            [
                .. named.Select(feature => new DialogChoice(feature.Name, Icons.Package, feature.Name,
                    Text.Format(Strings.Creator_FeatureRow, ("on", feature.IsDefault ? "true" : "false")))),
                new DialogChoice("", Icons.FolderOpen, Strings.Creator_PickFolder, Strings.Creator_FeatureFromFolder),
            ])).ConfigureAwait(true);
            if (answer.Key is not { } key || Session != session)
            {
                return;
            }

            name = key;
        }

        // An add-on the site names may take its folder later, on its cell.
        var folder = await Services.Pickers.PickFolderAsync(name.Length > 0 ? Text.Format(Strings.Creator_PickFeatureName, ("name", name)) : Strings.Creator_PickFolderTitle,
            session.Project.Parts[0].AuthorFolder is { Length: > 0 } start ? start : null).ConfigureAwait(true);
        if (Session == session && (folder is not null || name.Length > 0))
        {
            AddPart(name, folder ?? "");
        }
    }

    [RelayCommand]
    private void DropPart(string? folder)
    {
        if (folder is null)
        {
            ShowProblem(Strings.Creator_DroppedFilesTitle, Strings.Creator_DroppedFiles);
        }
        else
        {
            AddPart("", folder);
        }
    }

    [RelayCommand]
    private async Task RemovePartAsync()
    {
        if (Session is not { } session || Cell is not { IsFeature: true } cell
            || !await Services.Dialogs.ShowAsync(new ConfirmDialogViewModel(Text.Format(Strings.Creator_RemovePartTitle, ("name", cell.Key.Part)),
                Strings.Creator_RemovePartMessage, Strings.Creator_RemovePartConfirm, Icons.Trash, DialogTone.Danger) { ConfirmIcon = Icons.Trash }).ConfigureAwait(true)
            || Session != session)
        {
            return;
        }

        var part = cell.Key.Part;
        IsPanelOpen = false;
        session.Change(project => project with { Parts = project.Parts.RemoveAll(each => each.Name == part) });
    }

    // Cells are told apart by their part's name, and packages take it, so no two parts share one, whatever its letter case.
    private void AddPart(string name, string folder)
    {
        if (Session is not { } session)
        {
            return;
        }

        name = name.Length > 0 ? name : Path.GetFileName(Path.TrimEndingDirectorySeparator(folder)) is { Length: > 0 } leaf ? leaf : folder;
        var unique = name;
        for (var number = 2; IsNamed(session.Project, unique); number++)
        {
            unique = Text.Format(Strings.Creator_PartNumbered, ("name", name), ("number", number));
        }

        if (folder.Length == 0 || !Refuses(unique, folder))
        {
            session.Change(project => project with { Parts = project.Parts.Add(new ProjectPart { Name = unique, AuthorFolder = folder }) });
        }
    }

    private static bool IsNamed(CreatorProject project, string name, string? besides = null) =>
        project.Parts.Any(part => part.Name != besides && string.Equals(part.Name, name, StringComparison.OrdinalIgnoreCase));
}
