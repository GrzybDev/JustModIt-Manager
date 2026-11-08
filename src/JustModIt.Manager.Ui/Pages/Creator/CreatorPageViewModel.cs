using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JustModIt.Manager.Core.IO;
using JustModIt.Manager.Core.Library;
using JustModIt.Manager.Core.Platform;
using JustModIt.Manager.Creator;
using JustModIt.Manager.Creator.Comparison;
using JustModIt.Manager.Creator.Projects;
using JustModIt.Manager.Localization;
using JustModIt.Manager.Protocol.Messages;
using JustModIt.Manager.Protocol.Views;
using JustModIt.Manager.Ui.Connection;
using JustModIt.Manager.Ui.Navigation;
using JustModIt.Manager.Ui.Overlays;
using JustModIt.Manager.Ui.Pages.Library;
using JustModIt.Manager.Ui.Theme;
using Microsoft.Extensions.Logging;

namespace JustModIt.Manager.Ui.Pages.Creator;

/// <summary>The creator's steps, in the order the author takes them.</summary>
public enum CreatorStep
{
    Game,
    Files,
    Review,
    Package,
}

/// <summary>How a step of the creator stands for the author.</summary>
public enum StepState
{
    /// <summary>A step before it is not done yet.</summary>
    Locked,
    Available,
    Current,
    Done,
}

/// <summary>A step in the creator's step bar.</summary>
/// <param name="step">The step.</param>
/// <param name="title">Its name.</param>
public sealed partial class StepItem(CreatorStep step, string title) : ObservableObject
{
    public CreatorStep Step { get; } = step;

    public int Number { get; } = (int)step + 1;

    /// <summary>Its name; the check of an advanced project is named after the steps the author lays out there.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AccessibleName))]
    public partial string Title { get; set; } = title;

    public bool IsLast => Step == CreatorStep.Package;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCurrent), nameof(IsDone), nameof(CanOpen), nameof(AccessibleName))]
    public partial StepState State { get; set; }

    /// <summary>Why the step cannot be opened yet; empty when it can.</summary>
    [ObservableProperty]
    public partial string Tip { get; set; } = "";

    public bool IsCurrent => State == StepState.Current;

    public bool IsDone => State == StepState.Done;

    public bool CanOpen => State is StepState.Available or StepState.Done;

    public string AccessibleName => Text.Format(Strings.Creator_StepName, ("number", Number), ("title", Title), ("state", State.ToString()));
}

/// <summary>A step of the creator as the start explains it.</summary>
/// <param name="Number">Its number.</param>
/// <param name="Title">Its name.</param>
/// <param name="Text">What happens in it.</param>
public sealed record HowItWorksStep(int Number, string Title, string Text);

/// <summary>A project of the library as the start lists it.</summary>
/// <param name="Id">The project's id.</param>
/// <param name="Name">What it is called.</param>
/// <param name="Folder">The author's folder; null while it has none.</param>
/// <param name="Changed">When it last changed.</param>
public sealed record RecentProject(Guid Id, string Name, string? Folder, string Changed);

/// <summary>
/// The package creator: a project the author opens or starts, taken through four steps (the game, the author's files, the
/// check of the operations, the package), which saves itself. Comparing, planning and building run on the thread pool; the
/// window may not close over a running build until the author stops it, or over changes that did not reach the disk until the
/// author lets them go.
/// </summary>
public sealed partial class CreatorPageViewModel : PageViewModel, IDisposable
{
    private readonly ProjectLibrary _library;
    private readonly LibraryAccess _access;
    private ICreatorStepView[] _steps = [];
    private bool _recentImported;

    /// <summary>Creates the page, which starts without a project.</summary>
    /// <param name="services">The window's services.</param>
    /// <param name="fileSystem">Reads the folders and files the creator works with; null for the real file system.</param>
    public CreatorPageViewModel(UiServices services, IFileSystem? fileSystem = null)
    {
        Services = services;
        FileSystem = fileSystem;
        Log = services.Context.Loggers.CreateLogger<CreatorPageViewModel>();
        _library = new ProjectLibrary(services.Context.CreatorDirectory, services.Clock);
        _access = new LibraryAccess(services.Clock);
        Steps =
        [
            new(CreatorStep.Game, Strings.Creator_StepGame),
            new(CreatorStep.Files, Strings.Creator_StepFiles),
            new(CreatorStep.Review, Strings.Creator_StepReview),
            new(CreatorStep.Package, Strings.Creator_StepPackage),
        ];
        Services.Agent.PropertyChanged += OnAgentChanged;
    }

    public UiServices Services { get; }

    /// <summary>Reads the folders and files the creator works with; null for the real file system.</summary>
    public IFileSystem? FileSystem { get; }

    public CreatorOptions Options => Services.Context.Creator;

    /// <summary>Records what goes wrong in the creator, for the diagnostic report.</summary>
    public ILogger Log { get; }

    public IAgent Agent => Services.Agent;

    public override string Title => Strings.Shell_Creator;

    public IReadOnlyList<StepItem> Steps { get; }

    public string StartText => Text.Format(Strings.Creator_StartText, ("extension", Options.PackageExtension));

    public string ProjectHint => Text.Format(Strings.Creator_ProjectHint, ("extension", Options.ProjectExtension));

    public IReadOnlyList<HowItWorksStep> HowItWorks =>
    [
        new(1, Strings.Creator_StepGame, Strings.Creator_HowGame),
        new(2, Strings.Creator_StepFiles, Strings.Creator_HowFiles),
        new(3, Strings.Creator_StepReview, Strings.Creator_HowReview),
        new(4, Strings.Creator_StepPackage, Text.Format(Strings.Creator_HowPackage, ("extension", Options.PackageExtension))),
    ];

    public ObservableCollection<RecentProject> Recent { get; } = [];

    public bool HasRecent => Recent.Count > 0;

    /// <summary>The project being worked on; null shows the start.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasProject))]
    public partial CreatorSession? Session { get; private set; }

    public bool HasProject => Session is not null;

    /// <summary>The step shown.</summary>
    [ObservableProperty]
    public partial CreatorStep Step { get; private set; }

    /// <summary>The view model of the step shown.</summary>
    [ObservableProperty]
    public partial ICreatorStepView? Content { get; private set; }

    /// <summary>The last step change went back, which reverses its transition.</summary>
    [ObservableProperty]
    public partial bool IsGoingBack { get; private set; }

    [ObservableProperty]
    public partial string ProjectName { get; private set; } = "";

    [ObservableProperty]
    public partial bool IsAdvanced { get; private set; }

    /// <summary>Why importing, exporting or opening a project failed; empty while nothing did.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasProblem))]
    public partial string ProblemTitle { get; private set; } = "";

    [ObservableProperty]
    public partial string ProblemText { get; private set; } = "";

    public bool HasProblem => ProblemTitle.Length > 0;

    /// <summary>The kind of the app's project files, for the dialogs.</summary>
    public FileKind ProjectKind => new(Strings.Creator_ProjectFiles, Options.ProjectExtension);

    /// <summary>The project's game as the agent reported it, once the author chose one.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GameStoreName))]
    public partial DetectedGameEntry? Game { get; internal set; }

    public string GameStoreName => Game is { } game ? GameWording.StoreName(game.Key.Store) : "";

    /// <summary>Another store reports a game of the same name as <see cref="Game"/>, whose package would take the same name.</summary>
    internal bool IsGameOnOtherStores { get; set; }

    public override void OnShown()
    {
        _ = ImportRecentAsync();
        if (Session is null)
        {
            _ = ShowProjectsAsync();
        }

        Content?.OnShown();
    }

    public override async Task<bool> CanCloseAsync() => await CanCloseProjectAsync().ConfigureAwait(true);

    public void Dispose()
    {
        Services.Agent.PropertyChanged -= OnAgentChanged;
        Release();
        _library.Dispose();
    }

    /// <summary>Shows a step, when it can be opened.</summary>
    /// <param name="step">The step to show.</param>
    internal void GoTo(CreatorStep step)
    {
        if (Session is null || (step != Step && !Steps[(int)step].CanOpen))
        {
            return;
        }

        IsGoingBack = step < Step;
        Step = step;
        Content = _steps[(int)step];
        Content.OnShown();
        RefreshSteps();
    }

    /// <summary>Takes back a problem shown, once the author acts again.</summary>
    [RelayCommand]
    internal void ClearProblem() => ProblemTitle = ProblemText = "";

    /// <summary>Says why importing, exporting or opening a project failed.</summary>
    /// <param name="title">What failed.</param>
    /// <param name="text">Why, and what to do.</param>
    internal void ShowProblem(string title, string text) => (ProblemTitle, ProblemText) = (title, text);

    /// <summary>Closes the project, once nothing keeps it open, so the creator shows its start again.</summary>
    internal async Task FinishAsync()
    {
        if (!await CanCloseProjectAsync().ConfigureAwait(true))
        {
            return;
        }

        Release();
        ClearProblem();
        (Session, Content, Game, Step) = (null, null, null, CreatorStep.Game);
        await ShowProjectsAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task NewProjectAsync()
    {
        if (await CanCloseProjectAsync().ConfigureAwait(true))
        {
            Start(new CreatorProject { Id = Guid.NewGuid(), CreatedAt = Services.Clock.GetUtcNow() });
        }
    }

    [RelayCommand]
    private async Task ImportProjectAsync()
    {
        var path = await Services.Pickers.PickFileAsync(Strings.Creator_ImportTitle, ProjectKind, Session is { } session ? FolderOf(session) : null)
            .ConfigureAwait(true);
        if (path is null)
        {
            return;
        }

        ClearProblem();
        if (!await CanCloseProjectAsync().ConfigureAwait(true))
        {
            return;
        }

        try
        {
            Start(await _library.ImportAsync(path, _access, CancellationToken.None).ConfigureAwait(true));
        }
        catch (Exception exception) when (exception is ProjectLoadException or TimeoutException)
        {
            ShowOpenFailed(Strings.Creator_OpenFailedTitle, (exception as ProjectLoadException)?.Error ?? ProjectLoadError.Unreadable);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            ShowProblem(Strings.Creator_OpenFailedTitle, Strings.Creator_ExportFailed);
        }
    }

    [RelayCommand]
    private async Task ExportProjectAsync()
    {
        var session = Session!;
        ClearProblem();
        var path = await Services.Pickers.PickSaveFileAsync(Strings.Creator_ExportTitle,
            FileNames.Suggest(session.Version?.Game.Name, Strings.Creator_ProjectFallbackName, Options.ProjectExtension), ProjectKind,
            FolderOf(session)).ConfigureAwait(true);
        if (path is null)
        {
            return;
        }

        try
        {
            await ProjectLibrary.ExportAsync(session.Project, path, _access, CancellationToken.None)
                .ConfigureAwait(true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or TimeoutException)
        {
            ShowProblem(Strings.Creator_SaveFailedTitle, Strings.Creator_ExportFailed);
        }
    }

    [RelayCommand]
    private async Task OpenRecentAsync(RecentProject recent)
    {
        ClearProblem();
        if (!await CanCloseProjectAsync().ConfigureAwait(true))
        {
            return;
        }

        if (await _library.Store(recent.Id).LoadAsync(CancellationToken.None).ConfigureAwait(true) is { } project)
        {
            Start(project);
        }
        else
        {
            ShowOpenFailed(Strings.Creator_ProjectOpenFailedTitle, ProjectLoadError.Unreadable);
        }
    }

    [RelayCommand]
    private async Task OpenStepAsync(StepItem step)
    {
        if (step.Step != Step && Content is { } current && !await current.CanLeaveAsync().ConfigureAwait(true))
        {
            return;
        }

        GoTo(step.Step);
    }

    private void ShowOpenFailed(string title, ProjectLoadError error) => ShowProblem(title,
        Text.Format(Strings.Creator_OpenFailed, ("reason", error.ToString()), ("product", Services.Context.ProductName)));

    private void Start(CreatorProject project)
    {
        Release();
        ClearProblem();
        Game = null;
        var session = new CreatorSession(Options, project, _library.Store(project.Id), Log);
        Session = session;
        session.PropertyChanged += OnSessionChanged;
        var game = new GameStepViewModel(this, session);
        var files = new FilesStepViewModel(this, session);
        _steps = [game, files, new ReviewViewModel(this, session), new PackageStepViewModel(this, session)];
        Step = CreatorStep.Game;
        Content = null;
        ShowSession();
        RefreshSteps();
        GoTo(CreatorStep.Game);
        _ = ResumeAsync(session, game);
    }

    // A project keeps only what a comparison cannot tell, so an opened project takes its game as the stores report it now,
    // and its files step compares the folders again.
    private async Task ResumeAsync(CreatorSession session, GameStepViewModel game)
    {
        await game.LoadAsync().ConfigureAwait(true);
        if (Session != session || session.Version?.Game is not { } chosen)
        {
            return;
        }

        if (game.Find(chosen.Key) is not { } entry)
        {
            ShowProblem(Strings.Creator_GameGoneTitle, Text.Format(Strings.Creator_GameGone, ("game", chosen.Name)));
            return;
        }

        game.Choose(entry);
        GoTo(CreatorStep.Files);
    }

    private async Task<bool> CanCloseProjectAsync()
    {
        foreach (var step in _steps)
        {
            if (!await step.CanLeaveAsync().ConfigureAwait(true))
            {
                return false;
            }
        }

        // Only a save that failed can lose anything now: a comparison writes nothing and starts over at the next open, and it is
        // not stopped, since the close may still be refused after this.
        return Session is not { } session || await session.FlushAsync().ConfigureAwait(true)
            || await Services.Dialogs.ShowAsync(new ConfirmDialogViewModel(Strings.Creator_SaveFailedTitle, Strings.Creator_CloseUnsaved,
                Strings.Creator_CloseUnsavedConfirm, Icons.CircleX, DialogTone.Danger) { ConfirmIcon = Icons.CircleX })
                    .ConfigureAwait(true);
    }

    private void Release()
    {
        if (Session is { } session)
        {
            session.PropertyChanged -= OnSessionChanged;
            _ = session.DisposeAsync().AsTask();
        }

        foreach (var step in _steps)
        {
            step.Dispose();
        }

        _steps = [];
    }

    private void OnSessionChanged(object? sender, PropertyChangedEventArgs e)
    {
        ShowSession();
        RefreshSteps();
    }

    partial void OnGameChanged(DetectedGameEntry? value)
    {
        RefreshSteps();
        _ = ReadKeptPathsAsync();
    }

    private void ShowSession()
    {
        if (Session is not { } session)
        {
            return;
        }

        ProjectName = NameOf(session.Project);
        IsAdvanced = session.Part.Mode == CreatorMode.Advanced;
        Steps[(int)CreatorStep.Review].Title = IsAdvanced ? Strings.Creator_StepSteps : Strings.Creator_StepReview;
    }

    private void RefreshSteps()
    {
        if (Session is not { } session)
        {
            return;
        }

        var reached = Game is null ? CreatorStep.Game
            : session.Comparison is null || HasNothingToPackage(session) ? CreatorStep.Files
            : !session.IsConfirmed ? CreatorStep.Review
            : CreatorStep.Package;
        foreach (var item in Steps)
        {
            item.State = item.Step == Step ? StepState.Current
                : item.Step > reached ? StepState.Locked
                : item.Step < Step ? StepState.Done
                : StepState.Available;
            item.Tip = item.State == StepState.Locked
                ? Text.Format(Strings.Creator_StepLocked, ("step", Steps[(int)reached].Title))
                : "";
        }
    }

    /// <summary>Opens the files step and compares the folders again.</summary>
    /// <returns>Completes once the comparison ended, was cancelled or refused.</returns>
    internal Task CompareAgainAsync()
    {
        GoTo(CreatorStep.Files);
        return Content is FilesStepViewModel files ? files.CompareAsync() : Task.CompletedTask;
    }

    /// <summary>Whether the author's files only repeat the game's, so a simple project has nothing to package.</summary>
    /// <param name="session">The project with its comparison.</param>
    internal static bool HasNothingToPackage(CreatorSession session) =>
        session.Part.Mode == CreatorMode.Simple && session.Part.ExtraSteps.Count == 0
        && session.Comparison?.Files.All(file => file.Difference == FileDifference.Identical) == true;

    private void OnAgentChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(IAgent.State))
        {
            _ = ImportRecentAsync();
        }
    }

    // Once per window, so what could not come over yet is tried again at the next open.
    private async Task ImportRecentAsync()
    {
        var paths = Agent.State.Settings.RecentCreatorProjects;
        if (_recentImported || paths.Count == 0)
        {
            return;
        }

        _recentImported = true;
        var import = await _library.ImportRecentAsync(paths, _access, CancellationToken.None).ConfigureAwait(true);
        if (import.Leaving.Count > 0)
        {
            await AgentRequests.AskQuietlyAsync(Agent, new ForgetRecentCreatorProjects(import.Leaving))
                .ConfigureAwait(true);
        }

        List<string> told = [];
        if (import.Skipped.Count > 0)
        {
            told.Add(Text.Format(Strings.Creator_ImportSkipped, ("count", import.Skipped.Count),
                ("files", string.Join(", ", import.Skipped.Select(Path.GetFileName)))));
        }

        if (import.Waiting.Count > 0)
        {
            told.Add(Text.Format(Strings.Creator_ImportWaiting, ("count", import.Waiting.Count)));
        }

        if (told.Count > 0)
        {
            ShowProblem(Strings.Creator_OpenFailedTitle, string.Join(' ', told));
        }

        await ShowProjectsAsync().ConfigureAwait(true);
    }

    private async Task ShowProjectsAsync()
    {
        ProjectListing listing;
        try
        {
            listing = await _library.ListAsync(_access, CancellationToken.None).ConfigureAwait(true);
        }
        // What the list shows stays, and the next look at the page reads it again.
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or TimeoutException)
        {
            return;
        }

        Recent.Clear();
        foreach (var (project, changed) in listing.Projects)
        {
            Recent.Add(new RecentProject(project.Id, NameOf(project), project.Parts[0].AuthorFolder is { Length: > 0 } folder ? folder : null,
                Text.Format(Strings.Creator_RecentChanged, ("when", Formats.RelativeTime(changed, Services.Clock)))));
        }

        OnPropertyChanged(nameof(HasRecent));
    }

    /// <summary>
    /// Reads again the game's paths that the agent keeps for an undone test, of the game in its folder as the store reports
    /// it now; a lost connection keeps those read before.
    /// </summary>
    internal async Task ReadKeptPathsAsync()
    {
        if (Session is not { } session || Game is not { } game)
        {
            return;
        }

        if (await AgentRequests.AskQuietlyAsync(Agent, new GetLeftoverBackups()).ConfigureAwait(true) is LeftoverBackups found && Session == session
            && Game?.Key == game.Key)
        {
            await session.UseKeptPathsAsync(found.Backups
                .Where(backup => backup.Game == game.Key && backup.Kept is not null
                    && PathComparison.AreSame(backup.GameDirectory, game.Game.InstallDirectory))
                .SelectMany(backup => backup.Kept!.Paths)
                .Select(path => session.Comparison?.Game.Layout.ToPackage(path) ?? path)).ConfigureAwait(true);
        }
    }

    private static string NameOf(CreatorProject project) =>
        project.Name.Length > 0 ? project.Name
        : project.Versions.Count > 0 ? project.Versions[0].Game.Name
        : Strings.Creator_New;

    private static string? FolderOf(CreatorSession session) =>
        session.Part.AuthorFolder.Length > 0 ? Path.GetDirectoryName(session.Part.AuthorFolder) : null;
}

/// <summary>A step of the creator as the page shows it.</summary>
public interface ICreatorStepView : IDisposable
{
    /// <summary>Called when the step is shown, also when it is gone back to.</summary>
    void OnShown();

    /// <summary>Whether the project may be closed now; a step with work under way asks first.</summary>
    Task<bool> CanLeaveAsync() => Task.FromResult(true);
}
