using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JustModIt.Manager.Core.Catalog;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.IO;
using JustModIt.Manager.Core.Library;
using JustModIt.Manager.Core.Packages;
using JustModIt.Manager.Core.Platform;
using JustModIt.Manager.Creator;
using JustModIt.Manager.Creator.Building;
using JustModIt.Manager.Creator.Comparison;
using JustModIt.Manager.Creator.Planning;
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

/// <summary>The tabs of a project's page, in their order.</summary>
public enum ProjectTab
{
    Release,
    Runtime,
    Description,
}

/// <summary>What an open project is doing, which its card in the library says.</summary>
public enum ProjectWork
{
    None,
    Comparing,
    Building,
}

/// <summary>
/// The open project, which saves itself: its part on its version of the game in tabs (the release, the files its software
/// changes in the game, the package's description) over a bar that holds what the build needs and the build itself. Comparing,
/// planning and building run on the thread pool. It is deliberately not <see cref="IDisposable"/>: the navigator disposes a
/// page it drops and a click in the sidebar drops this one, while its build goes on, so the library page holds it as
/// <see cref="CreatorPageViewModel.OpenPage"/> and alone closes it, and alone answers the window's close.
/// </summary>
public sealed partial class ProjectPageViewModel : PageViewModel
{
    private readonly CreatorPageViewModel _creator;
    private Task? _resuming;
    private IReadOnlyList<(GameKey Key, string Folder)> _versions;
    private DateTimeOffset? _resumedAt;
    private bool _titled;

    /// <summary>Opens a project, whose part compares once the stores tell where its game is.</summary>
    /// <param name="creator">The library page.</param>
    /// <param name="project">The project as created or read.</param>
    public ProjectPageViewModel(CreatorPageViewModel creator, CreatorProject project)
    {
        _creator = creator;
        ProjectId = project.Id;
        var session = new CreatorSession(Options, project, Library.Store(project.Id), Log);
        Session = session;
        _versions = Folders(session.Project);
        Review = new ReviewViewModel(this, session);
        Description = new PackageDescriptionViewModel(this, session);
        Test = new TestInstallViewModel(this);
        Build = new BuildViewModel(this, session, Description, Test);
        ShowCells(session);
        session.PropertyChanged += OnSessionChanged;
        Build.PropertyChanged += OnWorkChanged;
        Test.PropertyChanged += OnWorkChanged;
        Agent.PropertyChanged += OnAgentChanged;
        ShowSession();
        _ = ReadRecordAsync();
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

    /// <summary>Opens the folders the project works with, giving up on a drive that does not answer.</summary>
    public LibraryAccess Access => _creator.Access;

    public override string Title => ProjectName;

    /// <summary>The project being worked on; null once it is closed.</summary>
    [ObservableProperty]
    public partial CreatorSession? Session { get; private set; }

    /// <summary>The cell the panel and the tabs show; null while the project has no version.</summary>
    [ObservableProperty]
    public partial CellViewModel? Cell { get; private set; }

    public ReviewViewModel Review { get; }

    public PackageDescriptionViewModel Description { get; }

    public BuildViewModel Build { get; }

    public TestInstallViewModel Test { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TabContent), nameof(IsPartView))]
    public partial ProjectTab Tab { get; set; }

    /// <summary>
    /// The view model of the tab shown. The release tab takes the first cell, which stays while the author picks others, so a
    /// pick does not slide the tab in again; a table and its panel read the page.
    /// </summary>
    public object? TabContent => Tab switch
    {
        ProjectTab.Runtime => Review,
        ProjectTab.Description => Description,
        _ => Cells.Count > 0 ? Cells[0] : null,
    };

    /// <summary>The last tab change went to an earlier tab, which reverses its transition.</summary>
    [ObservableProperty]
    public partial bool IsGoingBack { get; private set; }

    /// <summary>The release tab shows the part's operations or steps in place of its card.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPartView))]
    public partial bool ShowsOperations { get; private set; }

    /// <summary>The page shows the part's operations, which the breadcrumb names.</summary>
    public bool IsPartView => ShowsOperations && Tab == ProjectTab.Release;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title))]
    public partial string ProjectName { get; private set; } = "";

    /// <summary>The part's mode, as its card names it.</summary>
    [ObservableProperty]
    public partial string PartLine { get; private set; } = "";

    /// <summary>The version's store and build and the project's last package, under its title.</summary>
    [ObservableProperty]
    public partial string VersionLine { get; private set; } = "";

    /// <summary>What went wrong with the project as a whole; empty while nothing did.</summary>
    [ObservableProperty]
    public partial string ProblemTitle { get; private set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasProblem))]
    public partial string ProblemText { get; private set; } = "";

    public bool HasProblem => ProblemText.Length > 0;

    /// <summary>The game of the chosen cell's version as the agent reported it; null until it did.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GameStoreName))]
    public partial DetectedGameEntry? Game { get; private set; }

    /// <summary>The store of the chosen cell's version.</summary>
    public string GameStoreName => Cell?.StoreName ?? "";

    /// <summary>The project names a version of the game; one imported without a game takes it first.</summary>
    [ObservableProperty]
    public partial bool HasVersion { get; private set; }

    /// <summary>The project's record of its builds; null when it has none.</summary>
    internal BuildRecord? Record { get; private set; }

    /// <summary>The words of the link to the part's operations, with their count.</summary>
    [ObservableProperty]
    public partial string AllOperationsText { get; private set; } = "";

    /// <summary>What the project is doing now.</summary>
    public ProjectWork Work => Cells.Any(cell => cell.IsComparing) ? ProjectWork.Comparing : Build.IsBuilding ? ProjectWork.Building : ProjectWork.None;

    public override void OnShown()
    {
        _ = CheckAsync();
        _ = Description.HintAsync();
        _ = ReadKeptPathsAsync();
        Test.ShowTest();
    }

    public override void OnWindowActivated() => _ = CheckAsync();

    public override bool GoBackWithin()
    {
        if (IsPartView)
        {
            ShowsOperations = false;
            return true;
        }

        if (IsPanelOpen)
        {
            IsPanelOpen = false;
            return true;
        }

        return false;
    }

    /// <summary>Shows what went wrong with the project as a whole.</summary>
    /// <param name="title">What went wrong.</param>
    /// <param name="text">Why, and what to do.</param>
    internal void ShowProblem(string title, string text) => (ProblemTitle, ProblemText) = (title, text);

    /// <summary>Takes back a problem shown, once the author acts again.</summary>
    [RelayCommand]
    internal void ClearProblem() => ProblemTitle = ProblemText = "";

    /// <summary>
    /// Asks whether a running build may stop and saves the project at once. A comparison writes nothing and starts over at the
    /// next open, and it is not stopped, since the close of the window may still be refused after this.
    /// </summary>
    /// <returns>Whether nothing keeps the project from closing any more.</returns>
    internal async Task<bool> LeaveAsync() =>
        await Build.CanStopAsync().ConfigureAwait(true)
        && (Session is not { } session || await session.FlushAsync().ConfigureAwait(true)
            || await Services.Dialogs.ShowAsync(new ConfirmDialogViewModel(Strings.Creator_SaveFailedTitle, Strings.Creator_CloseUnsaved,
                Strings.Creator_CloseUnsavedConfirm, Icons.CircleX, DialogTone.Danger) { ConfirmIcon = Icons.CircleX }).ConfigureAwait(true));

    /// <summary>Closes the project for good, once nothing keeps it open, also cancelling a comparison and ending its saves.</summary>
    /// <returns>False when the author kept it open.</returns>
    internal async Task<bool> CloseAsync()
    {
        if (!await LeaveAsync().ConfigureAwait(true))
        {
            return false;
        }

        Agent.PropertyChanged -= OnAgentChanged;
        foreach (var cell in Cells)
        {
            cell.Dispose();
        }

        Review.Dispose();
        Build.Dispose();
        Test.Dispose();
        if (Session is { } session)
        {
            Session = null;
            session.PropertyChanged -= OnSessionChanged;
            await session.DisposeAsync().ConfigureAwait(true);
        }

        return true;
    }

    /// <summary>
    /// Reads again the game's paths that the agent keeps for an undone test, of each version's game in its folder as the store
    /// reports it now; a lost connection keeps those read before.
    /// </summary>
    internal async Task ReadKeptPathsAsync()
    {
        if (Session is not { } session || await AgentRequests.AskQuietlyAsync(Agent, new GetLeftoverBackups()).ConfigureAwait(true) is not LeftoverBackups found
            || Session != session)
        {
            return;
        }

        foreach (var version in session.Project.Versions)
        {
            if (GameOf(version.Game.Key) is { } game)
            {
                var layout = new FolderLayout(version.Folders);
                await session.UseKeptPathsAsync(game.Key, found.Backups
                    .Where(backup => backup.Game == game.Key && backup.Kept is not null && PathComparison.AreSame(backup.GameDirectory, game.Game.InstallDirectory))
                    .SelectMany(backup => backup.Kept!.Paths)
                    .Select(layout.ToPackage)).ConfigureAwait(true);
            }
        }
    }

    /// <summary>Reads the project's record of its builds again, e.g. after a build wrote it.</summary>
    internal async Task ReadRecordAsync()
    {
        Record = await Library.ReadBuildAsync(ProjectId, CancellationToken.None).ConfigureAwait(true);
        ShowSession();
        ShowBar();
        foreach (var cell in Cells)
        {
            await cell.ShowRecordAsync(Record, CancellationToken.None).ConfigureAwait(true);
        }
    }

    /// <summary>
    /// Asks the stores where the games are and which builds they have, then compares again each cell whose game or author's
    /// files changed since its comparison. Nothing watches them meanwhile, so coming back, e.g. from an editor, is when it matters.
    /// </summary>
    /// <param name="anyway">Compares again whatever changed, as a build that found the files changed asks.</param>
    /// <returns>Completes once the check ended; a comparison it starts goes on.</returns>
    internal async Task CheckAsync(bool anyway = false)
    {
        if (!HasVersion || Session is null)
        {
            return;
        }

        if (_resuming is not { IsCompleted: false })
        {
            _resuming = ResumeAsync();
        }

        await _resuming.ConfigureAwait(true);
        await Task.WhenAll(Cells.Select(cell => anyway ? cell.CompareAsync() : cell.CheckAsync())).ConfigureAwait(true);
    }

    [RelayCommand]
    private void ShowOperations() => (ShowsOperations, IsPanelOpen) = (true, IsPanelOpen && !IsOverlay);

    [RelayCommand]
    private void ShowRelease() => GoBackWithin();

    // A project keeps only what a comparison cannot tell, so an opened project takes its games as the stores report them now,
    // and its cells compare the folders again. Only a whole look of the stores tells a game is gone.
    private async Task ResumeAsync()
    {
        var session = Session!;
        _resumedAt = Agent.State.Library.ScannedAt;
        // Asked again whenever the page or the window comes back, so an agent out of reach only means no answer this time.
        if (await AgentRequests.AskQuietlyAsync(Agent, new GetDetectedGames()).ConfigureAwait(true) is not DetectedGames detected || Session != session)
        {
            return;
        }

        // The games first, so a folder a store moved is compared with the game where it is now.
        (_detected, _lookedEverywhere) = (detected.Games, Agent.State.Library is { IsCached: false, ScannedAt: not null });
        ShowGames(session);
        foreach (var version in session.Project.Versions)
        {
            if (GameOf(version.Game.Key) is { } entry && (version.GameFolder != entry.Game.InstallDirectory || version.Game.Name != entry.Name))
            {
                session.ChangeVersion(entry.Key, changed => changed with { Game = new ProjectGame(entry.Key, entry.Name), GameFolder = entry.Game.InstallDirectory });
            }
        }
    }

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
        if (Cells.Any(cell => cell.Game is null) && _resuming is not { IsCompleted: false } && Agent.State.Library.ScannedAt != _resumedAt)
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
    }

    // What a package and its test are made for: each version's game and the folder it lies in.
    private static List<(GameKey Key, string Folder)> Folders(CreatorProject project) =>
        [.. project.Versions.Select(version => (version.Game.Key, version.GameFolder))];

    private void ShowSession()
    {
        if (Session is not { } session)
        {
            return;
        }

        // A manifest package names itself, by the game's name until the author says otherwise.
        if (!_titled && session.Review?.Plan is { Format.Kind: PackageKind.Manifest } && session.Version?.Game is { } title)
        {
            _titled = true;
            session.Change(project => project.Metadata.Title.Length > 0 ? project : project with { Metadata = project.Metadata with { Title = title.Name } });
        }

        ProjectName = ProjectCard.NameOf(session.Project);
        HasVersion = session.Version is not null;
        PartLine = GameWording.Join([HasVersion ? GameStoreName : "", Text.Format(Strings.Creator_PartMode, ("mode", session.Part.Mode.ToString()))]);
        PanelName = GameWording.Join([session.Part.Name.Length > 0 ? session.Part.Name : Strings.Creator_PartBase, PartLine]);
        // A table names each version in its column's head.
        VersionLine = GameWording.Join(
        [
            HasVersion && !IsTable ? GameStoreName : "",
            !IsTable && Game?.Game.BuildId is { Length: > 0 } build ? Text.Format(Strings.Creator_GameBuild, ("build", build)) : "",
            ProjectCard.LastPackageOf(Record, Services.Clock)
                ?? Text.Format(Strings.Creator_NoPackageYet, ("date", Formats.Date(session.Project.CreatedAt, Services.Clock.LocalTimeZone))),
        ]);
        AllOperationsText = Text.Format(Strings.Creator_AllOperations, ("count", session.Review?.Plan.Operations.Count ?? 0));
        OnPropertyChanged(nameof(OutputFolder));
        OnPropertyChanged(nameof(OutputText));
        OnPropertyChanged(nameof(HasOutput));
    }
}
