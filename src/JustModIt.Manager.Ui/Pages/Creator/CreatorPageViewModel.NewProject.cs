using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JustModIt.Manager.Core.IO;
using JustModIt.Manager.Creator;
using JustModIt.Manager.Localization;
using JustModIt.Manager.Protocol.Messages;
using JustModIt.Manager.Ui.Pages.Library;

namespace JustModIt.Manager.Ui.Pages.Creator;

public sealed partial class CreatorPageViewModel
{
    /// <summary>Why the folder given cannot make a project, shown under the drop zone; empty while there is nothing to say.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRefusal))]
    public partial string RefusalTitle { get; private set; } = "";

    [ObservableProperty]
    public partial string RefusalText { get; private set; } = "";

    public bool HasRefusal => RefusalTitle.Length > 0;

    /// <summary>Files rather than a folder were dropped.</summary>
    [ObservableProperty]
    public partial bool DroppedFiles { get; private set; }

    [RelayCommand]
    private async Task NewProjectAsync()
    {
        if (await Services.Pickers.PickFolderAsync(Strings.Creator_PickFolderTitle).ConfigureAwait(true) is { } folder)
        {
            await UseFolderAsync(folder).ConfigureAwait(true);
        }
    }

    [RelayCommand]
    private async Task DropAsync(string? folder)
    {
        if (folder is not null)
        {
            await UseFolderAsync(folder).ConfigureAwait(true);
        }
        else
        {
            ClearProblem();
            DroppedFiles = true;
        }
    }

    // A folder in a game's or around one would pass off the game's own files as the author's. With no answer from the agent
    // nothing can be told here, and the comparison still refuses a folder that overlaps the game chosen in the end.
    private async Task UseFolderAsync(string folder)
    {
        ClearProblem();
        var games = (await AgentRequests.AskQuietlyAsync(Agent, new GetDetectedGames()).ConfigureAwait(true) as DetectedGames)?.Games ?? [];
        if (games.FirstOrDefault(game => PathComparison.Overlaps(game.Game.InstallDirectory, folder)) is { } overlapped)
        {
            (RefusalTitle, RefusalText) = (PathComparison.AreSame(overlapped.Game.InstallDirectory, folder)
                ? Strings.Creator_GameFolderTitle
                : Strings.Creator_InsideGameTitle, Strings.Creator_GameFolder);
            return;
        }

        // A project works on a game, so a new one takes it first, and nothing is made when the author cancels.
        using var dialog = new NewProjectDialogViewModel(this, folder);
        if (await Services.Dialogs.ShowAsync(dialog).ConfigureAwait(true) is not { } answer)
        {
            return;
        }

        var project = new CreatorProject
        {
            Id = Guid.NewGuid(),
            CreatedAt = Services.Clock.GetUtcNow(),
            Versions = [.. answer.Versions],
            Parts = [.. answer.Parts],
            Site = answer.Site,
        };
        try
        {
            await Library.Store(project.Id).SaveAsync(project, CancellationToken.None).ConfigureAwait(true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            (ProblemTitle, ProblemText) = (Strings.Creator_SaveFailedTitle, Strings.Creator_ExportFailed);
            return;
        }

        await OpenAsync(project).ConfigureAwait(true);
    }

    // A project with no version opens on the page, which takes one first.
    private async Task OpenAsync(CreatorProject project)
    {
        if (await CloseProjectAsync().ConfigureAwait(true))
        {
            OpenPage = new ProjectPageViewModel(this, project);
            OpenPage.PropertyChanged += OnOpenPageChanged;
            ShowOpenPage();
        }
        // A build the author would not stop keeps its project open, and this one waits among the cards.
        else
        {
            await ShowProjectsAsync().ConfigureAwait(true);
        }
    }
}
