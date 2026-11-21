using System.ComponentModel;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JustModIt.Manager.Core.Catalog;
using JustModIt.Manager.Core.Settings;
using JustModIt.Manager.Localization;
using JustModIt.Manager.Protocol.Messages;
using JustModIt.Manager.Protocol.Transport;
using JustModIt.Manager.Protocol.Views;
using JustModIt.Manager.Ui.Connection;
using JustModIt.Manager.Ui.Navigation;
using JustModIt.Manager.Ui.Pages.Library;
using JustModIt.Manager.Ui.Pages.Settings;
using JustModIt.Manager.Ui.Theme;

namespace JustModIt.Manager.Ui.Pages.Onboarding;

/// <summary>What happens without asking: one choice for automatic installs, updates and reapplying after a game update.</summary>
public enum AutomationChoice
{
    /// <summary>The three settings were set apart from each other, which none of the choices matches.</summary>
    Custom,
    Everything,
    UpdatesOnly,
    Nothing,
}

/// <summary>
/// The first-run setup: its steps in a card over the catalogue's covers, with the Steam step where a gamescope session opens
/// the window from Steam's library, and in Game Mode without the Steam and package creator's steps. Every choice is saved
/// at once, together with the step it was made on, so a setup left halfway opens there again; finishing or skipping it
/// completes the setup with the choices made, the rest keeping their defaults.
/// </summary>
public sealed partial class OnboardingViewModel : PageViewModel, IDisposable
{
    // Enough covers for a wall that fills a large window without the same cover next to itself.
    private const int WallCovers = 40;

    private readonly UiServices _services;

    // Every step in the order the setup shows them, whichever apply.
    private readonly List<OnboardingStep> _all;
    private SettingsView? _sent;
    private bool _syncing;
    private DateTimeOffset? _coversFor;

    /// <summary>Creates the setup on the step it was left on, with the choices the agent's settings hold.</summary>
    /// <param name="services">What the setup talks to.</param>
    public OnboardingViewModel(UiServices services)
    {
        _services = services;
        SteamShortcut = new SteamShortcutViewModel(services);
        SteamShortcut.Refresh();
        Ready = new ReadyStep(this, services.Agent);
        Background = new BackgroundStep(this);
        _all = [new WelcomeStep(this), new AutomationStep(this), Background, new SteamShortcutStep(this), new CreatorStep(this), Ready];
        Steps = [.. _all.Where(IsShown)];
        Background.Refresh(Agent.State.Activity.WindowReturn);
        Sync(Agent.State.Settings);
        var saved = _all.FindIndex(step => step.Id == Agent.State.Settings.OnboardingStep);
        CurrentStep = ShownFrom(saved < 0 ? _all.Count - 1 : saved);
        MarkSteps();
        Ready.Refresh();
        Agent.PropertyChanged += OnAgentChanged;
        _ = LoadCoversAsync();
    }

    public override string Title => Strings.Onboarding_Title;

    /// <summary>The steps that apply, in order.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFirstStep), nameof(IsLastStep), nameof(NextText), nameof(NextTip), nameof(NextIcon),
        nameof(Progress))]
    public partial IReadOnlyList<OnboardingStep> Steps { get; private set; }

    public ReadyStep Ready { get; }

    public BackgroundStep Background { get; }

    /// <summary>The app's shortcut in Steam's library, which the Steam step adds and the last step sums up.</summary>
    public SteamShortcutViewModel SteamShortcut { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFirstStep), nameof(IsLastStep), nameof(NextText), nameof(NextTip), nameof(NextIcon),
        nameof(Progress))]
    [NotifyCanExecuteChangedFor(nameof(BackCommand))]
    public partial OnboardingStep CurrentStep { get; private set; }

    /// <summary>The step shown came before the one left, so the steps slide the other way.</summary>
    [ObservableProperty]
    public partial bool IsGoingBack { get; private set; }

    public bool IsFirstStep => Index == 0;

    public bool IsLastStep => Index == Steps.Count - 1;

    public string NextText => IsLastStep ? Strings.Onboarding_Start : Strings.Onboarding_Next;

    public string? NextTip => IsLastStep ? null : Strings.Onboarding_NextTip;

    public Geometry NextIcon => IsLastStep ? Icons.Rocket : Icons.ChevronRight;

    /// <summary>Where the setup is, for screen readers: "step 2 of 5".</summary>
    public string Progress => Text.Format(Strings.Onboarding_StepOf, ("step", Index + 1), ("count", Steps.Count));

    /// <summary>The setup was completed and waits for the agent to say so, which closes it.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(NextCommand), nameof(SkipCommand), nameof(BackCommand))]
    public partial bool IsFinishing { get; private set; }

    [ObservableProperty]
    public partial AutomationChoice Automation { get; set; }

    [ObservableProperty]
    public partial bool ShowNotifications { get; set; }

    [ObservableProperty]
    public partial bool StartWithSystem { get; set; }

    [ObservableProperty]
    public partial CloseBehavior CloseBehavior { get; set; }

    [ObservableProperty]
    public partial bool CreatorMode { get; set; }

    /// <summary>The catalogue's covers for the wall behind the card; none until the catalogue has been read.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<string> Covers { get; private set; } = [];

    /// <summary>The app's logo; null when its branding has none.</summary>
    public IImage? Logo => _services.Context.Branding.Logo;

    public string ProductName => _services.Context.ProductName;

    internal IAgent Agent => _services.Agent;

    private int Index => Steps.TakeWhile(step => step != CurrentStep).Count();

    public void Dispose() => Agent.PropertyChanged -= OnAgentChanged;

    public override bool GoBackWithin()
    {
        if (!BackCommand.CanExecute(null))
        {
            return false;
        }

        BackCommand.Execute(null);
        return true;
    }

    partial void OnAutomationChanged(AutomationChoice value) => OnChosen();

    partial void OnShowNotificationsChanged(bool value) => OnChosen();

    partial void OnStartWithSystemChanged(bool value) => OnChosen();

    partial void OnCloseBehaviorChanged(CloseBehavior value) => OnChosen();

    partial void OnCreatorModeChanged(bool value) => OnChosen();

    [RelayCommand(CanExecute = nameof(CanMoveOn))]
    private Task NextAsync()
    {
        if (IsLastStep)
        {
            return FinishAsync();
        }

        GoTo(Index + 1);
        return Task.CompletedTask;
    }

    [RelayCommand(CanExecute = nameof(CanGoBack))]
    private void Back() => GoTo(Index - 1);

    [RelayCommand(CanExecute = nameof(CanMoveOn))]
    private Task SkipAsync() => FinishAsync();

    private bool CanMoveOn() => !IsFinishing;

    private bool CanGoBack() => Index > 0 && !IsFinishing;

    // The shell closes the setup once the agent's settings say it is completed; until then nothing more can be chosen.
    private async Task FinishAsync()
    {
        IsFinishing = true;
        if (!await SaveAsync(complete: true).ConfigureAwait(true))
        {
            IsFinishing = false;
        }
    }

    private void GoTo(int index)
    {
        IsGoingBack = index < Index;
        CurrentStep = Steps[index];
        MarkSteps();
        Ready.Refresh();
        _ = SaveAsync(complete: false);
    }

    private void MarkSteps()
    {
        var current = Index;
        for (var index = 0; index < Steps.Count; index++)
        {
            Steps[index].IsCurrent = index == current;
            Steps[index].IsDone = index < current;
        }
    }

    private bool IsShown(OnboardingStep step) => step switch
    {
        // Game Mode shows only the window its shortcut started, and hides the package creator.
        SteamShortcutStep => SteamShortcut is { IsOffered: true, HasGameSession: true } && !_services.Context.StartedBySteam
            && !_services.Context.InGameMode,
        CreatorStep => !_services.Context.InGameMode,
        _ => true,
    };

    // The first step shown from a step on, whichever apply now; the last always does.
    private OnboardingStep ShownFrom(int position) => _all.Skip(position).First(Steps.Contains);

    // The shortcut's view may arrive after the setup opened, which adds or takes away the Steam step.
    private void ShowSteps()
    {
        List<OnboardingStep> shown = [.. _all.Where(IsShown)];
        if (shown.SequenceEqual(Steps))
        {
            return;
        }

        var current = _all.IndexOf(CurrentStep);
        Steps = shown;
        CurrentStep = ShownFrom(current);
        MarkSteps();
    }

    private void OnChosen()
    {
        Ready.RefreshSummary();
        if (!_syncing)
        {
            _ = SaveAsync(complete: false);
        }
    }

    private async Task<bool> SaveAsync(bool complete)
    {
        var settings = Chosen(Agent.State.Settings) with { OnboardingCompleted = complete };
        _sent = settings;
        try
        {
            await Agent.RequestAsync(new ChangeSettings(settings)).ConfigureAwait(true);
            return true;
        }
        catch (Exception exception) when (exception is IOException or TimeoutException or RequestFailedException)
        {
            // The choices show the agent's settings again, which the request did not change, once the tile that made the
            // choice has finished taking it: a request refused at once would otherwise be undone by the tile's own group.
            if (_sent == settings)
            {
                _sent = null;
                Dispatcher.UIThread.Post(() =>
                {
                    if (_sent is null)
                    {
                        Sync(Agent.State.Settings);
                    }
                });
            }

            return false;
        }
    }

    private SettingsView Chosen(SettingsView settings)
    {
        var (install, update, reapply) = Automation switch
        {
            AutomationChoice.Everything => (true, true, true),
            AutomationChoice.UpdatesOnly => (false, true, true),
            AutomationChoice.Nothing => (false, false, false),
            _ => (settings.AutoInstallProjects, settings.AutoUpdateProjects, settings.ReapplyAfterGameUpdate),
        };
        return settings with
        {
            AutoInstallProjects = install,
            AutoUpdateProjects = update,
            ReapplyAfterGameUpdate = reapply,
            ShowNotifications = ShowNotifications,
            StartWithSystem = StartWithSystem,
            CloseBehavior = CloseBehavior,
            CreatorMode = CreatorMode,
            OnboardingStep = CurrentStep.Id,
        };
    }

    private static bool SameChoices(SettingsView first, SettingsView second) =>
        (first.AutoInstallProjects, first.AutoUpdateProjects, first.ReapplyAfterGameUpdate, first.ShowNotifications,
            first.StartWithSystem, first.CloseBehavior, first.CreatorMode, first.OnboardingStep)
        == (second.AutoInstallProjects, second.AutoUpdateProjects, second.ReapplyAfterGameUpdate, second.ShowNotifications,
            second.StartWithSystem, second.CloseBehavior, second.CreatorMode, second.OnboardingStep);

    private void Sync(SettingsView settings)
    {
        _syncing = true;
        Automation = (settings.AutoInstallProjects, settings.AutoUpdateProjects, settings.ReapplyAfterGameUpdate) switch
        {
            (true, true, true) => AutomationChoice.Everything,
            (false, true, true) => AutomationChoice.UpdatesOnly,
            (false, false, false) => AutomationChoice.Nothing,
            _ => AutomationChoice.Custom,
        };
        ShowNotifications = settings.ShowNotifications;
        StartWithSystem = settings.StartWithSystem;
        CloseBehavior = settings.CloseBehavior;
        CreatorMode = settings.CreatorMode;
        _syncing = false;
    }

    private void OnAgentChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(IAgent.State))
        {
            return;
        }

        // A state the agent made before it took the last choices would take them back on screen for a moment.
        var settings = Agent.State.Settings;
        if (_sent is null)
        {
            Sync(settings);
        }
        else if (SameChoices(settings, _sent))
        {
            _sent = null;
        }

        Background.Refresh(Agent.State.Activity.WindowReturn);
        SteamShortcut.Refresh();
        ShowSteps();
        Ready.Refresh();
        _ = LoadCoversAsync();
    }

    private async Task LoadCoversAsync()
    {
        var fetchedAt = Agent.State.Catalogue.FetchedAt;
        if (Covers.Count > 0 || fetchedAt is null || fetchedAt == _coversFor)
        {
            return;
        }

        _coversFor = fetchedAt;
        // Without an answer the wall stays a glow until the catalogue is read again.
        var query = new GetCataloguePage(new CatalogueQuery { Order = CatalogueOrder.Popular }, 0, WallCovers);
        if (await AgentRequests.AskQuietlyAsync(Agent, query).ConfigureAwait(true) is CataloguePageResult page)
        {
            Covers = [.. page.Games.Select(game => game.Cover).Where(cover => cover.Length > 0)];
        }
    }
}
