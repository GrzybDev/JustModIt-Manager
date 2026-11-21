using Avalonia.Media;
using JustModIt.Manager.Localization;
using JustModIt.Manager.Ui.Theme;

namespace JustModIt.Manager.Ui.Overlays;

/// <summary>A controller's button that may be mapped to a key, as its label shows it.</summary>
/// <param name="Label">The button's label, which screen readers read where an icon shows it.</param>
/// <param name="IsRound">A face button, drawn round as on the controller.</param>
/// <param name="Icon">The button's shape, drawn in place of the label; null for none.</param>
public sealed record ControllerButton(string Label, bool IsRound = false, Geometry? Icon = null)
{
    /// <summary>Whether the label shows, as no icon does instead.</summary>
    public bool ShowsLabel => Icon is null;
}

/// <summary>A key the window listens to, what it does, and the controller's buttons a player may map to it.</summary>
/// <param name="Key">The key, as its cap names it.</param>
/// <param name="Action">What it does in the window.</param>
/// <param name="Buttons">The controller's buttons that serve as an example.</param>
public sealed record ControlsRow(string Key, string Action, IReadOnlyList<ControllerButton> Buttons);

/// <summary>
/// The keys the window listens to in Game Mode, where Steam maps the controller's buttons to keys: what the window says,
/// since it cannot change Steam's mapping itself.
/// </summary>
/// <param name="openedByItself">The hint opened without the player asking, so it says where it can be found again.</param>
public sealed class ControlsHintDialogViewModel(bool openedByItself) : DialogViewModel<bool>(false)
{
    public string KeyHeading { get; } = Strings.Shell_ControlsKey.ToUpper(Text.Culture);

    public string ActionHeading { get; } = Strings.Shell_ControlsAction.ToUpper(Text.Culture);

    public string ExampleHeading { get; } = Strings.Shell_ControlsExample.ToUpper(Text.Culture);

    public IReadOnlyList<ControlsRow> Rows { get; } =
    [
        new(Strings.Shell_ControlsMoveKeys, Strings.Shell_ControlsMove, [new(Strings.Shell_ControlsDpad, Icon: Icons.GamepadDirectional)]),
        new(Strings.Shell_ControlsChooseKey, Strings.Shell_ControlsChoose, [new(Strings.Shell_ControlsButtonA, IsRound: true)]),
        new(Strings.Shell_ControlsBackKey, Strings.Shell_ControlsBack, [new(Strings.Shell_ControlsButtonB, IsRound: true)]),
        new(Strings.Shell_ControlsPreviousNextKeys, Strings.Shell_ControlsPreviousNext,
            [new(Strings.Shell_ControlsButtonL1), new(Strings.Shell_ControlsButtonR1)]),
        new(Strings.Shell_ControlsSearchKeys, Strings.Shell_ControlsSearch, [new(Strings.Shell_ControlsButtonY, IsRound: true)]),
        new(Strings.Shell_ControlsCheckNowKey, Strings.Shell_ControlsCheckNow, [new(Strings.Shell_ControlsButtonMenu)]),
        new(Strings.Shell_ControlsPointerKey, Strings.Shell_ControlsPointer,
            [new(Strings.Shell_ControlsRightTrackpad), new(Strings.Shell_ControlsButtonR2)]),
    ];

    public string? Footer { get; } = openedByItself ? Text.Format(Strings.Shell_ControlsFooter, ("controls", Strings.Shell_Controls)) : null;
}
