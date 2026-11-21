using System.ComponentModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JustModIt.Manager.Core.Automation;
using JustModIt.Manager.Localization;
using JustModIt.Manager.Protocol.Messages;
using JustModIt.Manager.Ui.Connection;
using JustModIt.Manager.Ui.Images;
using JustModIt.Manager.Ui.Navigation;
using JustModIt.Manager.Ui.Overlays;
using JustModIt.Manager.Protocol.Views;
using JustModIt.Manager.Ui.Pages.Library;
using JustModIt.Manager.Ui.Theme;

namespace JustModIt.Manager.Ui.Shell;

/// <summary>An entry of the sidebar.</summary>
/// <param name="section">The section it opens and stands for.</param>
/// <param name="title">Its label.</param>
/// <param name="icon">Its icon.</param>
/// <param name="route">The route it opens.</param>
public sealed partial class NavigationItem(ShellSection section, string title, Geometry icon, Route route) : ObservableObject
{
    public ShellSection Section { get; } = section;

    public string Title { get; } = title;

    public Geometry Icon { get; } = icon;

    public Route Route { get; } = route;

    [ObservableProperty]
    public partial bool IsActive { get; set; }

    [ObservableProperty]
    public partial bool IsVisible { get; set; } = true;
}

/// <summary>
/// The window around the pages: who draws its title bar, the title bar's name and way back, the sidebar, the status card,
/// the first-run setup that takes the whole window until it is done, the calm state shown while the agent cannot be
/// reached, the one-time word that the app closed unexpectedly last time, and in Game Mode the keys the window listens to.
/// </summary>
public sealed partial class ShellViewModel : ObservableObject, IDisposable
{
    private readonly UiServices _services;
    private readonly Func<UiServices, PageViewModel>? _firstRun;
    private readonly NavigationItem _creator;
    private bool _toldUncleanExit;
    private bool _toldWindowCrash;
    private Task _toldUncleanExits = Task.CompletedTask;
    private bool _shownControls;
    private Navigate? _pendingLink;

    /// <summary>Creates the shell, which opens the player's games or the place the window was started for.</summary>
    /// <param name="services">What the shell and its pages talk to.</param>
    /// <param name="firstRun">The first-run setup's page; null when the app has none.</param>
    public ShellViewModel(UiServices services, Func<UiServices, PageViewModel>? firstRun = null)
    {
        _services = services;
        _firstRun = firstRun;
        Status = new StatusCardViewModel(services.Agent, services.Context.Version);
        Primary =
        [
            new(ShellSection.Library, Strings.Shell_Library, Icons.Gamepad, new LibraryRoute()),
            new(ShellSection.Catalogue, Strings.Shell_Catalogue, Icons.Library, new CatalogueRoute()),
        ];
        _creator = new(ShellSection.Creator, Strings.Shell_Creator, Icons.WandSparkles, new CreatorRoute()) { IsVisible = false };
        Secondary = [_creator, new(ShellSection.Settings, Strings.Shell_Settings, Icons.Settings, new SettingsRoute())];
        Wordmark = services.Context.ProductName.ToUpper(Text.Culture);
        Frame = services.Context.Frame;

        Navigator.Navigated += (_, _) => SyncNavigation();
        Agent.PropertyChanged += OnAgentChanged;
        Agent.NavigationRequested += OnNavigationRequested;
        Agent.Noticed += OnNoticed;
        Uninstall.PropertyChanged += OnUninstalled;
        services.Settings.Changed += OnSettingsChanged;
        CloseFlow = new CloseFlow(services.Agent, services.Navigator, services.Dialogs, services.Uninstall);
        _pendingLink = services.Context.Launch.Target is { } launched ? new Navigate(launched) : null;
        Navigator.Open([new LibraryRoute()]);
        ApplyLink();
        ApplyState();
    }

    public Navigator Navigator => _services.Navigator;

    public IAgent Agent => _services.Agent;

    public DialogHost Dialogs => _services.Dialogs;

    public ToastHost Toasts => _services.Toasts;

    /// <summary>What the window's process knows of the app, such as how it started anew.</summary>
    public UiContext Context => _services.Context;

    public IImageLoader Images => _services.Images;

    /// <summary>Uninstalling the app, whose end takes the place of the sidebar and the pages.</summary>
    public AppUninstall Uninstall => _services.Uninstall;

    /// <summary>What closing the window means, by the player's setting.</summary>
    public CloseFlow CloseFlow { get; }

    public StatusCardViewModel Status { get; }

    public IReadOnlyList<NavigationItem> Primary { get; }

    public IReadOnlyList<NavigationItem> Secondary { get; }

    public string ProductName => _services.Context.ProductName;

    /// <summary>The product's name as the title bar shows it, in capitals.</summary>
    public string Wordmark { get; }

    /// <summary>The window's logo, drawn beside the product's name; null when the app's branding has none.</summary>
    public IImage? Logo => _services.Context.Branding.Logo;

    /// <summary>Who draws the window's title bar now: on a Linux desktop, the player's setting as the window shows it.</summary>
    [ObservableProperty]
    public partial WindowFrame Frame { get; private set; }

    /// <summary>The first-run setup, shown instead of the sidebar and the pages until it is done; null once it is.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsPages))]
    public partial PageViewModel? FirstRun { get; private set; }

    public bool ShowsPages => !IsConnecting && FirstRun is null && !IsUninstalled;

    /// <summary>The app is uninstalled, so the window says so in place of everything else and waits for it to be closed.</summary>
    public bool IsUninstalled => Uninstall.Uninstalled is not null;

    public void Dispose()
    {
        _slowConnection?.Dispose();
        _busyPage?.PropertyChanged -= OnBusyPageChanged;
        Agent.PropertyChanged -= OnAgentChanged;
        Agent.NavigationRequested -= OnNavigationRequested;
        Agent.Noticed -= OnNoticed;
        Uninstall.PropertyChanged -= OnUninstalled;
        _services.Settings.Changed -= OnSettingsChanged;
        Status.Dispose();
        (FirstRun as IDisposable)?.Dispose();
    }

    [RelayCommand]
    private void Open(NavigationItem item) => Navigator.Navigate(item.Route);

    // Esc closes the dialog shown, else leaves a view within the page, such as a project's operations, wherever the focus is.
    // In Game Mode it also goes back a step of the setup or a page, since the controller's button that Steam maps to it is the
    // only way back there.
    [RelayCommand]
    private void Escape()
    {
        if (Dialogs.Current is not null)
        {
            Dialogs.CancelCurrent();
        }
        else if (_services.Context.InGameMode && FirstRun is not null)
        {
            FirstRun.GoBackWithin();
        }
        else if (_services.Context.InGameMode && Navigator.GoBackCommand.CanExecute(null))
        {
            Navigator.GoBackCommand.Execute(null);
        }
        else
        {
            Navigator.Current?.GoBackWithin();
        }
    }

    // Seen once, the hint waits under its button, also when the player opened it there first.
    [RelayCommand]
    private async Task ShowControlsAsync(bool openedByItself)
    {
        await Dialogs.ShowAsync(new ControlsHintDialogViewModel(openedByItself)).ConfigureAwait(true);
        if (Agent.HasState && !Agent.State.Settings.ControlsHintShown)
        {
            await AgentRequests.AskQuietlyAsync(Agent, new ChangeSettings(Agent.State.Settings with { ControlsHintShown = true }))
                .ConfigureAwait(true);
        }
    }

    // Once the setup is done, so the hint covers none of its steps, and after the word of an unclean exit.
    private async Task ShowControlsOnceAsync()
    {
        await _toldUncleanExits.ConfigureAwait(true);
        await ShowControlsCommand.ExecuteAsync(true).ConfigureAwait(true);
    }

    // The package creator needs pickers, a file manager and long forms, which Game Mode has no room for.
    private bool ShowsCreator(SettingsView settings) => settings.CreatorMode && !_services.Context.InGameMode;

    private void OnAgentChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(IAgent.Link) or nameof(IAgent.AgentVersion):
                ApplyLink();
                break;
            case nameof(IAgent.State) or nameof(IAgent.HasState):
                ApplyState();
                break;
        }
    }

    // The agent leaves once the app is gone, which is no reason to wait for it.
    private void OnUninstalled(object? sender, PropertyChangedEventArgs e)
    {
        ApplyLink();
        OnPropertyChanged(nameof(IsUninstalled));
        OnPropertyChanged(nameof(ShowsPages));
    }

    private void OnSettingsChanged(object? sender, EventArgs e) => ShowFrame();

    // The frame follows the switch at once, before the agent answers, and goes back with a change the agent refuses.
    private void ShowFrame()
    {
        if (_services.Context.OffersSystemTitleBar && Agent.HasState)
        {
            Frame = _services.Settings.Current.SystemTitleBar ? WindowFrame.System : WindowFrame.Frameless;
        }
    }

    private void OnNavigationRequested(object? sender, Navigate navigate)
    {
        _pendingLink = navigate;
        OpenPendingLink();
    }

    // Where a link leads depends on the player's games, so it waits for the agent's state, under the connecting view; a
    // project link waits for the state that holds them, which an agent just started sends after its first.
    private void OpenPendingLink()
    {
        if (_pendingLink is { Target: var link } navigate && Agent.HasState && (link is not InstallTarget || Agent.State.IsLoaded))
        {
            _pendingLink = null;
            Navigator.Open(AgentLinks.RoutesFor(link, Agent.State, ShowsCreator(Agent.State.Settings)));
            if (link is UninstallAppTarget)
            {
                _ = Uninstall.ConfirmAsync();
            }

            // Only the store's library could open the window, so the page opens later than the link asked for it.
            if (navigate.Waited)
            {
                var game = Navigator.CurrentRoute is GameRoute shown
                    ? Agent.State.Games.FirstOrDefault(view => view.Key == shown.Game)?.Name
                    : null;
                Toasts.Show(new ToastViewModel(ToastTone.Info, Icons.Link, Strings.Shell_LinkWaitedTitle,
                    Text.Format(Strings.Shell_LinkWaited, ("named", game is null ? "false" : "true"), ("game", game ?? ""))));
            }
        }
    }

    private void OnNoticed(object? sender, NoticeView notice)
    {
        // The game's page in front says the same in its step.
        if (notice is { Kind: NoticeKind.StoreCheckAdvised, Game: { } game } && Navigator.CurrentRoute is GameRoute shown && shown.Game == game)
        {
            return;
        }

        Toasts.Show(NoticeToasts.Create(notice,
            about => Navigator.Open(AgentLinks.RoutesFor(new GameTarget(about.Game!.Value.Store.Value, about.Game.Value.StoreGameId),
                Agent.State, ShowsCreator(Agent.State.Settings))),
            () => Navigator.Navigate(new SettingsRoute()),
            Agent.State.SteamUi.RunningGame is null && !_services.Context.InGameMode
                ? () => _ = AgentRequests.SendAsync(_services, new RestartSteam(), Strings.GamePage_RestartSteamFailed)
                : null,
            Agent.State.Volumes));
    }

    private void ApplyState()
    {
        ApplyLink();
        if (!Agent.HasState)
        {
            return;
        }

        var agentExit = _toldUncleanExit ? null : Agent.State.UncleanExit;
        var windowCrash = _toldWindowCrash ? null : _services.Context.PreviousCrash;
        _toldUncleanExit |= agentExit is not null;
        _toldWindowCrash = true;
        if (agentExit is not null || windowCrash is not null)
        {
            _toldUncleanExits = TellUncleanExitsAsync(agentExit, windowCrash);
        }

        OpenPendingLink();

        ShowFrame();
        var settings = Agent.State.Settings;
        _creator.IsVisible = ShowsCreator(settings);
        if (!_creator.IsVisible && Navigator.CurrentRoute?.Section == ShellSection.Creator)
        {
            Navigator.Navigate(new LibraryRoute());
        }

        if (!settings.OnboardingCompleted && FirstRun is null && _firstRun is not null)
        {
            FirstRun = _firstRun(_services);
        }
        else if (settings.OnboardingCompleted && FirstRun is { } done)
        {
            FirstRun = null;
            (done as IDisposable)?.Dispose();
            // The setup ends with a check that fills the player's games, whatever a link opened behind it.
            Navigator.Navigate(new LibraryRoute());
        }

        if (_services.Context.InGameMode && settings is { OnboardingCompleted: true, ControlsHintShown: false } && !_shownControls)
        {
            _shownControls = true;
            _ = ShowControlsOnceAsync();
        }
    }

    // The agent forgets its exit at once, so the next window does not tell it again even if this one closes meanwhile; the
    // window's own crash was its marker's, which this window's start has already replaced.
    private async Task TellUncleanExitsAsync(UncleanExitView? agent, UncleanExitView? window)
    {
        if (agent is not null)
        {
            await AgentRequests.AskQuietlyAsync(Agent, new DismissUncleanExit()).ConfigureAwait(true);
            await Dialogs.ShowAsync(new UncleanExitDialogViewModel(_services, agent)).ConfigureAwait(true);
        }

        if (window is not null)
        {
            await Dialogs.ShowAsync(new UncleanExitDialogViewModel(_services, window)).ConfigureAwait(true);
        }
    }

    // The window's own crash is told here when no state of the agent's told it yet, and a word being told is waited for.
    private async Task TellUntoldExitsAsync()
    {
        if (!_toldWindowCrash && _services.Context.PreviousCrash is { } crash)
        {
            _toldWindowCrash = true;
            _toldUncleanExits = Dialogs.ShowAsync(new UncleanExitDialogViewModel(_services, crash));
        }

        while (!_toldUncleanExits.IsCompleted)
        {
            await _toldUncleanExits.ConfigureAwait(true);
        }
    }

    private void SyncNavigation()
    {
        var section = Navigator.CurrentRoute?.Section;
        foreach (var item in Primary.Concat(Secondary))
        {
            item.IsActive = item.Section == section;
        }
    }
}
