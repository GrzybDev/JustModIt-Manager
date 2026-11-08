using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JustModIt.Manager.Core.Library;
using JustModIt.Manager.Core.Platform;
using JustModIt.Manager.Creator;
using JustModIt.Manager.Creator.Projects;
using JustModIt.Manager.Localization;
using JustModIt.Manager.Protocol.Messages;
using JustModIt.Manager.Ui.Connection;
using JustModIt.Manager.Ui.Navigation;
using JustModIt.Manager.Ui.Overlays;
using JustModIt.Manager.Ui.Pages.Library;
using JustModIt.Manager.Ui.Theme;
using Microsoft.Extensions.Logging;

namespace JustModIt.Manager.Ui.Pages.Creator;

/// <summary>
/// The package creator's library of projects, whose cards open a project, and the owner of the one open project: it answers the
/// window's close for it, so a project's page gone from the way back cannot escape the question.
/// </summary>
public sealed partial class CreatorPageViewModel : PageViewModel, IDisposable
{
    private const string ExportChoice = "export";
    private const string DuplicateChoice = "duplicate";
    private const string RemoveChoice = "remove";

    /// <summary>Creates the page, which starts without an open project.</summary>
    /// <param name="services">The window's services.</param>
    /// <param name="fileSystem">Reads the folders and files the creator works with; null for the real file system.</param>
    public CreatorPageViewModel(UiServices services, IFileSystem? fileSystem = null)
    {
        Services = services;
        FileSystem = fileSystem;
        Log = services.Context.Loggers.CreateLogger<CreatorPageViewModel>();
        Library = new ProjectLibrary(services.Context.CreatorDirectory, services.Clock);
        Access = new LibraryAccess(services.Clock);
        Services.Agent.PropertyChanged += OnAgentChanged;
    }

    public UiServices Services { get; }

    /// <summary>Reads the folders and files the creator works with; null for the real file system.</summary>
    public IFileSystem? FileSystem { get; }

    public CreatorOptions Options => Services.Context.Creator;

    /// <summary>Records what goes wrong in the creator, for the diagnostic report.</summary>
    public ILogger Log { get; }

    public IAgent Agent => Services.Agent;

    /// <summary>Where the projects are kept.</summary>
    public ProjectLibrary Library { get; }

    /// <summary>Reads the library and the folders its cards name, each read limited in time, one per drive.</summary>
    public LibraryAccess Access { get; }

    public override string Title => Strings.Shell_Creator;

    public string Subtitle => !IsListing && _grid.Items.Count == 0 ? Strings.Creator_Subtitle : Strings.Creator_LibrarySubtitle;

    /// <summary>The project open behind its card, which goes on working whichever page the window shows; null while there is none.</summary>
    [ObservableProperty]
    public partial ProjectPageViewModel? OpenPage { get; private set; }

    /// <summary>Why importing, exporting, opening or removing a project failed; empty while nothing did.</summary>
    [ObservableProperty]
    public partial string ProblemTitle { get; private set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasProblem))]
    public partial string ProblemText { get; private set; } = "";

    public bool HasProblem => ProblemText.Length > 0;

    /// <summary>The kind of the app's project files, for the dialogs.</summary>
    public FileKind ProjectKind => new(Strings.Creator_ProjectFiles, Options.ProjectExtension);

    public override void OnShown()
    {
        _ = ImportRecentAsync();
        _ = ShowProjectsAsync();
        _ = ReadDetectedAsync();
    }

    public override Task<bool> CanCloseAsync() => OpenPage?.LeaveAsync() ?? Task.FromResult(true);

    public void Dispose()
    {
        Services.Agent.PropertyChanged -= OnAgentChanged;
        Library.Dispose();
    }

    /// <summary>Closes the open project, once nothing keeps it open.</summary>
    /// <returns>False when the author kept it open.</returns>
    internal async Task<bool> CloseProjectAsync()
    {
        if (OpenPage is not { } page)
        {
            return true;
        }

        if (!await page.CloseAsync().ConfigureAwait(true))
        {
            return false;
        }

        page.PropertyChanged -= OnOpenPageChanged;
        OpenPage = null;
        return true;
    }

    /// <summary>Takes back a problem shown, once the author acts again.</summary>
    [RelayCommand]
    internal void ClearProblem() => ProblemTitle = ProblemText = "";

    [RelayCommand]
    private async Task NewProjectAsync()
    {
        ClearProblem();
        if (await CloseProjectAsync().ConfigureAwait(true))
        {
            Open(new CreatorProject { Id = Guid.NewGuid(), CreatedAt = Services.Clock.GetUtcNow() });
        }
    }

    [RelayCommand]
    private async Task ImportProjectAsync()
    {
        var path = await Services.Pickers.PickFileAsync(Strings.Creator_ImportTitle, ProjectKind).ConfigureAwait(true);
        if (path is null)
        {
            return;
        }

        ClearProblem();
        if (!await CloseProjectAsync().ConfigureAwait(true))
        {
            return;
        }

        try
        {
            Open(await Library.ImportAsync(path, Access, CancellationToken.None).ConfigureAwait(true));
        }
        catch (Exception exception) when (exception is ProjectLoadException or TimeoutException)
        {
            ShowOpenFailed(Strings.Creator_OpenFailedTitle, (exception as ProjectLoadException)?.Error ?? ProjectLoadError.Unreadable);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            (ProblemTitle, ProblemText) = (Strings.Creator_OpenFailedTitle, Strings.Creator_ExportFailed);
        }
    }

    [RelayCommand]
    private async Task OpenCardAsync(ProjectCard card)
    {
        ClearProblem();
        if (OpenPage?.ProjectId == card.Id)
        {
            ShowOpenPage();
        }
        else if (await CloseProjectAsync().ConfigureAwait(true) && await ProjectOfAsync(card).ConfigureAwait(true) is { } project)
        {
            Open(project);
        }
    }

    [RelayCommand]
    private async Task CardMenuAsync(ProjectCard card)
    {
        ClearProblem();
        var answer = await Services.Dialogs.ShowAsync(new ChoiceDialogViewModel(card.MoreName, "",
        [
            new DialogChoice(ExportChoice, Icons.Upload, Strings.Creator_Export, Strings.Creator_ExportText),
            new DialogChoice(DuplicateChoice, Icons.Copy, Strings.Creator_Duplicate, Strings.Creator_DuplicateText),
            new DialogChoice(RemoveChoice, Icons.Trash, Strings.Creator_Remove, Strings.Creator_RemoveText, IsDanger: true),
        ])).ConfigureAwait(true);
        await (answer.Key switch
        {
            ExportChoice => ExportAsync(card),
            DuplicateChoice => DuplicateAsync(card),
            RemoveChoice => RemoveAsync(card),
            _ => Task.CompletedTask,
        }).ConfigureAwait(true);
    }

    [RelayCommand]
    private void DismissNotice(ImportNotice notice) => Notices.Remove(notice);

    [RelayCommand]
    private void Reveal(UnreadableProject project) => Services.Links.OpenFolder(project.Folder);

    private async Task ExportAsync(ProjectCard card)
    {
        if (await ProjectOfAsync(card).ConfigureAwait(true) is not { } project)
        {
            return;
        }

        var path = await Services.Pickers.PickSaveFileAsync(Strings.Creator_ExportTitle,
            FileNames.Suggest(ProjectCard.NameOf(project), Strings.Creator_ProjectFallbackName, Options.ProjectExtension), ProjectKind,
            project.Parts[0].AuthorFolder.Length > 0 ? Path.GetDirectoryName(project.Parts[0].AuthorFolder) : null)
                .ConfigureAwait(true);
        if (path is null)
        {
            return;
        }

        try
        {
            await ProjectLibrary.ExportAsync(project, path, Access, CancellationToken.None).ConfigureAwait(true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or TimeoutException)
        {
            (ProblemTitle, ProblemText) = (Strings.Creator_SaveFailedTitle, Strings.Creator_ExportFailed);
        }
    }

    private async Task DuplicateAsync(ProjectCard card)
    {
        if (await ProjectOfAsync(card).ConfigureAwait(true) is not { } project || !await CloseProjectAsync().ConfigureAwait(true))
        {
            return;
        }

        try
        {
            Open(await Library.DuplicateAsync(project, Text.Format(Strings.Creator_CopyName, ("name", ProjectCard.NameOf(project))), CancellationToken.None).ConfigureAwait(true));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            (ProblemTitle, ProblemText) = (Strings.Creator_SaveFailedTitle, Strings.Creator_ExportFailed);
        }
    }

    private async Task RemoveAsync(ProjectCard card)
    {
        if (!await Services.Dialogs.ShowAsync(new ConfirmDialogViewModel(Strings.Creator_RemoveTitle,
                Text.Format(Strings.Creator_RemoveMessage, ("name", card.Name)), Strings.Creator_Remove, Icons.Trash, DialogTone.Danger)).ConfigureAwait(true))
        {
            return;
        }

        // A pending save or a running build's record would bring the folder back, so a refused question ends the removal.
        if (OpenPage?.ProjectId == card.Id && !await CloseProjectAsync().ConfigureAwait(true))
        {
            return;
        }

        try
        {
            await Library.RemoveAsync(card.Id, CancellationToken.None).ConfigureAwait(true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            ProblemText = Strings.Creator_RemoveFailed;
        }

        _ = ShowProjectsAsync();
    }

    // The open project is ahead of its file by the changes of the last moment.
    private async Task<CreatorProject?> ProjectOfAsync(ProjectCard card)
    {
        var project = OpenPage is { Session: { } session } page && page.ProjectId == card.Id
            ? session.Project
            : await Library.Store(card.Id).LoadAsync(CancellationToken.None).ConfigureAwait(true);
        if (project is null)
        {
            ShowOpenFailed(Strings.Creator_ProjectOpenFailedTitle, ProjectLoadError.Unreadable);
        }

        return project;
    }

    private void Open(CreatorProject project)
    {
        OpenPage = new ProjectPageViewModel(this, project);
        OpenPage.PropertyChanged += OnOpenPageChanged;
        ShowOpenPage();
    }

    // Reading a project takes a moment, in which the author may have gone to another page, and its way back is the library.
    private void ShowOpenPage() => Services.Navigator.Open([new CreatorRoute(), new CreatorProjectRoute(OpenPage!.ProjectId)]);

    private void ShowOpenFailed(string title, ProjectLoadError error) => (ProblemTitle, ProblemText) = (title,
        Text.Format(Strings.Creator_OpenFailed, ("reason", error.ToString()), ("product", Services.Context.ProductName)));

    private void OnAgentChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(IAgent.State))
        {
            _ = ImportRecentAsync();
            _ = ReadDetectedAsync();
            ShowStates();
        }
    }

    private void OnOpenPageChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ProjectPageViewModel.Work))
        {
            // A build that ended has written the package's row, which the card names.
            _ = Cards.FirstOrDefault(card => card.Id == ((ProjectPageViewModel)sender!).ProjectId)?.RefreshAsync();
            ShowStates();
        }
    }
}
