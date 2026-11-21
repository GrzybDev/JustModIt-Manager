using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JustModIt.Manager.Localization;
using JustModIt.Manager.Protocol.Messages;
using JustModIt.Manager.Protocol.Views;
using JustModIt.Manager.Ui.Controls;
using JustModIt.Manager.Ui.Navigation;
using JustModIt.Manager.Ui.Pages.Creator;
using JustModIt.Manager.Ui.Pages.Library;
using JustModIt.Manager.Ui.Theme;

namespace JustModIt.Manager.Ui.Pages.Settings;

/// <summary>
/// The app's shortcut in Steam's library as the window shows it: how it stands, the one thing to do about it, and the steps
/// to add it by hand.
/// </summary>
/// <param name="services">The window's services.</param>
public sealed partial class SteamShortcutViewModel(UiServices services) : ObservableObject
{
    /// <summary>How the shortcut stands; null where the app offers none.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOffered), nameof(HasAction), nameof(IsAdding))]
    [NotifyCanExecuteChangedFor(nameof(ActCommand))]
    public partial SteamShortcutState? State { get; private set; }

    public bool IsOffered => State is not null;

    /// <summary>What the shortcut is for, by whether the system offers a gamescope session.</summary>
    [ObservableProperty]
    public partial string Description { get; private set; } = "";

    /// <summary>How the shortcut stands, in a sentence; null while there is none to add.</summary>
    [ObservableProperty]
    public partial string? Status { get; private set; }

    [ObservableProperty]
    public partial Tone StatusTone { get; private set; }

    [ObservableProperty]
    public partial Geometry StatusIcon { get; private set; } = Icons.Info;

    /// <summary>The button's words: add, add again, try again or start Steam; empty when there is nothing to do.</summary>
    [ObservableProperty]
    public partial string ActionText { get; private set; } = "";

    [ObservableProperty]
    public partial Geometry? ActionIcon { get; private set; }

    public bool HasAction => State is not (null or SteamShortcutState.Added);

    /// <summary>Steam is adding the shortcut, so the button waits.</summary>
    public bool IsAdding => State == SteamShortcutState.Adding;

    /// <summary>The steps to add the shortcut by hand are offered, after a failure or where no gamescope session waits for it.</summary>
    [ObservableProperty]
    public partial bool HasManual { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ManualToggleIcon))]
    public partial bool IsManualShown { get; private set; }

    public Geometry ManualToggleIcon => IsManualShown ? Icons.ChevronUp : Icons.ChevronDown;

    /// <summary>The file the steps pick, written from the home folder.</summary>
    [ObservableProperty]
    public partial string ManualFile { get; private set; } = "";

    /// <summary>
    /// How the shortcut stands for this window: one that Steam started has it in Steam's library, which Steam's file may not
    /// show yet.
    /// </summary>
    /// <param name="shortcut">How the agent found it; null where the app offers none.</param>
    /// <param name="context">Tells whether Steam started the window.</param>
    public static SteamShortcutState? StateOf(SteamShortcutView? shortcut, UiContext context) =>
        shortcut is null ? null : context.StartedBySteam ? SteamShortcutState.Added : shortcut.State;

    /// <summary>Follows the shortcut as the agent's state has it.</summary>
    public void Refresh()
    {
        var shortcut = services.Agent.State.SteamShortcut;
        State = StateOf(shortcut, services.Context);
        if (shortcut is null)
        {
            return;
        }

        Description = Text.Format(Strings.SteamShortcut_Description, ("session", shortcut.HasGameSession ? "true" : "false"));
        (StatusIcon, StatusTone, Status) = State switch
        {
            SteamShortcutState.Adding => (Icons.LoaderCircle, Tone.Info, Strings.SteamShortcut_Adding),
            SteamShortcutState.Added => (Icons.Check, Tone.Success, Strings.SteamShortcut_Added),
            SteamShortcutState.SteamNotRunning => (Icons.TriangleAlert, Tone.Warning, Strings.SteamShortcut_SteamNotRunning),
            SteamShortcutState.Failed => (Icons.CircleX, Tone.Danger, Text.Format(Strings.SteamShortcut_Failed,
                ("seconds", (int)SteamShortcutView.AddTimeLimit.TotalSeconds))),
            SteamShortcutState.Elsewhere => (Icons.TriangleAlert, Tone.Warning, Text.Format(Strings.SteamShortcut_Elsewhere,
                ("copy", shortcut.OtherMissing ? "missing" : "other"),
                ("file", CreatorWording.Wrappable(GameWording.FromHome(shortcut.Other ?? ""))))),
            _ => (Icons.Info, Tone.Neutral, (string?)null),
        };
        (ActionText, ActionIcon) = State switch
        {
            SteamShortcutState.Adding => (Strings.SteamShortcut_AddingButton, Icons.LoaderCircle),
            SteamShortcutState.SteamNotRunning => (Strings.SteamShortcut_StartSteam, null),
            SteamShortcutState.Failed => (Strings.Library_Retry, Icons.RefreshCw),
            SteamShortcutState.Elsewhere => (Strings.SteamShortcut_AddAgain, Icons.Plus),
            SteamShortcutState.Missing => (Strings.SteamShortcut_Add, Icons.Plus),
            _ => ("", (Geometry?)null),
        };
        HasManual = State == SteamShortcutState.Failed || (State == SteamShortcutState.Missing && !shortcut.HasGameSession);
        // Pasted into Steam's file dialog, so unlike a sentence's path it carries no invisible places to break the line.
        ManualFile = GameWording.FromHome(shortcut.Program);
    }

    [RelayCommand]
    private void ToggleManual() => IsManualShown = !IsManualShown;

    [RelayCommand(CanExecute = nameof(CanAct))]
    private async Task ActAsync()
    {
        if (State == SteamShortcutState.SteamNotRunning)
        {
            services.Links.Open(PageLinks.SteamClient);
        }
        else
        {
            await AgentRequests.SendAsync(services, new AddToSteam()).ConfigureAwait(true);
        }
    }

    private bool CanAct() => HasAction && !IsAdding;
}
