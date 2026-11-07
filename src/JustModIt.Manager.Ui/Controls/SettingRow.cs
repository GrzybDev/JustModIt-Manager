namespace JustModIt.Manager.Ui.Controls;

/// <summary>
/// A row of a settings card: an icon in a square, a title with a line under it and, beside them, the row's control, such
/// as a switch. With <c>Classes="stacked"</c> the control goes under the texts across the row, e.g. a row of choice tiles;
/// <c>Classes="divided"</c> draws the line above a row that follows another.
/// </summary>
public sealed class SettingRow : IconTextControl;
