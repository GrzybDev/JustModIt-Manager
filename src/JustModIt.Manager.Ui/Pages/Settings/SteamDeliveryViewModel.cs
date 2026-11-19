using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JustModIt.Manager.Core.Steam;
using JustModIt.Manager.Localization;
using JustModIt.Manager.Protocol.Views;
using JustModIt.Manager.Ui.Controls;
using JustModIt.Manager.Ui.Navigation;
using JustModIt.Manager.Ui.Theme;

namespace JustModIt.Manager.Ui.Pages.Settings;

/// <summary>
/// How achievement texts reach Steam's interface, as the settings tell it: the Millennium plugin or the classic mode, how it
/// stands now, and when the classic mode's port is taken.
/// </summary>
/// <param name="services">The window's services.</param>
public sealed partial class SteamDeliveryViewModel(UiServices services) : ObservableObject
{
    private enum Standing
    {
        Working,
        Restart,
        Install,
        Blocked,
        Active,
        Waiting,
        PortTaken,
        Disabled,
        Unused,
        Idle,
        Off,
    }

    [ObservableProperty]
    public partial Geometry Icon { get; private set; } = Icons.Plug;

    [ObservableProperty]
    public partial string Title { get; private set; } = "";

    [ObservableProperty]
    public partial string Status { get; private set; } = "";

    [ObservableProperty]
    public partial Tone StatusTone { get; private set; }

    [ObservableProperty]
    public partial Geometry StatusIcon { get; private set; } = Icons.Check;

    /// <summary>Millennium's and the plugin's versions, or the classic mode's port; empty for none.</summary>
    [ObservableProperty]
    public partial string Details { get; private set; } = "";

    [ObservableProperty]
    public partial string Description { get; private set; } = "";

    /// <summary>Millennium is installed, so the classic mode is not used.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ClassicModeDescription))]
    public partial bool HasMillennium { get; private set; }

    /// <summary>Another program held the classic mode's port when Steam started.</summary>
    [ObservableProperty]
    public partial bool IsPortTaken { get; private set; }

    public string ClassicModeDescription =>
        Text.Format(Strings.Settings_ClassicModeDescription, ("millennium", HasMillennium ? "true" : "false"));

    /// <summary>Follows the way into Steam's interface as the agent's state has it.</summary>
    public void Refresh()
    {
        var state = services.Agent.State;
        var steam = state.SteamUi;
        var classic = state.Settings.AchievementClassicMode;
        HasMillennium = steam.Delivery == SteamUiDelivery.Millennium;
        IsPortTaken = steam.Delivery == SteamUiDelivery.Classic && steam.PortTaken;
        var blocked = steam.Hooks is { Achievements: false };
        // Nothing goes into Steam's folder while no game needs it, so there is nothing to wait for.
        var standing = steam.Delivery switch
        {
            SteamUiDelivery.Millennium => steam.PluginDisabled ? Standing.Disabled
                : !steam.IsNeeded ? Standing.Unused
                : blocked ? Standing.Blocked
                : steam.RestartNeeded ? Standing.Restart
                : steam.PluginVersion.Length == 0 ? Standing.Install
                : Standing.Working,
            SteamUiDelivery.Classic => !steam.IsNeeded ? Standing.Idle
                : blocked ? Standing.Blocked
                : steam.PortTaken ? Standing.PortTaken
                : steam.RestartNeeded ? Standing.Restart
                : steam.Reachable ? Standing.Active
                : Standing.Waiting,
            _ => Standing.Off,
        };
        (StatusTone, StatusIcon) = standing switch
        {
            Standing.Working or Standing.Active => (Tone.Success, Icons.Check),
            Standing.Restart => (Tone.Info, Icons.RotateCcw),
            Standing.Install or Standing.Waiting => (Tone.Neutral, Icons.Clock),
            Standing.Unused or Standing.Idle => (Tone.Neutral, Icons.Info),
            _ => (Tone.Warning, Icons.TriangleAlert),
        };
        Status = Text.Format(Strings.Settings_SteamStatus, ("status", standing.ToString()));
        var games = state.Games.Count(game => game.AchievementTexts is { IsOn: true });
        (Icon, Title, Details, Description) = steam.Delivery switch
        {
            SteamUiDelivery.Millennium => (Icons.Plug, Strings.Settings_SteamMillennium,
                Text.Format(Strings.Settings_SteamVersions, ("millennium", steam.MillenniumVersion), ("plugin", steam.PluginVersion),
                    ("hasMillennium", steam.MillenniumVersion.Length > 0 ? "true" : "false"),
                    ("hasPlugin", steam.PluginVersion.Length > 0 ? "true" : "false")),
                Text.Format(Strings.Settings_SteamMillenniumDescription, ("count", games))),
            SteamUiDelivery.Classic => (Icons.Terminal, Strings.Settings_SteamClassic, Strings.Settings_SteamPort,
                Strings.Settings_SteamClassicDescription),
            _ => (Icons.TriangleAlert, Text.Format(Strings.Settings_SteamNone, ("classic", classic ? "true" : "false")), "",
                Text.Format(Strings.Settings_SteamNoneDescription, ("classic", classic ? "true" : "false"))),
        };
    }

    [RelayCommand]
    private void OpenMillennium() => services.Links.Open(PageLinks.Millennium);
}
