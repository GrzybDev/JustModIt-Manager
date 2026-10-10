using System.ComponentModel;
using System.Diagnostics;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JustModIt.Manager.Localization;
using JustModIt.Manager.Protocol.Activation;
using JustModIt.Manager.Protocol.Messages;
using JustModIt.Manager.Ui.Connection;
using JustModIt.Manager.Ui.Images;
using JustModIt.Manager.Ui.Navigation;
using JustModIt.Manager.Ui.Overlays;
using JustModIt.Manager.Protocol.Views;
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
/// The window around the pages: the title bar's name and way back, the sidebar, the status card, the first-run setup
/// that takes the whole window until it is done, and the calm state shown while the agent cannot be reached.
/// </summary>
public sealed partial class ShellViewModel : ObservableObject, IDisposable
{
    /// <summary>How long the window waits for the agent before it offers to start it again.</summary>
    public static readonly TimeSpan SlowConnection = TimeSpan.FromSeconds(8);

    private readonly UiServices _services;
    private readonly Func<UiServices, PageViewModel>? _firstRun;
    private readonly NavigationItem _creator;
    private readonly DispatcherTimer _slowConnection;

    /// <summary>Creates the shell, which opens the player's games or the place the window was started for.</summary>
    /// <param name="services">What the shell and its pages talk to.</param>
    /// <param name="firstRun">The first-run setup's page; null when the app has none.</param>
    public ShellViewModel(UiServices services, Func<UiServices, PageViewModel>? firstRun = null)
    {
        ArgumentNullException.ThrowIfNull(services);
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
        _slowConnection = new DispatcherTimer { Interval = SlowConnection };
        _slowConnection.Tick += (_, _) =>
        {
            _slowConnection.Stop();
            IsSlowToConnect = true;
        };

        Navigator.Navigated += (_, _) => SyncNavigation();
        Agent.PropertyChanged += OnAgentChanged;
        Agent.NavigationRequested += OnNavigationRequested;
        Agent.Noticed += OnNoticed;
        CloseFlow = new CloseFlow(services.Agent, services.Navigator, services.Dialogs);
        Navigator.Open(services.Context.Launch.Target is { } target ? AgentLinks.RoutesFor(target, creatorMode: false) : [new LibraryRoute()]);
        ApplyLink();
        ApplyState();
    }

    public Navigator Navigator => _services.Navigator;

    public IAgent Agent => _services.Agent;

    public DialogHost Dialogs => _services.Dialogs;

    public ToastHost Toasts => _services.Toasts;

    public IImageLoader Images => _services.Images;

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

    /// <summary>The window waits for the agent: the calm connecting state takes the place of the pages.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsPages))]
    public partial bool IsConnecting { get; private set; } = true;

    /// <summary>The agent is being replaced by an update; the window reconnects once it is back.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ConnectingTitle), nameof(ConnectingMessage))]
    public partial bool IsUpdating { get; private set; }

    public string ConnectingTitle => IsUpdating ? Strings.Shell_UpdatingTitle : Strings.Shell_ConnectingTitle;

    public string ConnectingMessage => IsUpdating ? Strings.Shell_UpdatingMessage : Strings.Shell_ConnectingMessage;

    /// <summary>The agent has not answered for a while, so the window offers to start it again.</summary>
    [ObservableProperty]
    public partial bool IsSlowToConnect { get; private set; }

    /// <summary>The first-run setup, shown instead of the sidebar and the pages until it is done; null once it is.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsPages))]
    public partial PageViewModel? FirstRun { get; private set; }

    public bool ShowsPages => !IsConnecting && FirstRun is null;

    public void Dispose()
    {
        _slowConnection.Stop();
        Agent.PropertyChanged -= OnAgentChanged;
        Agent.NavigationRequested -= OnNavigationRequested;
        Agent.Noticed -= OnNoticed;
        Status.Dispose();
        (FirstRun as IDisposable)?.Dispose();
    }

    [RelayCommand]
    private void Open(NavigationItem item) => Navigator.Navigate(item.Route);

    // The agent is this same program in the background; started this way it takes over and the window reconnects to it.
    [RelayCommand]
    private void StartAgent()
    {
        IsSlowToConnect = false;
        _slowConnection.Start();
        if (Environment.ProcessPath is { } executable)
        {
            _ = Task.Run(() =>
            {
                using var process = Process.Start(new ProcessStartInfo(executable, LaunchArguments.BackgroundSwitch) { UseShellExecute = false });
            });
        }
    }

    private void OnAgentChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(IAgent.Link):
                ApplyLink();
                break;
            case nameof(IAgent.State) or nameof(IAgent.HasState):
                ApplyState();
                break;
        }
    }

    private void OnNavigationRequested(object? sender, NavigationTarget target) =>
        Navigator.Open(AgentLinks.RoutesFor(target, Agent.State.Settings.CreatorMode));

    private void OnNoticed(object? sender, NoticeView notice) => Toasts.Show(NoticeToasts.Create(notice, about =>
        Navigator.Open(AgentLinks.RoutesFor(new GameTarget(about.Game!.Value.Store.Value, about.Game.Value.StoreGameId),
            Agent.State.Settings.CreatorMode))));

    private void ApplyLink()
    {
        var connected = Agent.Link == AgentLink.Connected && Agent.HasState;
        IsConnecting = !connected;
        IsUpdating = Agent.Link == AgentLink.Incompatible;
        if (connected)
        {
            _slowConnection.Stop();
            IsSlowToConnect = false;
        }
        else if (!_slowConnection.IsEnabled && !IsSlowToConnect)
        {
            _slowConnection.Start();
        }
    }

    private void ApplyState()
    {
        ApplyLink();
        if (!Agent.HasState)
        {
            return;
        }

        var settings = Agent.State.Settings;
        _creator.IsVisible = settings.CreatorMode;
        if (!settings.CreatorMode && Navigator.CurrentRoute?.Section == ShellSection.Creator)
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
