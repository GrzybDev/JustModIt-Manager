using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JustModIt.Manager.Core.IO;
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

/// <summary>What an open project is doing, which its card in the library says.</summary>
public enum ProjectWork
{
    None,
    Comparing,
    Building,
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

/// <summary>
/// The open project, taken through four steps (the game, the author's files, the check of the operations, the package), which
/// saves itself. Comparing, planning and building run on the thread pool. It is deliberately not <see cref="IDisposable"/>: the
/// navigator disposes a page it drops and a click in the sidebar drops this one, while its build goes on, so the library page
/// holds it as <see cref="CreatorPageViewModel.OpenPage"/> and alone closes it, and alone answers the window's close.
/// </summary>
public sealed partial class ProjectPageViewModel : PageViewModel
{
    private readonly CreatorPageViewModel _creator;
    private ICreatorStepView[] _steps;

    /// <summary>Opens a project on its game's step, moving on by itself when the game and its folder are known.</summary>
    /// <param name="creator">The library page.</param>
    /// <param name="project">The project as created or read.</param>
    public ProjectPageViewModel(CreatorPageViewModel creator, CreatorProject project)
    {
        _creator = creator;
        ProjectId = project.Id;
        Steps =
        [
            new(CreatorStep.Game, Strings.Creator_StepGame),
            new(CreatorStep.Files, Strings.Creator_StepFiles),
            new(CreatorStep.Review, Strings.Creator_StepReview),
            new(CreatorStep.Package, Strings.Creator_StepPackage),
        ];
        var session = new CreatorSession(Options, project, Library.Store(project.Id), Log);
        Session = session;
        session.PropertyChanged += OnSessionChanged;
        var game = new GameStepViewModel(this, session);
        var files = new FilesStepViewModel(this, session);
        var package = new PackageStepViewModel(this, session);
        _steps = [game, files, new ReviewViewModel(this, session), package];
        files.PropertyChanged += OnWorkChanged;
        package.Build.PropertyChanged += OnWorkChanged;
        ShowSession();
        GoTo(CreatorStep.Game);
        _ = ResumeAsync(session, game);
    }

    public Guid ProjectId { get; }

    public UiServices Services => _creator.Services;

    /// <summary>Reads the folders and files the creator works with; null for the real file system.</summary>
    public IFileSystem? FileSystem => _creator.FileSystem;

    public CreatorOptions Options => _creator.Options;

    /// <summary>Records what goes wrong in the creator, for the diagnostic report.</summary>
    public ILogger Log => _creator.Log;

    public IAgent Agent => _creator.Agent;

    /// <summary>Where the project and the record of its builds are kept.</summary>
    public ProjectLibrary Library => _creator.Library;

    public override string Title => ProjectName;

    public IReadOnlyList<StepItem> Steps { get; }

    /// <summary>The project being worked on; null once it is closed.</summary>
    [ObservableProperty]
    public partial CreatorSession? Session { get; private set; }

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
    [NotifyPropertyChangedFor(nameof(Title))]
    public partial string ProjectName { get; private set; } = "";

    [ObservableProperty]
    public partial bool IsAdvanced { get; private set; }

    /// <summary>What went wrong with the project as a whole; empty while nothing did.</summary>
    [ObservableProperty]
    public partial string ProblemTitle { get; private set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasProblem))]
    public partial string ProblemText { get; private set; } = "";

    public bool HasProblem => ProblemText.Length > 0;

    /// <summary>The project's game as the agent reported it, once the author chose one.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GameStoreName))]
    public partial DetectedGameEntry? Game { get; internal set; }

    public string GameStoreName => Game is { } game ? GameWording.StoreName(game.Key.Store) : "";

    /// <summary>Another store reports a game of the same name as <see cref="Game"/>, whose package would take the same name.</summary>
    internal bool IsGameOnOtherStores { get; set; }

    /// <summary>What the project is doing now.</summary>
    public ProjectWork Work => _steps is [_, FilesStepViewModel { IsComparing: true }, ..] ? ProjectWork.Comparing
        : _steps is [.., PackageStepViewModel { Build.IsBuilding: true }] ? ProjectWork.Building
        : ProjectWork.None;

    public override void OnShown() => Content?.OnShown();

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

    /// <summary>
    /// Asks whether a running build may stop and saves the project at once. A comparison writes nothing and starts over at the
    /// next open, and it is not stopped, since the close of the window may still be refused after this.
    /// </summary>
    /// <returns>Whether nothing keeps the project from closing any more.</returns>
    internal async Task<bool> LeaveAsync()
    {
        foreach (var step in _steps)
        {
            if (!await step.CanLeaveAsync().ConfigureAwait(true))
            {
                return false;
            }
        }

        return Session is not { } session || await session.FlushAsync().ConfigureAwait(true)
            || await Services.Dialogs.ShowAsync(new ConfirmDialogViewModel(Strings.Creator_SaveFailedTitle, Strings.Creator_CloseUnsaved,
                Strings.Creator_CloseUnsavedConfirm, Icons.CircleX, DialogTone.Danger) { ConfirmIcon = Icons.CircleX })
                    .ConfigureAwait(true);
    }

    /// <summary>Closes the project for good, once nothing keeps it open, also cancelling a comparison and ending its saves.</summary>
    /// <returns>False when the author kept it open.</returns>
    internal async Task<bool> CloseAsync()
    {
        if (!await LeaveAsync().ConfigureAwait(true))
        {
            return false;
        }

        foreach (var step in _steps)
        {
            step.Dispose();
        }

        _steps = [];
        if (Session is { } session)
        {
            Session = null;
            session.PropertyChanged -= OnSessionChanged;
            await session.DisposeAsync().ConfigureAwait(true);
        }

        return true;
    }

    /// <summary>Closes the project, once nothing keeps it open, and goes back to the library.</summary>
    internal async Task FinishAsync()
    {
        if (await _creator.CloseProjectAsync().ConfigureAwait(true))
        {
            Services.Navigator.GoBackCommand.Execute(null);
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

    [RelayCommand]
    private async Task OpenStepAsync(StepItem step)
    {
        if (step.Step != Step && Content is { } current && !await current.CanLeaveAsync().ConfigureAwait(true))
        {
            return;
        }

        GoTo(step.Step);
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
            (ProblemTitle, ProblemText) = (Strings.Creator_GameGoneTitle, Text.Format(Strings.Creator_GameGone, ("game", chosen.Name)));
            return;
        }

        game.Choose(entry);
        GoTo(CreatorStep.Files);
    }

    private void OnSessionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(CreatorSession.SaveFailed))
        {
            ShowSession();
            RefreshSteps();
        }
        else if (Session!.SaveFailed)
        {
            (ProblemTitle, ProblemText) = ("", Strings.Creator_SaveFailed);
        }
        // The next change saves again, and a save that works takes the failure back.
        else if (ProblemText == Strings.Creator_SaveFailed)
        {
            ClearProblem();
        }
    }

    private void OnWorkChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FilesStepViewModel.State))
        {
            OnPropertyChanged(nameof(Work));
        }
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

        ProjectName = ProjectCard.NameOf(session.Project);
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
}

/// <summary>A step of the creator as the page shows it.</summary>
public interface ICreatorStepView : IDisposable
{
    /// <summary>Called when the step is shown, also when it is gone back to.</summary>
    void OnShown();

    /// <summary>Whether the project may be closed now; a step with work under way asks first.</summary>
    Task<bool> CanLeaveAsync() => Task.FromResult(true);
}
