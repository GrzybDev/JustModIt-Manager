using System.Collections.ObjectModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.State;
using JustModIt.Manager.Core.Steam;
using JustModIt.Manager.Localization;
using JustModIt.Manager.Protocol.Messages;
using JustModIt.Manager.Protocol.Views;
using JustModIt.Manager.Ui.Controls;
using JustModIt.Manager.Ui.Navigation;
using JustModIt.Manager.Ui.Pages.Library;
using JustModIt.Manager.Ui.Theme;

namespace JustModIt.Manager.Ui.Pages.Game;

/// <summary>What keeps a game's achievement texts from Steam's interface, said under their row with the way out of it.</summary>
/// <param name="Tone">Info, or warning when only the player can change it.</param>
/// <param name="Icon">Its icon.</param>
/// <param name="Title">What it is, in a few words.</param>
/// <param name="Text">Why, and what happens next.</param>
public sealed record AchievementTextsNotice(Tone Tone, Geometry Icon, string Title, string Text)
{
    /// <summary>Offers to start Steam again, which loads what shows the texts.</summary>
    public bool OffersRestart { get; init; }

    /// <summary>No Steam game runs, so Steam may start again now.</summary>
    public bool CanRestart { get; init; }

    /// <summary>Names the Steam game that holds the restart back; empty when none is named.</summary>
    public string Running { get; init; } = "";

    /// <summary>Leads to the settings, where the way into Steam is switched.</summary>
    public bool OffersSettings { get; init; }

    public bool IsWarning => Tone == Tone.Warning;

    public bool HasRunning => Running.Length > 0;
}

/// <summary>A translated achievement as the unfolded list shows it.</summary>
/// <param name="Name">Its name.</param>
/// <param name="Description">Its description, or that Steam keeps it hidden until the achievement is unlocked.</param>
public sealed record AchievementTextItem(string Name, string Description);

/// <summary>
/// A Steam game's achievement texts on its page: whether Steam shows them, how many are translated, what keeps them from
/// Steam's interface with the way out of it, and the translated names unfolded on request; kept for the page's life, so the
/// list stays unfolded across states.
/// </summary>
/// <param name="services">The window's services.</param>
/// <param name="key">The game the row is about.</param>
public sealed partial class AchievementTextsRow(UiServices services, GameKey key) : ObservableObject
{
    private (Guid Project, int Revision)? _listed;

    [ObservableProperty]
    public partial bool IsShown { get; private set; }

    [ObservableProperty]
    public partial string Title { get; private set; } = "";

    [ObservableProperty]
    public partial string Detail { get; private set; } = "";

    /// <summary>Green while Steam shows the texts, amber while some achievements stay untranslated.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDetailSuccess), nameof(IsDetailWarning))]
    public partial Tone DetailTone { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SwitchText), nameof(SwitchName))]
    public partial bool IsOn { get; private set; }

    /// <summary>On, with a way into Steam's interface, so the row tells what Steam shows and lists the translated names.</summary>
    [ObservableProperty]
    public partial bool ReachesSteam { get; private set; }

    /// <summary>The texts can be switched here: not while the settings turn them off for every game or Steam cannot take them.</summary>
    [ObservableProperty]
    public partial bool CanSwitch { get; private set; }

    /// <summary>What keeps the texts from Steam's interface; null when nothing does.</summary>
    [ObservableProperty]
    public partial AchievementTextsNotice? Notice { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ListToggle), nameof(ListToggleIcon))]
    public partial bool IsListShown { get; private set; }

    [ObservableProperty]
    public partial bool IsLoading { get; private set; }

    [ObservableProperty]
    public partial bool ListFailed { get; private set; }

    public ObservableCollection<AchievementTextItem> Items { get; } = [];

    public bool IsDetailSuccess => DetailTone == Tone.Success;

    public bool IsDetailWarning => DetailTone == Tone.Warning;

    public string SwitchText => Text.Format(Strings.GamePage_AchievementsSwitch, ("on", IsOn ? "true" : "false"));

    public string SwitchName => Text.Format(Strings.GamePage_AchievementsSwitchName, ("on", IsOn ? "true" : "false"));

    public string ListToggle => IsListShown ? Strings.GamePage_AchievementsHide : Strings.GamePage_AchievementsShow;

    public Geometry ListToggleIcon => IsListShown ? Icons.ChevronUp : Icons.ChevronDown;

    /// <summary>Follows the game as the agent's state has it; a game that takes no texts has no row.</summary>
    /// <param name="game">The game.</param>
    /// <param name="state">The agent's state, with the settings and the way into Steam's interface.</param>
    public void Show(GameView game, AppStateView state)
    {
        IsShown = game.AchievementTexts is not null;
        if (game.AchievementTexts is not { } texts)
        {
            return;
        }

        var counts = texts.Texts;
        var missing = Math.Max(0, counts.Total - counts.Translated);
        IsOn = texts.IsOn;
        ReachesSteam = texts.ReachesSteam;
        Title = Text.Format(Strings.GamePage_AchievementsTitle, ("on", ReachesSteam ? "true" : "false"));
        (Detail, DetailTone) = Describe(game, texts, state.SteamUi.Delivery, missing);
        CanSwitch = state.Settings.AchievementTexts && texts.State != AchievementTextsState.NoDelivery;
        Notice = Explain(texts.State, state, services.Context.InGameMode);
        if (!ReachesSteam)
        {
            IsListShown = false;
        }

        if (_listed != (texts.Project, counts.Revision))
        {
            _listed = (texts.Project, counts.Revision);
            Items.Clear();
            (IsLoading, ListFailed) = (false, false);
            if (IsListShown)
            {
                _ = LoadAsync();
            }
        }
    }

    private static (string Text, Tone Tone) Describe(GameView game, MyAchievementTexts texts, SteamUiDelivery delivery, int missing)
    {
        (string, object?)[] counts = [("translated", texts.Texts.Translated), ("total", texts.Texts.Total), ("team", texts.TeamName)];
        if (!texts.ReachesSteam)
        {
            // The texts follow the project chosen for the game, which the page compares when several teams offer one.
            var chosen = game.Projects.Count > 1 && game.Project?.ProjectId == texts.Project;
            return (Text.Format(chosen ? Strings.GamePage_AchievementsChosen : Strings.GamePage_AchievementsOffered,
                [.. counts, ("delivers", texts.State == AchievementTextsState.NoDelivery ? "false" : "true")]), Tone.Neutral);
        }

        var shown = texts.State == AchievementTextsState.On;
        return missing > 0 ? (Text.Format(Strings.GamePage_AchievementsMissing, [.. counts, ("missing", missing)]), Tone.Warning)
            : texts.WithInstall ? (Text.Format(Strings.GamePage_AchievementsWithInstall, counts), shown ? Tone.Success : Tone.Neutral)
            : (Text.Format(Strings.GamePage_AchievementsVia, [.. counts, ("delivery", delivery.ToString())]), shown ? Tone.Success : Tone.Neutral);
    }

    // Restarting Steam in Game Mode ends the window with it, so there the row only says that Steam has to start again.
    private static AchievementTextsNotice? Explain(AchievementTextsState textsState, AppStateView state, bool inGameMode)
    {
        var steam = state.SteamUi;
        if (!state.Settings.AchievementTexts)
        {
            return new AchievementTextsNotice(Tone.Info, Icons.Info, Strings.GamePage_AchievementsAllOffTitle, Strings.GamePage_AchievementsAllOff)
            {
                OffersSettings = true,
            };
        }

        var blocked = textsState == AchievementTextsState.RestartBlocked && !inGameMode;
        return textsState switch
        {
            AchievementTextsState.NoDelivery when steam.PluginDisabled => new AchievementTextsNotice(Tone.Warning, Icons.TriangleAlert,
                Strings.GamePage_AchievementsPluginOffTitle, Strings.GamePage_AchievementsPluginOff) { OffersSettings = true },
            AchievementTextsState.NoDelivery => state.Settings.AchievementClassicMode
                ? new AchievementTextsNotice(Tone.Warning, Icons.TriangleAlert, Strings.GamePage_AchievementsNoWayTitle,
                    Strings.GamePage_AchievementsNoWay) { OffersSettings = true }
                : new AchievementTextsNotice(Tone.Warning, Icons.TriangleAlert, Strings.GamePage_AchievementsClassicOffTitle,
                    Strings.GamePage_AchievementsClassicOff) { OffersSettings = true },
            AchievementTextsState.WaitingForSteamRestart or AchievementTextsState.RestartBlocked => new AchievementTextsNotice(Tone.Info,
                Icons.RotateCcw, Strings.GamePage_AchievementsRestartTitle, blocked
                    ? Strings.GamePage_AchievementsRestartBlocked
                    : Text.Format(Strings.GamePage_AchievementsRestart, ("delivery", steam.Delivery.ToString())))
            {
                OffersRestart = !inGameMode,
                CanRestart = textsState == AchievementTextsState.WaitingForSteamRestart,
                Running = steam.RunningGameName is { } running && blocked
                    ? Text.Format(Strings.GamePage_AchievementsRunning, ("game", running))
                    : "",
            },
            AchievementTextsState.HookBlocked => new AchievementTextsNotice(Tone.Warning, Icons.TriangleAlert,
                Strings.GamePage_AchievementsHookBlockedTitle,
                Text.Format(Strings.GamePage_AchievementsHookBlocked, ("delivery", steam.Delivery.ToString()))),
            AchievementTextsState.SteamInOtherLanguage => new AchievementTextsNotice(Tone.Info, Icons.Info,
                Strings.GamePage_AchievementsOtherLanguageTitle, Strings.GamePage_AchievementsOtherLanguage),
            _ => null,
        };
    }

    [RelayCommand]
    private async Task SwitchAsync() => await AgentRequests.SendAsync(services, new SetAchievementTexts(key, !IsOn)).ConfigureAwait(true);

    [RelayCommand]
    private async Task RestartSteamAsync() =>
        await AgentRequests.SendAsync(services, new RestartSteam(), Strings.GamePage_RestartSteamFailed).ConfigureAwait(true);

    [RelayCommand]
    private void OpenSettings() => services.Navigator.Navigate(new SettingsRoute());

    [RelayCommand]
    private void ToggleList()
    {
        IsListShown = !IsListShown;
        if (IsListShown && Items.Count == 0 && !IsLoading)
        {
            _ = LoadAsync();
        }
    }

    // Texts of another project or revision that arrive late are dropped, since the row already follows the newer ones.
    private async Task LoadAsync()
    {
        var listed = _listed;
        (IsLoading, ListFailed) = (true, false);
        var answer = await AgentRequests.AskQuietlyAsync(services.Agent, new GetAchievementTexts(key)).ConfigureAwait(true);
        if (listed != _listed)
        {
            return;
        }

        IsLoading = false;
        if (answer is not AchievementTextsResult { Texts: { } texts })
        {
            ListFailed = true;
            return;
        }

        foreach (var achievement in texts.Achievements)
        {
            Items.Add(new AchievementTextItem(achievement.Name, achievement.Hidden ? Strings.GamePage_AchievementHidden : achievement.Description));
        }
    }
}
