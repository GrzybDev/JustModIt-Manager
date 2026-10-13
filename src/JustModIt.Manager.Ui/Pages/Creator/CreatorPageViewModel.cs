using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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

/// <summary>A project file opened or saved lately, with when it last changed, read off the UI thread.</summary>
/// <param name="path">The project's file.</param>
public sealed partial class RecentProject(string path) : ObservableObject
{
    public string Path { get; } = path;

    public string Name { get; } = System.IO.Path.GetFileNameWithoutExtension(path);

    [ObservableProperty]
    public partial string Changed { get; set; } = "";
}

/// <summary>
/// The package creator: a project the author opens or starts, taken through four steps (the game, the author's files,
/// the check of the operations, the package), and saved to a project file. Comparing, planning and building run on the
/// thread pool; the window may not close over unsaved work until the author saves or discards it.
/// </summary>
public sealed partial class CreatorPageViewModel : PageViewModel, IDisposable
{
    private const string SaveChoice = "save";
    private const string DiscardChoice = "discard";

    private ICreatorStepView[] _steps = [];

    /// <summary>Creates the page, which starts without a project.</summary>
    /// <param name="services">The window's services.</param>
    /// <param name="fileSystem">Reads the folders and files the creator works with; null for the real file system.</param>
    public CreatorPageViewModel(UiServices services, IFileSystem? fileSystem = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        Services = services;
        FileSystem = fileSystem;
        Steps =
        [
            new(CreatorStep.Game, Strings.Creator_StepGame),
            new(CreatorStep.Files, Strings.Creator_StepFiles),
            new(CreatorStep.Review, Strings.Creator_StepReview),
            new(CreatorStep.Package, Strings.Creator_StepPackage),
        ];
        Services.Agent.PropertyChanged += OnAgentChanged;
        ShowRecent();
    }

    public UiServices Services { get; }

    /// <summary>Reads the folders and files the creator works with; null for the real file system.</summary>
    public IFileSystem? FileSystem { get; }

    public CreatorOptions Options => Services.Context.Creator;

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

    /// <summary>Whether the project is saved, as the header says it.</summary>
    [ObservableProperty]
    public partial string SaveState { get; private set; } = "";

    [ObservableProperty]
    public partial bool IsUnsaved { get; private set; }

    [ObservableProperty]
    public partial bool IsAdvanced { get; private set; }

    /// <summary>Why opening or saving a project failed; empty while nothing did.</summary>
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

    public override void OnShown()
    {
        ShowRecent();
        Content?.OnShown();
    }

    public override async Task<bool> CanCloseAsync() => await CanCloseProjectAsync().ConfigureAwait(true);

    public void Dispose()
    {
        Services.Agent.PropertyChanged -= OnAgentChanged;
        Release();
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

    /// <summary>Sends a request to the agent; a refusal or a lost connection shows a toast.</summary>
    /// <param name="request">The request.</param>
    /// <returns>The answer, and whether the agent took the request.</returns>
    internal Task<(bool Sent, Message? Answer)> AskAsync(Message request) => AgentRequests.AskAsync(Services, request);

    /// <summary>Takes back a problem shown, once the author acts again.</summary>
    internal void ClearProblem() => ProblemTitle = ProblemText = "";

    /// <summary>Says why opening or saving a project failed.</summary>
    /// <param name="title">What failed.</param>
    /// <param name="text">Why, and what to do.</param>
    internal void ShowProblem(string title, string text) => (ProblemTitle, ProblemText) = (title, text);

    /// <summary>Saves the project to its file, asking for one when it has none.</summary>
    /// <param name="choose">Ask for a file even when the project has one.</param>
    /// <returns>Whether the project was saved.</returns>
    internal async Task<bool> SaveProjectAsync(bool choose)
    {
        if (Session is not { } session)
        {
            return false;
        }

        ClearProblem();
        var path = session.Path;
        if (choose || path is null)
        {
            path = await Services.Pickers.PickSaveFileAsync(Strings.Creator_SaveTitle,
                FileNames.Suggest(session.Project.Game?.Name, Strings.Creator_ProjectFallbackName, Options.ProjectExtension), ProjectKind,
                FolderOf(session)).ConfigureAwait(true);
            if (path is null)
            {
                return false;
            }
        }

        var project = session.Project;
        try
        {
            await CreatorProjectStore.SaveAsync(project, path, CancellationToken.None).ConfigureAwait(true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            ShowProblem(Strings.Creator_SaveFailedTitle, Strings.Creator_SaveFailed);
            return false;
        }

        session.Saved(path, project);
        Remember(path);
        return true;
    }

    [RelayCommand]
    private async Task NewProjectAsync()
    {
        if (await CanCloseProjectAsync().ConfigureAwait(true))
        {
            Start(new CreatorSession(Options, new CreatorProject(), null));
        }
    }

    [RelayCommand]
    private async Task OpenProjectAsync()
    {
        var path = await Services.Pickers.PickFileAsync(Strings.Creator_OpenTitle, ProjectKind,
            Session is { } session ? FolderOf(session) : null).ConfigureAwait(true);
        if (path is not null)
        {
            await OpenAsync(path).ConfigureAwait(true);
        }
    }

    [RelayCommand]
    private Task OpenRecentAsync(RecentProject recent) => OpenAsync(recent.Path);

    [RelayCommand]
    private async Task SaveAsync() => await SaveProjectAsync(choose: false).ConfigureAwait(true);

    [RelayCommand]
    private async Task SaveAsAsync() => await SaveProjectAsync(choose: true).ConfigureAwait(true);

    [RelayCommand]
    private void OpenStep(StepItem step) => GoTo(step.Step);

    [RelayCommand]
    private void DismissProblem() => ClearProblem();

    private async Task OpenAsync(string path)
    {
        ClearProblem();
        if (!await CanCloseProjectAsync().ConfigureAwait(true))
        {
            return;
        }

        CreatorProject project;
        try
        {
            project = await CreatorProjectStore.LoadAsync(path, CancellationToken.None).ConfigureAwait(true);
        }
        catch (ProjectLoadException exception)
        {
            ShowProblem(Strings.Creator_OpenFailedTitle, Text.Format(Strings.Creator_OpenFailed, ("reason", exception.Error.ToString()),
                ("product", Services.Context.ProductName)));
            return;
        }

        Remember(path);
        Start(new CreatorSession(Options, project, path));
    }

    private void Start(CreatorSession session)
    {
        Release();
        ClearProblem();
        Game = null;
        Session = session;
        session.PropertyChanged += OnSessionChanged;
        var game = new GameStepViewModel(this, session);
        var files = new FilesStepViewModel(this, session);
        _steps = [game, files, new ReviewStepViewModel(this, session), new PackageStepViewModel()];
        Step = CreatorStep.Game;
        Content = null;
        ShowSession();
        RefreshSteps();
        GoTo(CreatorStep.Game);
        _ = ResumeAsync(session, game);
    }

    // A project file keeps only what a comparison cannot tell, so an opened project takes its game as the stores report
    // it now, and its files step compares the folders again.
    private async Task ResumeAsync(CreatorSession session, GameStepViewModel game)
    {
        await game.LoadAsync().ConfigureAwait(true);
        if (Session != session || session.Project.Game is not { } chosen)
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

        if (Session is not { IsDirty: true } session)
        {
            return true;
        }

        var answer = await Services.Dialogs.ChooseAsync(new ChoiceDialogViewModel(Strings.Creator_UnsavedTitle,
            session.Path is null ? Strings.Creator_UnsavedNew : Text.Format(Strings.Creator_UnsavedMessage, ("project", ProjectName)),
        [
            new DialogChoice(SaveChoice, Icons.Save, Strings.Creator_UnsavedSave, Strings.Creator_UnsavedSaveDescription),
            new DialogChoice(DiscardChoice, Icons.Trash, Strings.Creator_UnsavedDiscard, Strings.Creator_UnsavedDiscardDescription,
                IsDanger: true),
        ])).ConfigureAwait(true);
        return answer.Key switch
        {
            SaveChoice => await SaveProjectAsync(choose: false).ConfigureAwait(true),
            DiscardChoice => true,
            _ => false,
        };
    }

    private void Release()
    {
        if (Session is { } session)
        {
            session.PropertyChanged -= OnSessionChanged;
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

    partial void OnGameChanged(DetectedGameEntry? value) => RefreshSteps();

    private void ShowSession()
    {
        if (Session is not { } session)
        {
            return;
        }

        ProjectName = session.Path is { } path ? Path.GetFileNameWithoutExtension(path) : Strings.Creator_New;
        IsUnsaved = session.Path is null || session.IsDirty;
        SaveState = session.Path is null ? Strings.Creator_StateNew
            : session.IsDirty ? Strings.Creator_StateChanged
            : Strings.Creator_StateSaved;
        IsAdvanced = session.Project.Mode == CreatorMode.Advanced;
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

    /// <summary>Whether the author's files only repeat the game's, so a simple project has nothing to package.</summary>
    /// <param name="session">The project with its comparison.</param>
    internal static bool HasNothingToPackage(CreatorSession session) =>
        session.Project.Mode == CreatorMode.Simple && session.Project.ExtraSteps.Count == 0
        && session.Comparison?.Files.All(file => file.Difference == FileDifference.Identical) == true;

    private void OnAgentChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(IAgent.State))
        {
            ShowRecent();
        }
    }

    private void ShowRecent()
    {
        var paths = Agent.State.Settings.RecentCreatorProjects;
        if (Recent.Select(recent => recent.Path).SequenceEqual(paths))
        {
            return;
        }

        Recent.Clear();
        foreach (var path in paths)
        {
            var recent = new RecentProject(path);
            Recent.Add(recent);
            _ = DateAsync(recent);
        }

        OnPropertyChanged(nameof(HasRecent));
    }

    private async Task DateAsync(RecentProject recent)
    {
        var changed = await Task.Run(() => LastWrite(recent.Path)).ConfigureAwait(true);
        recent.Changed = changed is { } at
            ? Text.Format(Strings.Creator_RecentChanged, ("when", Formats.RelativeTime(at, Services.Clock)))
            : Strings.Creator_RecentMissing;
    }

    private DateTimeOffset? LastWrite(string path)
    {
        try
        {
            return (FileSystem ?? PhysicalFileSystem.Instance).GetEntry(path) is { IsDirectory: false } file ? file.LastWriteTimeUtc : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    // The list lives in the settings, which the agent keeps; a list that could not be updated only misses this file.
    private void Remember(string path) => _ = SendQuietlyAsync(new RememberCreatorProject(path));

    private async Task SendQuietlyAsync(Message request)
    {
        try
        {
            await Agent.RequestAsync(request).ConfigureAwait(true);
        }
        catch (Exception exception) when (exception is IOException or TimeoutException or Protocol.Transport.RequestFailedException)
        {
        }
    }

    private static string? FolderOf(CreatorSession session) =>
        session.Path is { } path ? Path.GetDirectoryName(path)
        : session.Project.AuthorFolder.Length > 0 ? Path.GetDirectoryName(session.Project.AuthorFolder)
        : null;
}

/// <summary>A step of the creator as the page shows it.</summary>
public interface ICreatorStepView : IDisposable
{
    /// <summary>Called when the step is shown, also when it is gone back to.</summary>
    void OnShown();

    /// <summary>Whether the project may be closed now; a step with work under way asks first.</summary>
    Task<bool> CanLeaveAsync();
}
