using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JustModIt.Manager.Core.Catalog;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.Installation;
using JustModIt.Manager.Core.Jobs;
using JustModIt.Manager.Core.State;
using JustModIt.Manager.Localization;
using JustModIt.Manager.Protocol.Messages;
using JustModIt.Manager.Protocol.Views;
using JustModIt.Manager.Ui.Connection;
using JustModIt.Manager.Ui.Controls;
using JustModIt.Manager.Ui.Controls.Markdown;
using JustModIt.Manager.Ui.Images;
using JustModIt.Manager.Ui.Navigation;
using JustModIt.Manager.Ui.Overlays;
using JustModIt.Manager.Ui.Pages.Library;
using JustModIt.Manager.Ui.Theme;

namespace JustModIt.Manager.Ui.Pages.Game;

/// <summary>
/// A game's page: what the store and this computer say about it, its installs with their add-ons, the automation choice,
/// the one next step, and the project's page from the site below. Everything about the game comes from the agent's state,
/// so the page shows offline; the project's page is asked for in the background and read off the UI thread.
/// </summary>
public sealed partial class GamePageViewModel : PageViewModel, IProjectComparison, IDisposable
{
    // Sizes below these take seconds, about a minute, or longer to install, as the install step says.
    private const long QuickPackage = 50L << 20;
    private const long MinutePackage = 500L << 20;

    private readonly UiServices _services;
    private readonly GameKey _key;
    private readonly List<(DateTimeOffset At, long Bytes)> _samples = [];
    private readonly Dictionary<Guid, RuntimeFilesView> _runtimeFiles = [];
    private readonly Dictionary<Guid, LicensesView> _licenses = [];
    private readonly Dictionary<Guid, FeaturePanel> _features = [];
    private GameView? _game;
    private Guid? _detailsFor;
    private CancellationTokenSource? _details;
    private JobView? _job;
    private Guid? _cancelled;
    private NextStep? _result;
    private Guid? _finished;
    private JobEnded? _ended;
    private bool _askedDetected;
    private bool _composing;

    /// <summary>Creates the page of a game, which follows the agent's state from then on.</summary>
    /// <param name="services">The window's services.</param>
    /// <param name="game">The game the page is about.</param>
    public GamePageViewModel(UiServices services, GameKey game)
    {
        ArgumentNullException.ThrowIfNull(services);
        _services = services;
        _key = game;
        Story = new ProjectStory(services);
        _services.Agent.PropertyChanged += OnAgentChanged;
        _services.Agent.JobsProgressed += OnJobsProgressed;
        _services.Agent.JobEnded += OnJobEnded;
        Refresh();
    }

    public override string Title => _game?.Name ?? Strings.Shell_PageGame;

    public GameKey Game => _key;

    ICommand IProjectComparison.ChooseProjectCommand => ChooseProjectCommand;

    ICommand IProjectComparison.OpenStatusesCommand => OpenStatusesCommand;

    [ObservableProperty]
    public partial bool IsMissing { get; private set; }

    /// <summary>Why the game is not in the list: gone, or on this computer and waiting for a project to install.</summary>
    [ObservableProperty]
    public partial string MissingTitle { get; private set; } = Strings.GamePage_NotFoundTitle;

    [ObservableProperty]
    public partial string MissingText { get; private set; } = Text.Format(Strings.GamePage_NotFoundText, ("library", Strings.Shell_Library));

    /// <summary>The catalogue's game of a game waiting for a project; null when the site lists none.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCatalogueGame))]
    public partial string? CatalogueGame { get; private set; }

    public bool HasCatalogueGame => CatalogueGame is not null;

    [ObservableProperty]
    public partial string Name { get; private set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCover))]
    public partial string Cover { get; private set; } = "";

    public bool HasCover => Cover.Length > 0;

    [ObservableProperty]
    public partial bool IsCoverBlurred { get; private set; }

    [ObservableProperty]
    public partial string CoverName { get; private set; } = "";

    [ObservableProperty]
    public partial BackdropRecipe? Backdrop { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    public partial string Status { get; private set; } = "";

    [ObservableProperty]
    public partial string StatusColor { get; private set; } = "";

    [ObservableProperty]
    public partial string StatusTip { get; private set; } = "";

    [ObservableProperty]
    public partial bool IsStatusOpen { get; set; }

    [ObservableProperty]
    public partial bool IsHidden { get; private set; }

    [ObservableProperty]
    public partial string HiddenText { get; private set; } = "";

    [ObservableProperty]
    public partial string HideText { get; private set; } = "";

    [ObservableProperty]
    public partial Geometry HideIcon { get; private set; } = Icons.EyeOff;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTeam), nameof(AreHeadButtonsUnder))]
    public partial string TeamName { get; private set; } = "";

    [ObservableProperty]
    public partial string TeamAvatar { get; private set; } = "";

    [ObservableProperty]
    public partial Uri? TeamPage { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSitePage))]
    public partial Uri? SitePage { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSummary))]
    public partial string Summary { get; private set; } = "";

    [ObservableProperty]
    public partial string CheckedText { get; private set; } = "";

    [ObservableProperty]
    public partial bool IsChecking { get; private set; }

    [ObservableProperty]
    public partial string StoreName { get; private set; } = "";

    [ObservableProperty]
    public partial string Store { get; private set; } = "";

    [ObservableProperty]
    public partial Uri? StorePage { get; private set; }

    [ObservableProperty]
    public partial string StoreLinkName { get; private set; } = "";

    [ObservableProperty]
    public partial string GameState { get; private set; } = "";

    [ObservableProperty]
    public partial Tone GameStateTone { get; private set; }

    [ObservableProperty]
    public partial Geometry GameStateIcon { get; private set; } = Icons.Check;

    /// <summary>The store's build of the game, shown only while a release does not fit it; empty otherwise.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasBuild))]
    public partial string Build { get; private set; } = "";

    [ObservableProperty]
    public partial bool CanLaunch { get; private set; }

    [ObservableProperty]
    public partial bool HasFolder { get; private set; }

    [ObservableProperty]
    public partial string FolderPath { get; private set; } = "";

    public ObservableCollection<InstallRow> Installs { get; } = [];

    [ObservableProperty]
    public partial bool IsNotInstalled { get; private set; }

    /// <summary>The add-ons of the project a new install takes; null when it offers none to choose.</summary>
    [ObservableProperty]
    public partial FeaturePanel? NewFeatures { get; private set; }

    [ObservableProperty]
    public partial bool ShowsAutomation { get; private set; }

    [ObservableProperty]
    public partial string AutomationLabel { get; private set; } = "";

    [ObservableProperty]
    public partial string AutomationTitle { get; private set; } = "";

    [ObservableProperty]
    public partial string AutomationHint { get; private set; } = "";

    [ObservableProperty]
    public partial bool IsAutomatic { get; set; }

    /// <summary>The store offers the game in the app's target language, so a notice stands where the switch would.</summary>
    [ObservableProperty]
    public partial bool ShowsOfficial { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStep))]
    public partial NextStep? Step { get; private set; }

    [ObservableProperty]
    public partial bool AreChangesOpen { get; set; }

    [ObservableProperty]
    public partial string ChangesToggle { get; private set; } = "";

    [ObservableProperty]
    public partial string ChangesTitle { get; private set; } = "";

    public ObservableCollection<JobStepView> Steps { get; } = [];

    [ObservableProperty]
    public partial string StageText { get; private set; } = "";

    [ObservableProperty]
    public partial string PercentText { get; private set; } = "";

    [ObservableProperty]
    public partial double Progress { get; private set; }

    [ObservableProperty]
    public partial bool IsIndeterminate { get; private set; }

    public ObservableCollection<ProjectChoice> Choices { get; } = [];

    [ObservableProperty]
    public partial bool ShowsChoices { get; private set; }

    [ObservableProperty]
    public partial string ChoicesText { get; private set; } = "";

    /// <summary>What the install's record keeps of a project the site no longer has.</summary>
    [ObservableProperty]
    public partial bool ShowsSavedInfo { get; private set; }

    /// <summary>The installed add-ons, or the variant, as the install's record names them; empty for none.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSavedFeatures))]
    public partial string SavedFeatures { get; private set; } = "";

    /// <summary>"Add-ons" for an install with add-ons, "Variant" for one of a release from before them.</summary>
    [ObservableProperty]
    public partial string SavedFeaturesLabel { get; private set; } = "";

    /// <summary>Every package the install came from, one per line.</summary>
    [ObservableProperty]
    public partial string SavedPackage { get; private set; } = "";

    [ObservableProperty]
    public partial string SavedFiles { get; private set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsDetails), nameof(HasNotes), nameof(ShowsChangesSection), nameof(StatusDescription))]
    public partial ProjectDetails? Details { get; private set; }

    /// <summary>The gallery, the game's story and the people behind the project, from the project's page.</summary>
    public ProjectStory Story { get; }

    [ObservableProperty]
    public partial bool IsLoadingDetails { get; private set; }

    [ObservableProperty]
    public partial string NotesTitle { get; private set; } = "";

    /// <summary>The window is too short for the panel to end above its edge, so the next step leads it.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AreHeadButtonsUnder))]
    public partial bool IsCompact { get; set; }

    public bool HasStatus => Status.Length > 0;

    /// <summary>The site's explanation of the project's status, which its chip opens.</summary>
    public string StatusDescription => Details?.StatusDescription ?? "";

    public bool HasTeam => TeamName.Length > 0;

    /// <summary>The head's buttons go under the team's line in a short window, or under the name when there is no team.</summary>
    public bool AreHeadButtonsUnder => IsCompact || !HasTeam;

    public bool HasSitePage => SitePage is not null;

    public bool HasSummary => Summary.Length > 0;

    public bool HasBuild => Build.Length > 0;

    public bool HasStep => Step is not null;

    public bool HasSavedFeatures => SavedFeatures.Length > 0;

    public bool ShowsDetails => Details is not null;

    public bool HasNotes => Details is { Notes.IsEmpty: false };

    /// <summary>The release's changes show as their own section when no step offers them already.</summary>
    public bool ShowsChangesSection => Details is { Changes.IsEmpty: false } && Step is not { OffersChanges: true };

    public override void OnShown()
    {
        Refresh();
        if (_game is not null)
        {
            _ = AgentRequests.SendAsync(_services, new OpenedGame(_key));
        }
    }

    public void Dispose()
    {
        _services.Agent.PropertyChanged -= OnAgentChanged;
        _services.Agent.JobsProgressed -= OnJobsProgressed;
        _services.Agent.JobEnded -= OnJobEnded;
        _details?.Cancel();
        _details?.Dispose();
    }

    partial void OnIsAutomaticChanged(bool value)
    {
        if (_game is { } game && value != AutomaticNow(game))
        {
            var installed = game.Installs.Count > 0;
            _ = AgentRequests.SendAsync(_services,
                installed ? new SetGameAutomation(_key, AutoUpdate: value) : new SetGameAutomation(_key, AutoInstall: value));
        }
    }

    partial void OnAreChangesOpenChanged(bool value) =>
        ChangesToggle = value ? Strings.GamePage_ChangesHide : Strings.GamePage_ChangesShow;

    partial void OnIsCompactChanged(bool value) => Compose();

    [RelayCommand]
    private async Task CheckNowAsync() => await AgentRequests.SendAsync(_services, new CheckNow()).ConfigureAwait(true);

    [RelayCommand]
    private async Task LaunchAsync() => await AgentRequests.SendAsync(_services, new LaunchGame(_key)).ConfigureAwait(true);

    [RelayCommand]
    private async Task OpenFolderAsync() => await AgentRequests.SendAsync(_services, new OpenFolder(AppFolder.Game, _key)).ConfigureAwait(true);

    [RelayCommand]
    private async Task ToggleHiddenAsync() => await AgentRequests.SendAsync(_services, new HideGame(_key, !IsHidden)).ConfigureAwait(true);

    [RelayCommand]
    private void ToggleStatus() => IsStatusOpen = !IsStatusOpen;

    [RelayCommand]
    private void ToggleChanges() => AreChangesOpen = !AreChangesOpen;

    [RelayCommand]
    private void Open(Uri? address)
    {
        if (address is not null)
        {
            _services.Links.Open(address);
        }
    }

    [RelayCommand]
    private void OpenHelp() => Open(PageLinks.Help(_services.Context.Site.SiteAddress));

    [RelayCommand]
    private void CheckFiles() => Open(Step?.CheckFiles);

    [RelayCommand]
    private void OpenStatuses() =>
        Open(PageLinks.Help(_services.Context.Site.SiteAddress, Details?.StatusSlug is { Length: > 0 } slug ? "status-" + slug : "project-statuses"));

    [RelayCommand]
    private void BackToLibrary() => _services.Navigator.Navigate(new LibraryRoute());

    [RelayCommand]
    private void OpenCatalogueGame()
    {
        if (CatalogueGame is { } slug)
        {
            _services.Navigator.Navigate(new CatalogueGameRoute(slug));
        }
    }

    // A game the stores report stays off the list while no project for it can be installed.
    private async Task FindDetectedAsync()
    {
        Message? answer;
        try
        {
            answer = await _services.Agent.RequestAsync(new GetDetectedGames()).ConfigureAwait(true);
        }
        catch (Exception exception) when (exception is IOException or TimeoutException or Protocol.Transport.RequestFailedException)
        {
            return;
        }

        if (answer is DetectedGames detected && detected.Games.FirstOrDefault(entry => entry.Key == _key) is { } game)
        {
            (MissingTitle, MissingText) = game.Unavailable is { } reason
                ? (Strings.GamePage_WaitingFileTitle, Text.Format(Strings.GamePage_WaitingFileText, ("reason", reason.ToString())))
                : (Strings.GamePage_WaitingTitle, Text.Format(Strings.Catalogue_HereWaits, ("library", Strings.Shell_Library)));
            CatalogueGame = game.Projects.Count > 0 ? game.Projects[0].GameSlug : null;
        }
    }

    [RelayCommand]
    private async Task ChooseProjectAsync(ProjectChoice choice)
    {
        if (_game is { Installs.Count: > 0, InstallReplaces: false, InstallRefusal: null })
        {
            await AgentRequests.SendAsync(_services, new InstallProject(_key, choice.ProjectId)).ConfigureAwait(true);
            return;
        }

        await AgentRequests.SendAsync(_services, new ChooseProject(_key, choice.ProjectId)).ConfigureAwait(true);
    }

    [RelayCommand]
    private Task PrimaryAsync() => Step?.PrimaryRequest is { } request ? SendJobAsync(request) : Task.CompletedTask;

    [RelayCommand]
    private async Task SecondaryAsync()
    {
        switch (Step)
        {
            case { SecondaryCancels: true } when _job is { } job:
                _cancelled = job.Id;
                await AgentRequests.SendAsync(_services, new CancelJob(job.Id)).ConfigureAwait(true);
                break;
            case { SecondaryUninstalls: true }:
                await UninstallAsync(SubjectInstall()?.Id).ConfigureAwait(true);
                break;
            case { SecondaryGoesBack: true }:
                BackToLibrary();
                break;
        }
    }

    [RelayCommand]
    private async Task DismissAsync()
    {
        var request = Step?.DismissRequest;
        _result = null;
        Compose();
        if (request is not null)
        {
            await AgentRequests.SendAsync(_services, request).ConfigureAwait(true);
        }
    }

    [RelayCommand]
    private Task UninstallAsync(Guid? install) => install is { } id ? ConfirmUninstallAsync(id) : Task.CompletedTask;

    [RelayCommand]
    private Task ActAsync(InstallRow row) => row.ActionRequest is { } request ? SendJobAsync(request) : Task.CompletedTask;

    [RelayCommand]
    private Task ApplyAsync(FeaturePanel panel) =>
        panel.Install is { } install ? SendJobAsync(new ApplyFeatures(install, panel.Changes())) : Task.CompletedTask;

    // A project not installed yet takes a turn at once, so installing it follows; an install keeps it in the window until
    // the player applies it.
    private void Turned(FeaturePanel panel, FeaturePill pill)
    {
        var group = panel.Groups.FirstOrDefault(group => group.Members.Contains(pill));
        if (group is null)
        {
            pill.IsOn = !pill.IsOn;
        }
        else
        {
            foreach (var member in group.Members)
            {
                member.IsOn = member == pill;
            }
        }

        if (panel.Install is null)
        {
            _ = AgentRequests.SendAsync(_services, new ChooseFeatures(_key, panel.Project,
                new Dictionary<string, bool?> { [pill.Key] = pill.IsOn }));
        }

        Compose();
    }

    // Folding an install's pills drops what the player turned, and the page's step follows.
    private void OnFeaturesChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FeaturePanel.IsOpen) && sender is FeaturePanel { IsOpen: false, Install: not null } panel && !_composing)
        {
            panel.Reset();
            Compose();
        }
    }

    private async Task ConfirmUninstallAsync(Guid install)
    {
        if (await _services.Dialogs.ConfirmAsync(new ConfirmDialogViewModel(Strings.GamePage_UninstallTitle,
                Text.Format(Strings.GamePage_UninstallMessage, ("game", Name)), Strings.GamePage_Uninstall, Icons.Trash, DialogTone.Danger)
            {
                ConfirmIcon = Icons.Trash,
            }).ConfigureAwait(true))
        {
            await SendJobAsync(new UninstallProject(install)).ConfigureAwait(true);
        }
    }

    private async Task SendJobAsync(Message request)
    {
        _result = null;
        _cancelled = null;
        await AgentRequests.SendAsync(_services, request).ConfigureAwait(true);
    }

    private void OnAgentChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(IAgent.State) or nameof(IAgent.HasState))
        {
            Refresh();
        }
    }

    private void OnJobsProgressed(object? sender, IReadOnlyList<JobView> jobs)
    {
        if (_job is { } known && jobs.FirstOrDefault(job => job.Id == known.Id) is { } moved && moved.Sequence > known.Sequence)
        {
            var sameStep = known.Stage == moved.Stage && known.WaitReason == moved.WaitReason;
            _job = moved;
            if (sameStep)
            {
                ShowProgress(moved);
            }
            else
            {
                Compose();
            }
        }
    }

    private void OnJobEnded(object? sender, JobEnded ended)
    {
        if (ended.Job.Game != _key)
        {
            return;
        }

        _ended = ended;
        // The state that ends an uninstall may come first, before the agent tells which files it left as they were or that
        // the install's folder was gone.
        if (_result is { Kind: StepKind.Removed } && _finished == ended.Job.Id && (ended.KeptFiles is { Count: > 0 } || ended.Forgotten))
        {
            _result = Removed(_services.Agent.State.Games.Any(game => game.Key == _key), ended);
            Compose();
        }
    }

    private void Refresh()
    {
        var state = _services.Agent.State;
        var game = state.Games.FirstOrDefault(game => game.Key == _key);
        var job = state.Jobs.FirstOrDefault(job => job.Game == _key) ?? game?.Job;
        if (_job is { } previous && job?.Id != previous.Id)
        {
            Finished(previous, game);
        }

        _job = job;
        if (game is null)
        {
            // An uninstall can take the game off the list; the page then keeps what it showed and says so.
            IsMissing = _game is null || _result?.Kind != StepKind.Removed;
            if (!IsMissing)
            {
                Compose();
            }
            else if (!_askedDetected)
            {
                _askedDetected = true;
                _ = FindDetectedAsync();
            }

            OnPropertyChanged(nameof(Title));
            return;
        }

        IsMissing = false;
        _game = game;
        OnPropertyChanged(nameof(Title));
        Compose();
        LoadDetails(Subject(game).Project);
    }

    // A job that ended while the page watched it leaves a result where its band was: done, cancelled, or removed.
    private void Finished(JobView job, GameView? game)
    {
        var failure = job.InstallId is { } install
            ? game?.Installs.FirstOrDefault(entry => entry.Id == install)?.LastFailure ?? game?.LastFailure
            : game?.LastFailure;
        if (failure is not null && failure.At >= _services.Clock.GetUtcNow().AddMinutes(-1))
        {
            return;
        }

        if (_cancelled == job.Id)
        {
            _result = Cancelled(job, game);
            return;
        }

        _finished = job.Id;
        _result = job.Kind == JobKind.Uninstall
            ? Removed(game is not null, _ended?.Job.Id == job.Id ? _ended : null)
            : new NextStep(StepKind.Done, Tone.Success, Icons.CircleCheck,
                Text.Format(Strings.GamePage_DoneTitle, ("kind", job.Kind.ToString())), Strings.GamePage_DoneText)
            {
                Meta = Strings.TimeJustNow,
                OffersHelp = true,
                CanDismiss = true,
                PointsToNotes = Details is { Notes.IsEmpty: false },
            };
    }

    private void Compose()
    {
        if (_game is not { } game || _composing)
        {
            return;
        }

        _composing = true;
        try
        {
            ComposePage(game);
        }
        finally
        {
            _composing = false;
        }
    }

    private void ComposePage(GameView game)
    {
        var state = _services.Agent.State;
        var subject = Subject(game);
        var install = subject.Install;
        var project = subject.Project;
        var store = GameWording.StoreName(game.Key.Store);

        Name = game.Name;
        CoverName = Text.Format(Strings.GamePage_CoverName, ("game", game.Name));
        Cover = project?.Cover is { Length: > 0 } cover ? cover : install?.CoverUrl?.AbsoluteUri ?? (game.Projects.Count > 0 ? game.Projects[0].Cover : "");
        IsCoverBlurred = AdultContent.IsVeiled(state, game.IsAdult, game.SiteIds);
        var backdrop = Details?.Backdrop ?? (project is null ? null : new BackdropRecipe(project.ProjectId.ToString(), "", "", "", project.Cover));
        Backdrop = backdrop is null ? null : backdrop with { IsBlurred = IsCoverBlurred };
        Status = project?.Status?.Name ?? "";
        StatusColor = project?.Status?.Color ?? "";
        StatusTip = Text.Format(Strings.GamePage_StatusTip, ("status", Status));
        IsHidden = game.IsHidden;
        HiddenText = Text.Format(Strings.GamePage_Hidden, ("library", Strings.Shell_Library));
        HideText = game.IsHidden ? Text.Format(Strings.GamePage_Show, ("library", Strings.Shell_Library)) : Strings.GamePage_Hide;
        HideIcon = game.IsHidden ? Icons.Eye : Icons.EyeOff;
        TeamName = project?.Team.Name ?? install?.TeamName ?? "";
        TeamAvatar = project?.Team.Avatar ?? "";
        var site = _services.Context.Site.SiteAddress;
        TeamPage = project is null ? null : PageLinks.TeamPage(site, project.Team.Slug);
        SitePage = project is null ? null : PageLinks.ProjectPage(site, project.GameSlug, project.Team.Slug);
        Summary = IsCompact ? "" : Details?.Summary ?? "";
        (Story.GameName, Story.TeamName, Story.TeamAvatar, Story.TeamPage, Story.IsBlurred) = (Name, TeamName, TeamAvatar, TeamPage, IsCoverBlurred);

        IsChecking = state.Activity.IsChecking;
        CheckedText = state.Library.ScannedAt is { } scanned
            ? Text.Format(state.Catalogue.Failure is null ? Strings.GamePage_Checked : Strings.GamePage_CheckedLast,
                ("time", Formats.RelativeTime(scanned, _services.Clock)))
            : "";

        ShowGame(game, store, install);
        ShowInstalls(game, install);
        ShowAutomation(game);
        ShowChoices(game);
        ShowSaved(install);
        var step = NextStep(game, state, install, project, store);
        if (step?.Kind != Step?.Kind)
        {
            AreChangesOpen = false;
        }

        Step = step is null ? null : step with { IsCompact = IsCompact };
        ChangesToggle = AreChangesOpen ? Strings.GamePage_ChangesHide : Strings.GamePage_ChangesShow;
        ChangesTitle = Text.Format(Strings.GamePage_ChangesTitle, ("version", GameWording.Version(project?.Release)));
        OnPropertyChanged(nameof(ShowsChangesSection));
        if (_job is { } job && step?.Kind == StepKind.Job)
        {
            ShowProgress(job);
        }
    }

    private void ShowGame(GameView game, string store, InstallView? install)
    {
        Store = game.Key.Store.Value;
        StoreName = store;
        StorePage = PageLinks.StorePage(game.Key);
        StoreLinkName = Text.Format(Strings.GamePage_OpenInStore, ("game", game.Name), ("store", store));
        // Like uninstalling, starting the game waits for the job, which a running game would hold up.
        CanLaunch = game.Detected is { CanLaunch: true } && _job is null;
        HasFolder = game.Detected is not null;
        FolderPath = game.Detected?.InstallDirectory ?? "";
        Build = install?.Status.Update == UpdateState.NotForThisGame || game.Project?.Unavailable is SelectionUnavailability.OtherStores
            ? game.Detected?.BuildId ?? ""
            : "";
        Build = Build.Length > 0 ? Text.Format(Strings.GamePage_Build, ("build", Build)) : "";
        if (game.Detected is not { } detected)
        {
            (GameState, GameStateTone, GameStateIcon) = (Text.Format(Strings.GamePage_GameGone, ("store", store)), Tone.Warning, Icons.TriangleAlert);
        }
        else if (detected.State == GameInstallState.Installed)
        {
            (GameState, GameStateTone, GameStateIcon) = (Strings.GamePage_GameReady, Tone.Success, Icons.Check);
        }
        else
        {
            GameState = GameWording.GameState(detected.State, detected.Reason, game.Key.Store);
            GameStateTone = Tone.Warning;
            GameStateIcon = detected.State == GameInstallState.Running ? Icons.Play : Icons.Clock;
        }
    }

    private void ShowInstalls(GameView game, InstallView? subject)
    {
        var several = game.Installs.Count > 1;
        foreach (var gone in _runtimeFiles.Keys.Where(id => game.Installs.All(install => install.Id != id)).ToList())
        {
            _runtimeFiles.Remove(gone);
        }

        var rows = game.Installs.Select(install => Row(game, install, several, install == subject)).ToList();
        IsNotInstalled = rows.Count == 0;
        NewFeatures = IsNotInstalled ? FeaturesOf(null, game.Project) : null;
        var shown = rows.Select(row => row.Features).Append(NewFeatures).OfType<FeaturePanel>().ToHashSet();
        foreach (var gone in _features.Where(entry => !shown.Contains(entry.Value)).ToList())
        {
            gone.Value.PropertyChanged -= OnFeaturesChanged;
            _features.Remove(gone.Key);
        }

        Sync(Installs, rows);
    }

    private InstallRow Row(GameView game, InstallView install, bool several, bool isSubject)
    {
        var version = GameWording.Version(install);
        var drive = Path.GetPathRoot(install.InstallDirectory)?.TrimEnd('\\', '/') ?? "";
        var (detail, warning) = install.Status switch
        {
            { State: MyGameState.NeedsRepair } status => (Text.Format(Strings.GamePage_FilesChanged, ("reason", status.Repair?.ToString() ?? ""),
                ("store", GameWording.StoreName(game.Key.Store))), true),
            _ when install.LeftOut.Count > 0 => (string.Join(" ", install.LeftOut.Select(feature => Text.Format(Strings.GamePage_FeatureLeftOut,
                ("reason", feature.Reason == PartLeftOutReason.NewerApp ? "app" : "other"), ("name", Quoted(feature.Name))))), true),
            { State: MyGameState.Orphaned, Orphan: OrphanReason.GameNotDetected } => (Text.Format(Strings.GamePage_BackupsStay, ("drive", drive)), false),
            { State: MyGameState.Orphaned, Orphan: OrphanReason.GameMoved } => (Text.Format(Strings.GamePage_LeftBehind, ("drive", drive)), false),
            { State: MyGameState.TestInstalled } => (Strings.GamePage_TestDetail, false),
            _ => (Text.Format(Strings.GamePage_FilesInPlace, ("location", install.BackupLocation.ToString())), false),
        };
        var (action, icon, request) = isSubject || _job is not null ? (null, null, null) : install.Status.State switch
        {
            MyGameState.UpdateAvailable => (Strings.Library_Update, Icons.CircleArrowUp, (Message)new UpdateProject(install.Id)),
            MyGameState.NeedsRepair => (Strings.Library_Repair, Icons.Wrench, new RepairProject(install.Id)),
            _ => ((string?)null, (Geometry?)null, (Message?)null),
        };
        return new InstallRow(install.Id,
            version.Length > 0 ? Text.Format(Strings.GamePage_Version, ("version", version)) : Strings.GamePage_VersionUnknown,
            Text.Format(Strings.GamePage_InstalledWhen, ("when", When(install.InstalledAt))), detail)
        {
            Project = several ? Text.Format(Strings.GamePage_ProjectOfTeam, ("project", install.ProjectName ?? ""), ("team", install.TeamName ?? "")) : "",
            IsWarning = warning,
            // The install the page is about offers removal in its step when it lost its game or project, so its row does not.
            CanUninstall = _job is null && install.Status.State != MyGameState.BackupsUnavailable
                && !(isSubject && install.Status.State is MyGameState.Orphaned or MyGameState.TestInstalled),
            UninstallName = Text.Format(Strings.Library_UninstallName, ("game", game.Name)),
            Action = action,
            ActionIcon = icon,
            ActionRequest = request,
            RuntimeFiles = RuntimeFilesOf(install),
            Licenses = LicensesOf(install),
            Features = FeaturesOf(install, install.Project),
        };
    }

    // An install of a project that offers add-ons, or several variants of a release from before them, has a line of its
    // own, and so has the project a new install takes; an install whose game or project is gone, or a test, has none.
    private FeaturePanel? FeaturesOf(InstallView? install, ProjectOfferView? project)
    {
        if (project is not { Features.Count: > 0 } offer || (offer.IsLegacy && offer.Features.Count < 2)
            || install?.Status.State is MyGameState.Orphaned or MyGameState.TestInstalled)
        {
            return null;
        }

        var id = install?.Id ?? offer.ProjectId;
        if (!_features.TryGetValue(id, out var panel) || panel.Project != offer.ProjectId)
        {
            _features[id] = panel = new FeaturePanel(install?.Id, offer.ProjectId);
            panel.PropertyChanged += OnFeaturesChanged;
        }

        // An add-on the install went without on this release stays off; turning it on again would only leave it out again.
        var leftOut = install is not null && install.ReleaseId == offer.Release?.Id
            ? install.LeftOut.Select(feature => feature.Key).Where(key => offer.Features.Any(feature => feature.Key == key && !feature.IsOn))
                .ToHashSet(StringComparer.Ordinal)
            : [];
        panel.Follow(offer.Features, offer.IsLegacy ? Strings.GamePage_RowVariant : null, leftOut, pill => Turned(panel, pill));
        // An install changes its add-ons through the current release, so only while that is its own or an update of it.
        panel.CanChange = _job is null && install?.Status.Update is null or UpdateState.UpToDate or UpdateState.Available;
        if (!panel.CanChange)
        {
            panel.IsOpen = false;
            panel.Reset();
        }

        (panel.Line, panel.LineEmphasis) = Line(install, offer, panel);
        var changed = install is not null && panel.HasChanges;
        panel.ShowsApply = changed && install!.Status.State == MyGameState.Installed;
        panel.ApplyWith = !changed ? ""
            : install!.Status.State == MyGameState.UpdateAvailable ? Strings.GamePage_ApplyWithUpdate
            : install.Status.State == MyGameState.NeedsRepair ? Strings.GamePage_ApplyWithRepair
            : "";
        if (panel.ShowsApply)
        {
            var changes = string.Join(", ", panel.Pills.Where(pill => pill.IsChanged && (pill.IsOn || panel.Toggles.Contains(pill)))
                .Select(pill => Text.Format(Strings.GamePage_ChangeItem,
                    ("kind", !panel.Toggles.Contains(pill) ? "other" : pill.IsOn ? "add" : "remove"), ("name", pill.Name))));
            (panel.SummaryLead, panel.SummaryChanges) = Around(Text.Format(Strings.GamePage_ChangeSummary, ("changes", changes)), changes);
            var download = Math.Max(0, Extra(panel) ?? 0);
            panel.SummaryCaption = Caption(Strings.GamePage_Caption, offer, download > 0 ? download : (long?)null);
        }

        return panel;
    }

    // An install's line names what it has; the line of a project not installed names what installing takes.
    private static (string Line, string Emphasis) Line(InstallView? install, ProjectOfferView project, FeaturePanel panel)
    {
        if (project.IsLegacy)
        {
            var variant = install is null ? panel.Pills.FirstOrDefault(pill => pill.IsOn)?.Name ?? "" : install.VariantName ?? "";
            return (Text.Format(Strings.GamePage_VariantLine, ("variant", variant)), variant);
        }

        var names = install is null ? panel.Pills.Where(pill => pill.IsOn).Select(pill => pill.Name).ToList()
            : install.Features.Select(feature => feature.Name).ToList();
        var quoted = string.Join(", ", names.Select(Quoted));
        return (Text.Format(Strings.GamePage_FeaturesLine, ("count", names.Count), ("names", quoted)), quoted);
    }

    private static string Quoted(string name) => Text.Format(Strings.GamePage_FeatureQuoted, ("name", name));

    // A sentence split where the part it quotes starts, so that part can be drawn apart.
    private static (string Lead, string Quote) Around(string sentence, string quote)
    {
        var at = quote.Length == 0 ? -1 : sentence.IndexOf(quote, StringComparison.Ordinal);
        return at < 0 ? (sentence, "") : (sentence[..at], sentence[at..]);
    }

    // The same list stays while the install declares the same files, so a state that changes nothing else folds nothing.
    private RuntimeFilesView? RuntimeFilesOf(InstallView install)
    {
        if (install.RuntimeFiles.Count == 0)
        {
            _runtimeFiles.Remove(install.Id);
            return null;
        }

        if (!_runtimeFiles.TryGetValue(install.Id, out var files) || !files.Paths.SequenceEqual(install.RuntimeFiles, StringComparer.Ordinal))
        {
            _runtimeFiles[install.Id] = files = new RuntimeFilesView(install.RuntimeFiles) { AreShown = files?.AreShown ?? false };
        }

        return files;
    }

    // The same list stays while the install's packages name the same software, so a state that changes nothing else folds
    // nothing.
    private LicensesView? LicensesOf(InstallView install)
    {
        if (install.ThirdPartyNotices.Count == 0)
        {
            _licenses.Remove(install.Id);
            return null;
        }

        if (!_licenses.TryGetValue(install.Id, out var licenses) || !licenses.Notices.SequenceEqual(install.ThirdPartyNotices))
        {
            _licenses[install.Id] = licenses = new LicensesView(install.ThirdPartyNotices) { AreShown = licenses?.AreShown ?? false };
        }

        return licenses;
    }

    private void ShowAutomation(GameView game)
    {
        var installed = game.Installs.Count > 0;
        var orphaned = game.Installs.Count > 0 && game.Installs.All(install => install.Status.State is MyGameState.Orphaned or MyGameState.TestInstalled);
        ShowsOfficial = !installed && game.OffersTargetLanguage;
        ShowsAutomation = !ShowsOfficial && !orphaned && (installed || game.Projects.Count > 0);
        AutomationLabel = installed ? Strings.GamePage_RowUpdates : Strings.GamePage_RowInstall;
        AutomationTitle = installed ? Strings.GamePage_AutoUpdate : Strings.GamePage_AutoInstall;
        var own = installed ? game.Preferences.AutoUpdate : game.Preferences.AutoInstall;
        var on = AutomaticNow(game);
        AutomationHint = Text.Format(Strings.GamePage_AutoHint, ("source", own is null ? "settings" : "own"), ("kind", installed ? "update" : "install"),
            ("on", on ? "true" : "false"), ("choice", game.Project is null && game.Projects.Count > 1 ? "true" : "false"));
        IsAutomatic = on;
    }

    private bool AutomaticNow(GameView game)
    {
        var settings = _services.Agent.State.Settings;
        return game.Installs.Count > 0
            ? game.Preferences.AutoUpdate ?? settings.AutoUpdateProjects
            : game.Preferences.AutoInstall ?? settings.AutoInstallProjects;
    }

    private void ShowChoices(GameView game)
    {
        var installed = game.Installs.Select(install => install.ProjectId).ToHashSet();
        var offersMore = game.Installs.Count == 0 || (!game.InstallReplaces && game.InstallRefusal is null);
        ShowsChoices = game.Projects.Count > 1 && offersMore;
        ChoicesText = Strings.GamePage_CompareText;
        var choices = game.Projects.Select(project =>
        {
            var version = GameWording.Version(project.Release);
            var released = project.Release is { } release ? GameWording.Released(release, _services.Clock) : "";
            return new ProjectChoice(project.ProjectId, project.Team.Name, project.Team.Avatar, project.Status?.Name ?? "", project.Status?.Color ?? "",
                version.Length > 0 ? Text.Format(Strings.GamePage_Version, ("version", version)) : "", released)
            {
                IsChosen = game.Project?.ProjectId == project.ProjectId,
                IsInstalled = installed.Contains(project.ProjectId),
                AccessibleName = Text.Format(Strings.GamePage_ChoiceName, ("team", project.Team.Name), ("status", project.Status?.Name ?? ""),
                    ("version", version)),
            };
        }).ToList();
        Sync(Choices, choices);
    }

    private void ShowSaved(InstallView? install)
    {
        ShowsSavedInfo = install is { Status: { State: MyGameState.Orphaned, Orphan: OrphanReason.NotInCatalog } };
        (SavedFeaturesLabel, SavedFeatures) = install switch
        {
            { Features.Count: > 0 } => (Strings.GamePage_InfoFeatures, string.Join(" · ", install.Features.Select(feature => feature.Name))),
            { VariantName: { Length: > 0 } variant } => (Strings.GamePage_RowVariant, variant),
            _ => ("", ""),
        };
        SavedPackage = install is null ? "" : string.Join(Environment.NewLine,
            install.PackageFileNames.Count > 0 ? install.PackageFileNames : [install.PackageFileName]);
        SavedFiles = install is null ? "" : Formats.Number(install.FileCount);
    }

    // A cancel keeps the step that comes next as its action, as cancelling leaves the way things were; closing an uninstall's
    // result shows that step instead.
    private NextStep? NextStep(GameView game, AppStateView state, InstallView? install, ProjectOfferView? project, string store)
    {
        if (_job is { } job)
        {
            return JobStep(game, state, job, store);
        }

        var next = Next(game, state, install, project, store);
        return _result switch
        {
            null => next,
            { Kind: StepKind.Cancelled } result when next is { Primary: not null } => result with
            {
                Primary = next.Primary,
                PrimaryIcon = next.PrimaryIcon,
                PrimaryCaption = next.PrimaryCaption,
                PrimaryRequest = next.PrimaryRequest,
            },
            { } result => result,
        };
    }

    private NextStep? Next(GameView game, AppStateView state, InstallView? install, ProjectOfferView? project, string store)
    {
        var offline = state.Catalogue.Failure is not null;
        var failure = install?.LastFailure ?? game.LastFailure;
        if (failure is not null)
        {
            return Failed(game, install, failure);
        }

        if (install is not null)
        {
            switch (install.Status.State)
            {
                case MyGameState.Orphaned when install.Status.Orphan == OrphanReason.GameNotDetected:
                    return Outlined(StepKind.Gone, Tone.Warning, Icons.TriangleAlert, Strings.GamePage_GoneTitle, Strings.GamePage_GoneText);
                case MyGameState.Orphaned when install.Status.Orphan == OrphanReason.GameMoved:
                    var drive = Path.GetPathRoot(game.Detected?.InstallDirectory ?? "")?.TrimEnd('\\', '/') ?? "";
                    return Outlined(StepKind.Moved, Tone.Warning, Icons.FolderInput, Strings.GamePage_MovedTitle,
                        Text.Format(Strings.GamePage_MovedText, ("store", store), ("drive", drive)));
                case MyGameState.Orphaned:
                    return Outlined(StepKind.NotInCatalogue, Tone.Info, Icons.Unlink, Strings.GamePage_NotInCatalogueTitle,
                        Strings.GamePage_NotInCatalogueText);
                case MyGameState.BackupsUnavailable:
                    return new NextStep(StepKind.Backups, Tone.Warning, Icons.TriangleAlert, Strings.GamePage_BackupsTitle,
                        Text.Format(Strings.GamePage_BackupsText, ("folder", install.Status.BackupDirectory ?? install.BackupDirectory)));
                case MyGameState.TestInstalled:
                    return Outlined(StepKind.Test, Tone.Neutral, Icons.Package, Strings.GamePage_TestTitle, Strings.GamePage_TestText);
            }
        }

        if (game.Detected is { } detected && detected.State != GameInstallState.Installed)
        {
            return new NextStep(StepKind.NotReady, Tone.Neutral, Icons.Clock, Strings.GamePage_NotReadyTitle,
                Text.Format(Strings.GamePage_NotReadyText, ("state", GameWording.GameState(detected.State, detected.Reason, game.Key.Store)),
                    ("store", store), ("installed", install is null ? "false" : "true")));
        }

        if (install is not null)
        {
            return Installed(game, install, project, offline);
        }

        if (project is null)
        {
            return game.Projects.Count > 1
                ? new NextStep(StepKind.Choose, Tone.Info, Icons.Users, Strings.GamePage_ChooseTitle,
                    Text.Format(Strings.GamePage_ChooseText, ("count", game.Projects.Count)))
                {
                    Meta = Strings.GamePage_ChoosePreferred,
                }
                : null;
        }

        if (game.InstallRefusal is { } refusal)
        {
            return new NextStep(StepKind.Refused, Tone.Neutral, Icons.Ban, Strings.GamePage_RefusedTitle,
                GameWording.FailureReason(new JobFailureView(refusal), game.Key.Store, game.Name));
        }

        if (project.Unavailable is not null)
        {
            return new NextStep(StepKind.Unavailable, Tone.Neutral, Icons.Ban, Strings.GamePage_UnavailableTitle,
                Text.Format(Strings.GamePage_Unavailable, ("reason", project.Unavailable?.ToString() ?? "")));
        }

        var size = NewSize(project);
        return new NextStep(StepKind.Install, Tone.Accent, Icons.Download, Strings.GamePage_InstallTitle,
            Text.Format(Strings.GamePage_InstallText, ("duration", size switch
            {
                null or < QuickPackage => "short",
                < MinutePackage => "minute",
                _ => "long",
            })))
        {
            Meta = Released(project),
            MetaWarning = project.IsOutdated ? Strings.GamePage_Outdated : "",
            OffersChanges = Details is { Changes.IsEmpty: false },
            Primary = Strings.GamePage_Install,
            PrimaryIcon = Icons.Download,
            PrimaryCaption = Caption(Strings.GamePage_Caption, project, size),
            PrimaryRequest = new InstallProject(_key, project.ProjectId),
        };
    }

    private NextStep Installed(GameView game, InstallView install, ProjectOfferView? project, bool offline)
    {
        // Pills the player turned go with the pending update or repair, so its button applies them too.
        var panel = _features.GetValueOrDefault(install.Id);
        var turned = panel is { HasChanges: true } ? new ApplyFeatures(install.Id, panel.Changes()) : null;
        if (install.Status.State == MyGameState.NeedsRepair)
        {
            var size = PendingSize(install, panel);
            return new NextStep(StepKind.Repair, Tone.Warning, Icons.Wrench, Strings.GamePage_RepairTitle,
                Text.Format(Strings.GamePage_RepairText, ("reason", install.Status.Repair?.ToString() ?? ""),
                    ("store", GameWording.StoreName(game.Key.Store))))
            {
                Primary = Strings.GamePage_Repair,
                PrimaryIcon = Icons.Wrench,
                PrimaryCaption = size > 0 && project is not null ? Caption(Strings.GamePage_Caption, project, size) : Strings.GamePage_RepairCaption,
                PrimaryRequest = turned ?? (Message)new RepairProject(install.Id),
            };
        }

        if (install.Status.State == MyGameState.UpdateAvailable && project is not null)
        {
            var size = PendingSize(install, panel);
            var replaced = install.Status.UpdateKind == UpdateKind.ReplacedPackage;
            var caption = Caption(replaced ? Strings.GamePage_Caption : Strings.GamePage_CaptionTo, project, size);
            return offline
                ? new NextStep(StepKind.Offline, Tone.Warning, Icons.WifiOff, Strings.Library_OfflineTitle, Strings.GamePage_OfflineText)
                {
                    Meta = Strings.GamePage_OfflineMeta,
                    Primary = Strings.GamePage_Update,
                    PrimaryIcon = Icons.CircleArrowUp,
                    PrimaryCaption = Text.Format(Strings.GamePage_CaptionWaits, ("caption", caption)),
                    PrimaryRequest = turned ?? (Message)new UpdateProject(install.Id),
                }
                : new NextStep(StepKind.Update, Tone.Info, Icons.CircleArrowUp,
                    replaced ? Strings.GamePage_UpdateFixTitle : Strings.GamePage_UpdateTitle, Strings.GamePage_UpdateText)
                {
                    Warning = Drops(install, panel),
                    Meta = Released(project),
                    OffersChanges = !replaced && Details is { Changes.IsEmpty: false },
                    Primary = Strings.GamePage_Update,
                    PrimaryIcon = Icons.CircleArrowUp,
                    PrimaryCaption = caption,
                    PrimaryRequest = turned ?? (Message)new UpdateProject(install.Id),
                };
        }

        if (install.Status.Update == UpdateState.NotForThisGame)
        {
            return new NextStep(StepKind.Unavailable, Tone.Neutral, Icons.Ban, Strings.GamePage_UnavailableTitle, Strings.GamePage_NotForThisGame);
        }

        return new NextStep(StepKind.Current, Tone.Success, Icons.Check, Strings.GamePage_CurrentTitle,
            Text.Format(Strings.GamePage_CurrentText, ("auto", AutomaticNow(game) ? "true" : "false")));
    }

    private static NextStep JobStep(GameView game, AppStateView state, JobView job, string store)
    {
        var cancel = new Func<NextStep, NextStep>(step => step with
        {
            Secondary = Strings.Dialog_Cancel,
            SecondaryIcon = Icons.X,
            SecondaryCancels = true,
        });
        switch (job.Stage)
        {
            case JobStage.Queued:
            case JobStage.Waiting when job.WaitReason is JobWaitReason.Download:
                var other = state.Jobs.FirstOrDefault(entry => entry.Id != job.Id && entry.Stage is not (JobStage.Queued or JobStage.Waiting));
                return cancel(new NextStep(StepKind.Queued, Tone.Accent, Icons.Hourglass, Strings.Library_JobQueued, other is null
                    ? Text.Format(Strings.GamePage_QueuedText, ("kind", job.Kind.ToString()))
                    : Text.Format(Strings.GamePage_QueuedAfter, ("kind", job.Kind.ToString()), ("other", other.GameName))));
            case JobStage.Waiting when job.WaitReason is JobWaitReason.GameRunning:
                var processes = job.RunningProcesses.Count > 0 ? job.RunningProcesses : game.RunningProcesses;
                return cancel(new NextStep(StepKind.Waiting, Tone.Accent, Icons.Clock, Strings.GamePage_WaitingGameTitle,
                    Text.Format(Strings.GamePage_WaitingGameText, ("processes", processes.Count > 0 ? string.Join(", ", processes) : "none"), ("kind", job.Kind.ToString()))));
            case JobStage.Waiting:
                return cancel(new NextStep(StepKind.Waiting, Tone.Accent, Icons.Hourglass,
                    job.WaitReason == JobWaitReason.Recovery ? Strings.Library_JobWaitingForRecovery : Text.Format(Strings.Library_JobWaitingForStore, ("store", store)),
                    job.WaitReason == JobWaitReason.Recovery ? Strings.GamePage_WaitingRecoveryText : Text.Format(Strings.GamePage_WaitingStoreText, ("store", store))));
            default:
                var title = job.Stage switch
                {
                    JobStage.RollingBack => Strings.Shell_JobRollingBack,
                    JobStage.RestoringPrevious => Strings.Shell_JobRestoringPrevious,
                    _ => Text.Format(Strings.GamePage_JobTitle, ("kind", job.Kind.ToString())),
                };
                return cancel(new NextStep(StepKind.Job, Tone.Accent, Icons.LoaderCircle, title, Strings.GamePage_DontRun) { OffersHelp = true });
        }
    }

    private NextStep Failed(GameView game, InstallView? install, FailureView failure)
    {
        var reason = failure.LostPrevious
            ? Strings.Library_PreviousLost
            : GameWording.FailureReason(failure.Failure, game.Key.Store, game.Name);
        var version = install is null ? "" : GameWording.Version(install);
        var kept = install is not null && failure.Kind is JobKind.Update or JobKind.Repair && !failure.LostPrevious && version.Length > 0
            ? " " + Text.Format(Strings.GamePage_FailedKept, ("version", version))
            : "";
        var (request, caption) = (failure.Kind, install) switch
        {
            // Trying again is refused the same way until the player uninstalls and the store checks the game's files.
            _ when failure.Failure?.Reason == JobFailureReason.KeptFilesBlockReplacement => (null, ""),
            (JobKind.Update, { } failed) when failed.Project is { } project =>
                ((Message?)new UpdateProject(failed.Id), Caption(Strings.GamePage_CaptionTo, project, failed.Status.UpdateSize)),
            (JobKind.Repair, { } failed) => (new RepairProject(failed.Id), Strings.GamePage_RepairCaption),
            (JobKind.Uninstall, { } failed) => (new UninstallProject(failed.Id), ""),
            // A change of add-ons tries again with the choices the agent kept.
            (JobKind.Install, { } failed) when failed.Project is { } project =>
                (new ApplyFeatures(failed.Id, new Dictionary<string, bool?>()), Caption(Strings.GamePage_Caption, project, failed.Status.UpdateSize)),
            (_, null) when game.Project is { } project =>
                (new InstallProject(_key, project.ProjectId), Caption(Strings.GamePage_Caption, project, NewSize(project))),
            _ => (null, ""),
        };
        return new NextStep(StepKind.Failed, Tone.Danger, Icons.CircleX,
            failure.LostPrevious ? Strings.GamePage_LostTitle : Text.Format(Strings.GamePage_FailedTitle, ("kind", failure.Kind.ToString())),
            reason + kept)
        {
            CheckFiles = failure.Failure?.Reason == JobFailureReason.KeptFilesPending ? PageLinks.ValidateFiles(_key) : null,
            CheckFilesLabel = CheckFilesLabel(),
            Meta = Formats.RelativeTime(failure.At, _services.Clock),
            OffersHelp = true,
            Primary = request is null ? null : Strings.Library_Retry,
            PrimaryIcon = Icons.RotateCcw,
            PrimaryCaption = caption,
            PrimaryRequest = request,
            CanDismiss = true,
            DismissRequest = new DismissFailure(_key, install?.LastFailure is null ? null : install.Id),
        };
    }

    private static NextStep Cancelled(JobView job, GameView? game)
    {
        var install = game?.Installs.FirstOrDefault(entry => entry.Id == job.InstallId);
        return new NextStep(StepKind.Cancelled, Tone.Neutral, Icons.Undo, Text.Format(Strings.GamePage_CancelledTitle, ("kind", job.Kind.ToString())),
            Text.Format(Strings.GamePage_CancelledText, ("kind", job.Kind.ToString()), ("version", install is null ? "" : GameWording.Version(install))))
        {
            CanDismiss = true,
        };
    }

    // Told by the most pressing reason the uninstall left files for; its tone says whether the player has anything to do.
    private NextStep Removed(bool listed, JobEnded? ended)
    {
        var kept = ended?.KeptFiles ?? [];
        var reason = ended is { Forgotten: true } ? null : KeptReasons.MostPressing(kept.Select(files => files.Reason));
        var told = KeptReasons.CaseOf(reason);
        var text = ended is { Forgotten: true } ? Strings.GamePage_RemovedForgotten
            : reason is null ? Strings.GamePage_RemovedText
            : Text.Format(Strings.GamePage_RemovedKept, ("reason", told), ("store", GameWording.StoreName(_key.Store)),
                ("count", kept.Where(files => KeptReasons.CaseOf(files.Reason) == told).Sum(files => files.Count)));
        if (!listed)
        {
            text += " " + Text.Format(Strings.GamePage_RemovedGone, ("library", Strings.Shell_Library));
        }

        var advises = reason is { } most && KeptReasons.AdvisesStoreCheck(most);
        var (tone, icon) = ended is { Forgotten: true } ? (Tone.Neutral, Icons.Folder)
            : reason == KeptReason.OriginalUnknown ? (Tone.Info, Icons.Info)
            : advises ? (Tone.Warning, Icons.TriangleAlert)
            : (Tone.Success, Icons.CircleCheck);
        return new NextStep(StepKind.Removed, tone, icon, Strings.GamePage_RemovedTitle, text)
        {
            CheckFiles = advises ? PageLinks.ValidateFiles(_key) : null,
            CheckFilesLabel = CheckFilesLabel(),
            Secondary = listed ? null : Text.Format(Strings.GamePage_BackToLibrary, ("library", Strings.Shell_Library)),
            SecondaryIcon = Icons.ArrowLeft,
            SecondaryGoesBack = !listed,
            CanDismiss = listed,
        };
    }

    private string CheckFilesLabel() => Text.Format(Strings.GamePage_CheckFiles, ("store", GameWording.StoreName(_key.Store)));

    private static NextStep Outlined(StepKind kind, Tone tone, Geometry icon, string title, string text) =>
        new(kind, tone, icon, title, text)
        {
            Secondary = Strings.GamePage_UninstallProject,
            SecondaryIcon = Icons.Trash,
            SecondaryUninstalls = true,
        };

    private void ShowProgress(JobView job)
    {
        var stages = Stages(job);
        var current = Math.Max(0, Array.FindIndex(stages, stage => stage.Stages.Contains(job.Stage)));
        var steps = stages.Select((stage, index) => new JobStepView(index + 1,
            Text.Format(Strings.GamePage_Step, ("step", stage.Name)),
            index < current ? StepState.Done : index == current ? StepState.Current : StepState.Pending)).ToList();
        Sync(Steps, steps);

        var stage = GameWording.Stage(job);
        var speed = Speed(job);
        var parts = new List<string> { stage };
        if (job.BytesTotal is { } total && job.Stage == JobStage.Downloading)
        {
            parts.Add(Text.Format(Strings.GamePage_BytesOf, ("done", Formats.FileSize(job.BytesDone)), ("total", Formats.FileSize(total))));
            if (speed is { } bytesPerSecond && bytesPerSecond > 0)
            {
                parts.Add(Text.Format(Strings.GamePage_Speed, ("speed", Formats.FileSize((long)bytesPerSecond))));
                parts.Add(Formats.TimeRemaining(TimeSpan.FromSeconds((total - job.BytesDone) / bytesPerSecond)));
            }
        }

        StageText = GameWording.Join(parts);
        PercentText = GameWording.Percent(job) ?? "";
        Progress = Math.Clamp(job.Fraction ?? 0, 0, 1) * 100;
        IsIndeterminate = job.Fraction is null;
    }

    // The bytes per second over the last few seconds of a download, from the progress it reported.
    private double? Speed(JobView job)
    {
        var now = _services.Clock.GetUtcNow();
        if (job.Stage != JobStage.Downloading)
        {
            _samples.Clear();
            return null;
        }

        _samples.Add((now, job.BytesDone));
        _samples.RemoveAll(sample => now - sample.At > TimeSpan.FromSeconds(4));
        var (firstAt, firstBytes) = _samples[0];
        var seconds = (now - firstAt).TotalSeconds;
        return seconds < 0.5 ? null : (job.BytesDone - firstBytes) / seconds;
    }

    // A change of an install's add-ons removes it before installing anew, as an update does.
    private static (string Name, JobStage[] Stages)[] Stages(JobView job) => job.Kind switch
    {
        JobKind.Install when job.InstallId is null =>
        [
            ("Download", [JobStage.Preparing, JobStage.Downloading]),
            ("Check", [JobStage.Checking]),
            ("Install", [JobStage.Installing, JobStage.Verifying, JobStage.RollingBack]),
        ],
        JobKind.TestInstall =>
        [
            ("Download", [JobStage.Preparing, JobStage.Downloading]),
            ("Check", [JobStage.Checking]),
            ("Install", [JobStage.Installing, JobStage.Verifying, JobStage.RollingBack]),
        ],
        JobKind.Update or JobKind.Repair or JobKind.Install =>
        [
            ("Download", [JobStage.Preparing, JobStage.Downloading]),
            ("Check", [JobStage.Checking]),
            ("Remove", [JobStage.Uninstalling]),
            ("Install", [JobStage.Installing, JobStage.Verifying, JobStage.RollingBack, JobStage.RestoringPrevious]),
        ],
        JobKind.Uninstall =>
        [
            ("Check", [JobStage.Checking]),
            ("Restore", [JobStage.Uninstalling, JobStage.Installing, JobStage.Verifying, JobStage.RollingBack]),
        ],
        _ => [],
    };

    private void LoadDetails(ProjectOfferView? project)
    {
        if (project is null || project.ProjectId == _detailsFor)
        {
            return;
        }

        _detailsFor = project.ProjectId;
        _details?.Cancel();
        _details?.Dispose();
        _details = new CancellationTokenSource();
        _ = LoadDetailsAsync(project, _details.Token);
    }

    private async Task LoadDetailsAsync(ProjectOfferView project, CancellationToken cancellationToken)
    {
        IsLoadingDetails = Details is null;
        var (sent, answer) = await AgentRequests.AskAsync(_services, new GetProjectPage(project.GameSlug, project.Team.Slug, project.ProjectId))
            .ConfigureAwait(true);
        if (cancellationToken.IsCancellationRequested)
        {
            return;
        }

        var details = sent && answer is ProjectPageResult { Page: { } page }
            ? await Task.Run(() => ProjectDetails.Read(page), cancellationToken).ConfigureAwait(true)
            : null;
        if (cancellationToken.IsCancellationRequested)
        {
            return;
        }

        IsLoadingDetails = false;
        Details = details;
        Story.Details = details;
        NotesTitle = Text.Format(Strings.GamePage_NotesTitle, ("team", project.Team.Name));
        Compose();
    }

    // The install the page is about: the one a job works on, then one that needs the player, then the project's own.
    private static (InstallView? Install, ProjectOfferView? Project) Subject(GameView game)
    {
        var install = game.Installs.FirstOrDefault(install => install.Id == game.Job?.InstallId)
            ?? game.Installs.FirstOrDefault(install => install.LastFailure is not null)
            ?? game.Installs.FirstOrDefault(install => install.NeedsAttention)
            ?? game.Installs.FirstOrDefault(install => install.Status.State == MyGameState.UpdateAvailable)
            ?? game.Installs.FirstOrDefault(install => install.ProjectId == game.Project?.ProjectId)
            ?? (game.Installs.Count > 0 ? game.Installs[0] : null);
        return (install, install is null ? game.Project : install.Project);
    }

    private InstallView? SubjectInstall() => _game is null ? null : Subject(_game).Install;

    // What installing the project downloads: its base and the add-ons the line names.
    private long? NewSize(ProjectOfferView project)
    {
        var features = NewFeatures is { } panel && panel.Project == project.ProjectId
            ? panel.Pills.Where(pill => pill.IsOn).Select(pill => pill.Size)
            : project.Features.Where(feature => feature.IsOn).Select(feature => feature.Package.Size);
        long? total = project.Base is { } package ? package.Size : 0;
        foreach (var size in features)
        {
            total += size;
        }

        return total;
    }

    // What the pending update or repair downloads, with the add-ons the player turned since.
    private static long? PendingSize(InstallView install, FeaturePanel? panel) =>
        install.Status.UpdateSize is { } size ? Math.Max(0, size + (panel is null ? 0 : Extra(panel) ?? 0)) : null;

    // The bytes the pills the player turned add to what the plan downloads, or take from it; null when a size is unknown.
    private static long? Extra(FeaturePanel panel)
    {
        long? extra = 0;
        foreach (var pill in panel.Pills.Where(pill => pill.IsChanged && !pill.IsPresent))
        {
            extra += pill.IsOn ? pill.Size : -pill.Size;
        }

        return extra;
    }

    // The installed add-ons the update goes without, unless the player turned their pills on again.
    private static string Drops(InstallView install, FeaturePanel? panel)
    {
        var drops = install.Status.UpdateDrops
            .Where(drop => panel?.Pills.FirstOrDefault(pill => pill.Key == drop.Key) is not { IsOn: true }).ToList();
        return drops.Count == 0 ? ""
            : Text.Format(Strings.GamePage_UpdateDrops, ("count", drops.Count), ("names", string.Join(", ", drops.Select(drop => Quoted(drop.Name)))));
    }

    private static string Caption(string pattern, ProjectOfferView project, long? size) =>
        Text.Format(pattern, ("version", GameWording.Version(project.Release)), ("size", size is { } bytes ? Formats.FileSize(bytes) : ""),
            ("hasSize", size is null ? "false" : "true"));

    private string Released(ProjectOfferView project) => project.Release is { } release ? GameWording.Released(release, _services.Clock) : "";

    private string When(DateTimeOffset at) =>
        _services.Clock.GetUtcNow() - at < TimeSpan.FromHours(1)
            ? Formats.RelativeTime(at, _services.Clock)
            : Text.Format(Strings.GamePage_When, ("age", Formats.CalendarAge(at, _services.Clock)), ("date", Formats.LongDate(at, _services.Clock.LocalTimeZone)));

    private static void Sync<T>(ObservableCollection<T> target, IReadOnlyList<T> items)
    {
        if (target.SequenceEqual(items))
        {
            return;
        }

        target.Clear();
        foreach (var item in items)
        {
            target.Add(item);
        }
    }
}
