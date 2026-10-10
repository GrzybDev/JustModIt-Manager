using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using JustModIt.Manager.Core.Settings;
using JustModIt.Manager.Localization;
using JustModIt.Manager.Protocol.Messages;
using JustModIt.Manager.Protocol.Transport;
using JustModIt.Manager.Protocol.Views;
using JustModIt.Manager.Ui.Connection;
using JustModIt.Manager.Ui.Theme;

namespace JustModIt.Manager.Ui.Pages.Onboarding;

/// <summary>A step of the first-run setup, shown by the view registered for its type; the choices live in the setup.</summary>
/// <param name="setup">The setup the step belongs to.</param>
/// <param name="title">The step's name, for the progress dots and screen readers.</param>
public abstract partial class OnboardingStep(OnboardingViewModel setup, string title) : ObservableObject
{
    public OnboardingViewModel Setup { get; } = setup;

    public string Title { get; } = title;

    [ObservableProperty]
    public partial bool IsCurrent { get; set; }

    /// <summary>The step was passed on the way to the one shown.</summary>
    [ObservableProperty]
    public partial bool IsDone { get; set; }

    /// <summary>A heading over a group of choices, in capitals, since text has no transform of its own.</summary>
    /// <param name="text">The heading's text.</param>
    protected static string Overline(string text) => text.ToUpper(Text.Culture);
}

/// <summary>The welcome: the app's art and what the app does.</summary>
/// <param name="setup">The setup the step belongs to.</param>
public sealed class WelcomeStep(OnboardingViewModel setup) : OnboardingStep(setup, Strings.Onboarding_WelcomeStep)
{
    /// <summary>The app's name as the welcome's second line says it.</summary>
    public string Name { get; } = Text.Format(Strings.Onboarding_WelcomeName, ("product", setup.ProductName));

    public string Lead { get; } = Text.Format(Strings.Onboarding_WelcomeLead, ("product", setup.ProductName));
}

/// <summary>How the app works, and what it does without asking.</summary>
/// <param name="setup">The setup the step belongs to.</param>
public sealed class AutomationStep(OnboardingViewModel setup) : OnboardingStep(setup, Strings.Onboarding_AutomationTitle)
{
    public string Lead { get; } = Text.Format(Strings.Onboarding_AutomationLead, ("product", setup.ProductName));

    public string Heading { get; } = Overline(Strings.Onboarding_AutomationHeading);
}

/// <summary>Starting with the system and what closing the window does.</summary>
/// <param name="setup">The setup the step belongs to.</param>
public sealed class BackgroundStep(OnboardingViewModel setup) : OnboardingStep(setup, Strings.Onboarding_BackgroundTitle)
{
    public string Lead { get; } = Text.Format(Strings.Onboarding_BackgroundLead, ("product", setup.ProductName));

    public string Heading { get; } = Overline(Strings.Onboarding_CloseHeading);

    public string ExitHint { get; } = Text.Format(Strings.Onboarding_ExitHint, ("exit", Strings.TrayExit));
}

/// <summary>Whether the package creator joins the sidebar.</summary>
/// <param name="setup">The setup the step belongs to.</param>
public sealed class CreatorStep(OnboardingViewModel setup) : OnboardingStep(setup, Strings.Onboarding_CreatorTitle)
{
    public string Lead { get; } = Text.Format(Strings.Onboarding_CreatorLead, ("creator", Strings.Shell_Creator));

    public string PlayerDescription { get; } =
        Text.Format(Strings.Onboarding_PlayerTileDescription, ("library", Strings.Shell_Library), ("catalogue", Strings.Shell_Catalogue));

    public string CreatorDescription { get; } = Text.Format(Strings.Onboarding_CreatorTileDescription, ("creator", Strings.Shell_Creator));
}

/// <summary>A store as the last step lists it: what detection found there.</summary>
/// <param name="Store">The store's id, for its mark.</param>
/// <param name="Name">The store's or its launcher's name.</param>
/// <param name="Detail">How many libraries and games were found, or that none were.</param>
/// <param name="IsFound">Whether the store is on this computer.</param>
public sealed record StoreLine(string Store, string Name, string Detail, bool IsFound)
{
    /// <summary>Words what detection found for one store.</summary>
    /// <param name="store">The store's part of the detection report.</param>
    public static StoreLine From(StoreDetectionView store)
    {
        ArgumentNullException.ThrowIfNull(store);
        var name = store.Launcher is { Length: > 0 } launcher ? launcher
            : store.Name is { Length: > 0 } site ? site
            : store.Store switch
            {
                "steam" => Strings.Onboarding_StoreSteam,
                "gog" => Strings.Onboarding_StoreGog,
                "epic" => Strings.Onboarding_StoreEpic,
                _ => store.Store,
            };
        var detail = !store.IsFound ? Strings.Onboarding_StoreMissing
            : store.ScanFailed ? Strings.Onboarding_StoreUnreadable
            : Text.Format(Strings.Onboarding_StoreFound, ("libraries", store.Libraries.Count), ("games", store.GameCount));
        return new StoreLine(store.Store, name, detail, store.IsFound && !store.ScanFailed);
    }
}

/// <summary>A game the last step shows the cover of, since projects are on offer for it.</summary>
/// <param name="Name">The game's name.</param>
/// <param name="Cover">The cover's address; empty for none.</param>
public sealed record MatchedGame(string Name, string Cover);

/// <summary>One of the choices the last step sums up.</summary>
/// <param name="Icon">The choice's icon.</param>
/// <param name="Label">What was chosen about.</param>
/// <param name="Value">What was chosen.</param>
/// <param name="IsActive">The choice turns something on, which its icon shows in the accent.</param>
public sealed record SummaryItem(Geometry Icon, string Label, string Value, bool IsActive);

/// <summary>The last step: what the search found on this computer and the choices made, before the setup completes.</summary>
public sealed partial class ReadyStep : OnboardingStep
{
    private const int CoverCount = 7;

    private readonly IAgent _agent;
    private DateTimeOffset? _reportFor;

    /// <summary>Creates the step, which follows the agent's state while the setup lasts.</summary>
    /// <param name="setup">The setup the step belongs to.</param>
    /// <param name="agent">The agent whose state and detection report the step shows.</param>
    public ReadyStep(OnboardingViewModel setup, IAgent agent)
        : base(setup, Strings.Onboarding_ReadyTitle)
    {
        _agent = agent;
        Lead = Text.Format(Strings.Onboarding_ReadyLead, ("start", Strings.Onboarding_Start));
    }

    public string Lead { get; }

    public string ComputerHeading { get; } = Overline(Strings.Onboarding_ThisComputer);

    public string SettingsHeading { get; } = Overline(Strings.Onboarding_ChosenSettings);

    /// <summary>The stores detection knows, once the first scan has read them.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStores))]
    public partial IReadOnlyList<StoreLine> Stores { get; private set; } = [];

    public bool HasStores => Stores.Count > 0;

    /// <summary>The stores are still being scanned or the catalogue read, so what fits is not known yet.</summary>
    [ObservableProperty]
    public partial bool IsSearching { get; private set; } = true;

    /// <summary>What the search found, once it is done.</summary>
    [ObservableProperty]
    public partial string Result { get; private set; } = "";

    [ObservableProperty]
    public partial IReadOnlyList<MatchedGame> Matches { get; private set; } = [];

    [ObservableProperty]
    public partial IReadOnlyList<SummaryItem> Summary { get; private set; } = [];

    /// <summary>Takes the agent's newest state, and reads the detection report again after a new scan while shown.</summary>
    public void Refresh()
    {
        var state = _agent.State;
        var scanned = state.Library.ScannedAt is not null && !state.Library.IsCached;
        var catalogue = state.Catalogue;
        var siteUnavailable = catalogue.FetchedAt is null && catalogue.Failure is not null;
        IsSearching = !scanned || (catalogue.FetchedAt is null && !siteUnavailable);
        var matched = state.Games.Where(game => !game.IsHidden && game.Projects.Count > 0).ToList();
        Matches = [.. matched.Take(CoverCount).Select(game => new MatchedGame(game.Name, (game.Project ?? game.Projects[0]).Cover))];
        Result = siteUnavailable ? Strings.Onboarding_SiteUnavailable
            : matched.Count == 0 ? Strings.Onboarding_MatchedNone
            : Text.Format(Strings.Onboarding_Matched, ("count", matched.Count));
        if (IsCurrent && scanned && state.Library.ScannedAt != _reportFor)
        {
            _ = LoadReportAsync(state.Library.ScannedAt);
        }

        RefreshSummary();
    }

    /// <summary>Sums up the choices again after one of them changed.</summary>
    public void RefreshSummary()
    {
        var setup = Setup;
        var automation = setup.Automation switch
        {
            AutomationChoice.Everything => (Icons.Zap, Strings.Onboarding_AutomateEverything),
            AutomationChoice.UpdatesOnly => (Icons.RefreshCw, Strings.Onboarding_AutomateUpdates),
            AutomationChoice.Nothing => (Icons.Hand, Strings.Onboarding_AutomateNothing),
            _ => (Icons.ListChecks, Strings.Onboarding_AutomateCustom),
        };
        var close = setup.CloseBehavior switch
        {
            CloseBehavior.MinimizeToTray => (Icons.MonitorDown, Strings.Shell_CloseToTray),
            CloseBehavior.Exit => (Icons.Power, Strings.Shell_CloseExit),
            _ => (Icons.CircleHelp, Strings.Onboarding_CloseAsk),
        };
        var backups = _agent.State.Settings.BackupLocation switch
        {
            BackupLocation.AppData => Strings.Onboarding_BackupsInAppData,
            BackupLocation.CustomFolder => Strings.Onboarding_BackupsInCustomFolder,
            _ => Strings.Onboarding_BackupsInGameFolders,
        };
        Summary =
        [
            new(automation.Item1, Strings.Onboarding_SummaryAutomation, automation.Item2, setup.Automation != AutomationChoice.Nothing),
            new(Icons.Bell, Strings.Onboarding_SummaryNotifications,
                setup.ShowNotifications ? Strings.Onboarding_NotificationsOn : Strings.Onboarding_NotificationsOff, setup.ShowNotifications),
            new(Icons.Sunrise, Strings.Onboarding_SummaryStart, setup.StartWithSystem ? Strings.Onboarding_On : Strings.Onboarding_Off,
                setup.StartWithSystem),
            new(close.Item1, Strings.Onboarding_CloseHeading, close.Item2, true),
            new(Icons.WandSparkles, Strings.Onboarding_CreatorTitle, setup.CreatorMode ? Strings.Onboarding_On : Strings.Onboarding_Off,
                setup.CreatorMode),
            new(Icons.Folder, Strings.Onboarding_SummaryBackups, backups, false),
        ];
    }

    private async Task LoadReportAsync(DateTimeOffset? scannedAt)
    {
        _reportFor = scannedAt;
        try
        {
            if (await _agent.RequestAsync(new GetDetectionReport()).ConfigureAwait(true) is DetectionReportResult result)
            {
                Stores = [.. result.Report.Stores.Select(StoreLine.From)];
            }
        }
        // The stores stay as they were; the next scan asks again.
        catch (Exception exception) when (exception is IOException or TimeoutException or RequestFailedException)
        {
        }
    }
}
